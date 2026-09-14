using System.Collections.ObjectModel;
using System.Diagnostics;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DragonDiskForge.App.Views;

public sealed partial class ExplorerView : UserControl
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".msi", ".msix", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".scr", ".lnk"
    };

    private readonly IExplorerService _explorer = new MountedFileSystemExplorerService();
    private readonly ObservableCollection<ExplorerEntryViewModel> _items = new();
    private readonly nint _windowHandle;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _copyCts;
    private string? _rootPath;
    private string? _currentPath;
    private string? _imagePath;
    private bool _searchMode;

    public ExplorerView(nint windowHandle)
    {
        _windowHandle = windowHandle;
        InitializeComponent();
        ExplorerList.ItemsSource = _items;
        Unloaded += ExplorerView_Unloaded;
        UpdateActionButtons();
    }

    public bool HasRoot => !string.IsNullOrWhiteSpace(_rootPath);

    public async Task OpenVolumeAsync(string rootPath, string imagePath)
    {
        _loadCts?.Cancel();
        _copyCts?.Cancel();

        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            ShowNoMountedVolume("The mounted drive is no longer available. Refresh Mounted and try again.");
            return;
        }

        _rootPath = root;
        _currentPath = root;
        _imagePath = imagePath;
        _searchMode = false;
        RootDescriptionText.Text = $"{Path.GetFileName(imagePath)} • live mounted volume {root}";
        SearchBox.IsEnabled = true;
        SearchButton.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        await LoadDirectoryAsync(root);
    }

    public void ShowNoMountedVolume(string? message = null)
    {
        _loadCts?.Cancel();
        _copyCts?.Cancel();
        _rootPath = null;
        _currentPath = null;
        _imagePath = null;
        _searchMode = false;
        _items.Clear();
        ExplorerList.SelectedItem = null;
        RootDescriptionText.Text = "Choose a mounted ISO, VHD or VHDX to begin.";
        AddressText.Text = "No mounted volume selected";
        SearchBox.Text = string.Empty;
        SearchBox.IsEnabled = false;
        SearchButton.IsEnabled = false;
        ClearSearchButton.Visibility = Visibility.Collapsed;
        UpButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        ItemCountText.Text = "0 items";
        EmptyTitleText.Text = "No mounted volume selected";
        EmptyDescriptionText.Text = message ?? "Mount an ISO, VHD or VHDX and open it from the Mounted dashboard.";
        EmptyState.Visibility = Visibility.Visible;
        ExplorerList.Visibility = Visibility.Collapsed;
        UpdateActionButtons();
    }

    private async Task LoadDirectoryAsync(string directoryPath)
    {
        if (_rootPath is null)
            return;

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        BusyRing.IsActive = true;

        try
        {
            var entries = await _explorer.ListAsync(_rootPath, directoryPath, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            _currentPath = Path.GetFullPath(directoryPath);
            _searchMode = false;
            SearchBox.Text = string.Empty;
            ClearSearchButton.Visibility = Visibility.Collapsed;
            ReplaceItems(entries, searchMode: false);
            UpdateAddress();
        }
        catch (OperationCanceledException)
        {
            // Superseded navigation is intentionally silent.
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not open folder: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                BusyRing.IsActive = false;
            }
            cts.Dispose();
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_rootPath is null)
            return;

        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            if (_currentPath is not null)
                await LoadDirectoryAsync(_currentPath);
            return;
        }

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        BusyRing.IsActive = true;

        try
        {
            var entries = await _explorer.SearchAsync(_rootPath, _rootPath, query, 300, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            _searchMode = true;
            ReplaceItems(entries, searchMode: true);
            AddressText.Text = $"Search • {query}";
            ClearSearchButton.Visibility = Visibility.Visible;
            UpButton.IsEnabled = false;
            EmptyTitleText.Text = entries.Count == 0 ? "No matching files" : EmptyTitleText.Text;
            EmptyDescriptionText.Text = entries.Count == 0
                ? $"No entries containing ‘{query}’ were found in this mounted volume."
                : EmptyDescriptionText.Text;
        }
        catch (OperationCanceledException)
        {
            // Superseded search is intentionally silent.
        }
        catch (Exception ex)
        {
            ShowStatus($"Search failed: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                BusyRing.IsActive = false;
            }
            cts.Dispose();
        }
    }

    private async void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath is not null)
            await LoadDirectoryAsync(_currentPath);
    }

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_rootPath is null || _currentPath is null || PathsEqual(_rootPath, _currentPath))
            return;

        var parent = Directory.GetParent(_currentPath)?.FullName;
        if (parent is null)
            return;

        if (!IsInsideRoot(_rootPath, parent))
            parent = _rootPath;

        await LoadDirectoryAsync(parent);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null)
            return;

        if (_searchMode && SearchBox.Text.Trim().Length > 0)
            Search_Click(sender, e);
        else
            await LoadDirectoryAsync(_currentPath);
    }

    private async void ExplorerList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ExplorerEntryViewModel item)
            return;

        ExplorerList.SelectedItem = item;
        if (item.IsDirectory)
            await OpenEntryAsync(item);
    }

    private void ExplorerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateActionButtons();

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ExplorerList.SelectedItem is ExplorerEntryViewModel item)
            await OpenEntryAsync(item);
    }

    private async Task OpenEntryAsync(ExplorerEntryViewModel item)
    {
        if (item.IsReparsePoint)
        {
            ShowStatus("Dragon Explorer does not follow reparse points or junctions in this safety-first slice.", InfoBarSeverity.Warning);
            return;
        }

        if (item.IsDirectory)
        {
            await LoadDirectoryAsync(item.FullPath);
            return;
        }

        if (!File.Exists(item.FullPath))
        {
            ShowStatus("The selected file is no longer available. Refresh the mounted image.", InfoBarSeverity.Warning);
            return;
        }

        if (ExecutableExtensions.Contains(Path.GetExtension(item.FullPath)))
        {
            var dialog = new ContentDialog
            {
                Title = "Open executable content?",
                Content = new TextBlock
                {
                    Text = "This file can execute code from the mounted image. Open it only if you trust the image source.",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Open anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not open file: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
    }

    private async void CopyOut_Click(object sender, RoutedEventArgs e)
    {
        if (_copyCts is not null)
        {
            _copyCts.Cancel();
            return;
        }

        if (_rootPath is null || ExplorerList.SelectedItem is not ExplorerEntryViewModel item)
            return;

        if (item.IsReparsePoint)
        {
            ShowStatus("Copy-out does not follow reparse points or junctions.", InfoBarSeverity.Warning);
            return;
        }

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _windowHandle);
        var destination = await picker.PickSingleFolderAsync();
        if (destination is null)
            return;

        var cts = new CancellationTokenSource();
        _copyCts = cts;
        CopyOutButton.Content = "Cancel • 0%";

        var progress = new Progress<double>(value =>
        {
            var percent = Math.Clamp(value, 0d, 1d);
            CopyOutButton.Content = $"Cancel • {percent:P0}";
        });

        try
        {
            await _explorer.CopyOutAsync(_rootPath, item.FullPath, destination.Path, progress, cts.Token);
            ShowStatus($"Copied {item.Name} to {destination.Path}.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Copy-out cancelled. A partially written destination file may remain and can be safely removed.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowStatus($"Copy-out failed: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_copyCts, cts))
                _copyCts = null;
            cts.Dispose();
            CopyOutButton.Content = "Copy out";
            UpdateActionButtons();
        }
    }

    private void ReplaceItems(IEnumerable<ExplorerEntry> entries, bool searchMode)
    {
        _items.Clear();
        if (_rootPath is null)
            return;

        foreach (var entry in entries)
            _items.Add(ExplorerEntryViewModel.FromEntry(entry, _rootPath, searchMode));

        ExplorerList.SelectedItem = null;
        ItemCountText.Text = _items.Count == 1 ? "1 item" : $"{_items.Count} items";
        EmptyTitleText.Text = _items.Count == 0 ? "This location is empty" : EmptyTitleText.Text;
        EmptyDescriptionText.Text = _items.Count == 0
            ? "There are no files or folders to show here."
            : EmptyDescriptionText.Text;
        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExplorerList.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateActionButtons();
    }

    private void UpdateAddress()
    {
        if (_rootPath is null || _currentPath is null)
            return;

        var relative = Path.GetRelativePath(_rootPath, _currentPath);
        AddressText.Text = relative == "." ? _rootPath : $"{_rootPath}  ›  {relative.Replace(Path.DirectorySeparatorChar, ' › ')}";
        UpButton.IsEnabled = !PathsEqual(_rootPath, _currentPath);
    }

    private void UpdateActionButtons()
    {
        var selected = ExplorerList.SelectedItem as ExplorerEntryViewModel;
        var enabled = selected is not null && !selected.IsReparsePoint;
        OpenButton.IsEnabled = enabled;
        CopyOutButton.IsEnabled = enabled || _copyCts is not null;
    }

    private void ExplorerView_Unloaded(object sender, RoutedEventArgs e)
    {
        _loadCts?.Cancel();
        _copyCts?.Cancel();
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static bool IsInsideRoot(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedRoot, normalizedCandidate, comparison)
            || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string ShortMessage(string message)
    {
        var text = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 220 ? text : text[..217] + "...";
    }
}

public sealed class ExplorerEntryViewModel
{
    private ExplorerEntryViewModel(
        string name,
        string fullPath,
        bool isDirectory,
        bool isReparsePoint,
        string glyph,
        string secondaryText,
        string meta)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IsReparsePoint = isReparsePoint;
        Glyph = glyph;
        SecondaryText = secondaryText;
        Meta = meta;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsReparsePoint { get; }
    public string Glyph { get; }
    public string SecondaryText { get; }
    public string Meta { get; }

    public static ExplorerEntryViewModel FromEntry(ExplorerEntry entry, string rootPath, bool searchMode)
    {
        var relative = Path.GetRelativePath(rootPath, entry.FullPath);
        var location = searchMode ? Path.GetDirectoryName(relative) : null;
        var link = entry.IsReparsePoint ? " • link blocked" : string.Empty;
        var secondary = searchMode && !string.IsNullOrWhiteSpace(location)
            ? $"{(entry.IsDirectory ? "Folder" : "File")} • {location}{link}"
            : $"{(entry.IsDirectory ? "Folder" : "File")}{link}";
        var meta = entry.IsDirectory
            ? entry.LastWriteTimeUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : $"{entry.SizeDisplay} • {entry.LastWriteTimeUtc.LocalDateTime:yyyy-MM-dd HH:mm}";

        return new ExplorerEntryViewModel(
            entry.Name,
            entry.FullPath,
            entry.IsDirectory,
            entry.IsReparsePoint,
            entry.IsDirectory ? "\uE8B7" : "\uE7C3",
            secondary,
            meta);
    }
}
