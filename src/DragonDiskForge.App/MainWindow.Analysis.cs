using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DragonDiskForge.App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _analysisCts;

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || sender is not Button button) return;
        if (_analysisCts is not null) { _analysisCts.Cancel(); return; }
        var path = _current.Path;
        using var cts = new CancellationTokenSource();
        _analysisCts = cts;
        button.Content = "Cancel analysis";
        try
        {
            var report = await Task.Run(() => new ImageReportService(_providerRegistry).AnalyzeAsync(path, cts.Token), cts.Token);
            if (!IsCurrentImage(path)) return;
            var text = new TextBox
            {
                Text = ImageReportService.ToText(report), IsReadOnly = true, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap, MinWidth = 400, MaxWidth = 740, MaxHeight = 540
            };
            AutomationProperties.SetName(text, "Image analysis report");
            var dialog = new ContentDialog
            {
                XamlRoot = RootLayout.XamlRoot, Title = "Image analysis", Content = text,
                PrimaryButtonText = "Save JSON report", CloseButtonText = "Close"
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(path) + "-report" };
                picker.FileTypeChoices.Add("JSON report", new List<string> { ".json" });
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                if (file is not null) await File.WriteAllTextAsync(file.Path, ImageReportService.ToJson(report), cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowDialogAsync("Analysis failed", ShortMessage(ex.Message)); }
        finally
        {
            if (ReferenceEquals(_analysisCts, cts)) _analysisCts = null;
            button.Content = "Analyze";
        }
    }

    public Task OpenPathAsync(string path) => LoadImageAsync(path);
}
