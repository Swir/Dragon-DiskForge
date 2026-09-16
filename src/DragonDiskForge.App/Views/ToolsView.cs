using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DragonDiskForge.App.Views;

public sealed class ToolsView : UserControl
{
    private readonly nint _window;
    private readonly ComboBox _operation = new() { Header = "Operation", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _source = new() { Header = "Source file", PlaceholderText = "Choose a file" };
    private readonly TextBox _destination = new() { Header = "Output file or new folder", PlaceholderText = "Existing files are never overwritten" };
    private readonly NumberBox _size = new() { Header = "Size / maximum decompressed size (MiB)", Value = 1024, Minimum = 1, Maximum = 16777216, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
    private readonly TextBox _expected = new() { Header = "Expected checksum (optional)", PlaceholderText = "Paste a SHA-256 or SHA-512 checksum to compare" };
    private readonly TextBox _result = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100 };
    private readonly Button _run = new() { Content = "Run", HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _cancel = new() { Content = "Cancel", IsEnabled = false };
    private CancellationTokenSource? _cts;

    public ToolsView(nint window)
    {
        _window = window;
        foreach (var name in new[] { "SHA-256", "SHA-512", "Create empty RAW image", "Compress to GZip", "Decompress GZip", "Split image", "Join split image" }) _operation.Items.Add(name);
        _operation.SelectedIndex = 0;
        var panel = new StackPanel { Spacing = 14, Padding = new Thickness(30), MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = "Image tools", FontSize = 30 });
        panel.Children.Add(new TextBlock { Text = "Inspect checksums and manage image files. Operations keep the source unchanged.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_operation);
        panel.Children.Add(_source);
        var browse = new Button { Content = "Choose source" };
        browse.Click += ChooseSource;
        panel.Children.Add(browse);
        panel.Children.Add(_destination);
        var chooseOutput = new Button { Content = "Choose output" };
        chooseOutput.Click += ChooseOutput;
        panel.Children.Add(chooseOutput);
        panel.Children.Add(_size);
        panel.Children.Add(_expected);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(_run);
        actions.Children.Add(_cancel);
        panel.Children.Add(actions);
        panel.Children.Add(_progress);
        panel.Children.Add(_result);
        _run.Click += Run;
        _cancel.Click += (_, _) => _cts?.Cancel();
        _operation.SelectionChanged += (_, _) => UpdateFields();
        Unloaded += (_, _) => _cts?.Cancel();
        Content = panel;
        UpdateFields();
    }

    private void UpdateFields()
    {
        var index = _operation.SelectedIndex;
        _source.IsEnabled = index != 2 && _cts is null;
        _destination.IsEnabled = index >= 2 && _cts is null;
        _size.Visibility = index is 2 or 4 or 5 ? Visibility.Visible : Visibility.Collapsed;
        _expected.Visibility = index < 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ChooseSource(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, _window);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) _source.Text = file.Path;
        }
        catch (Exception ex) { _result.Text = ex.Message; }
    }

    private async void ChooseOutput(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        try
        {
            if (_operation.SelectedIndex == 5)
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                InitializeWithWindow.Initialize(picker, _window);
                var folder = await picker.PickSingleFolderAsync();
                if (folder is not null) _destination.Text = Path.Combine(folder.Path, Path.GetFileName(_source.Text) + "-parts");
                return;
            }
            var outputFolderPicker = new FolderPicker();
            outputFolderPicker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(outputFolderPicker, _window);
            var outputFolder = await outputFolderPicker.PickSingleFolderAsync();
            if (outputFolder is not null)
                _destination.Text = Path.Combine(outputFolder.Path, _operation.SelectedIndex == 3 ? "image.gz" : "image.img");
        }
        catch (Exception ex) { _result.Text = ex.Message; }
    }

    private async void Run(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        using var cts = new CancellationTokenSource();
        _cts = cts;
        _run.IsEnabled = false;
        _cancel.IsEnabled = true;
        _operation.IsEnabled = false;
        UpdateFields();
        var operation = _operation.SelectedIndex;
        var source = _source.Text.Trim();
        var destination = _destination.Text.Trim();
        var expected = _expected.Text.Trim();
        var sizeValue = _size.Value;
        _result.Text = "Working...";
        _progress.Value = 0;
        var progress = new Progress<double>(x => _progress.Value = Math.Clamp(x * 100, 0, 100));
        try
        {
            var size = operation is 2 or 4 or 5
                ? checked((long)(double.IsFinite(sizeValue) && sizeValue > 0 ? sizeValue * 1024 * 1024 : throw new ArgumentException("Enter a positive size.")))
                : 0;
            var result = await Task.Run(async () =>
            {
                var files = new ImageFileToolsService();
                switch (operation)
                {
                    case 0:
                    case 1:
                        var hash = await new ImageVerificationService().ComputeHashAsync(source, operation == 0 ? "sha256" : "sha512", progress, cts.Token);
                        return expected.Length == 0 ? hash : (string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase) ? "MATCH" : "MISMATCH") + Environment.NewLine + hash;
                    case 2: await files.CreateRawAsync(destination, size, cts.Token); break;
                    case 3: await files.CompressAsync(source, destination, progress, cts.Token); break;
                    case 4: await files.DecompressAsync(source, destination, size, progress, cts.Token); break;
                    case 5: await files.SplitAsync(source, destination, size, progress, cts.Token); break;
                    case 6: await files.JoinAsync(source, destination, progress, cts.Token); break;
                }
                return "Completed: " + destination;
            }, cts.Token);
            _result.Text = result;
            _progress.Value = 100;
        }
        catch (OperationCanceledException) { _result.Text = "Cancelled. No incomplete output was committed."; }
        catch (Exception ex) { _result.Text = "Failed: " + ex.Message; }
        finally
        {
            _cts = null;
            _run.IsEnabled = true;
            _cancel.IsEnabled = false;
            _operation.IsEnabled = true;
            UpdateFields();
        }
    }
}
