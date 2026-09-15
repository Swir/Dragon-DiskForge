using System.Collections.ObjectModel;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DragonDiskForge.App.Views;

public sealed partial class DirectBrowseView : UserControl
{
    private readonly IDirectBrowseProvider _provider;
    private readonly nint _windowHandle;
    private readonly ObservableCollection<DirectBrowseEntryViewModel> _items = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _copyCts;
    private string? _imagePath;
    private string _currentPath = "/";
    private bool _searchMode;

    public DirectBrowseView(IDirectBrowseProvider provider, nint windowHandle)
    {
        _provider = provider;
        _windowHandle = windowHandle;
        InitializeComponent();
        EntryList.ItemsSource = _items;
        Unloaded += DirectBrowseView_Unloaded;
        UpdateActions();
    }

    public async Task OpenImageAsync(string imagePath)
    {
        _loadCts?.Cancel();
        _copyCts?.Cancel();

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
        {
            ShowUnavailable("The source image is no longer available.");
            return;
        }

        if (!await _provider.CanHandleAsync(fullPath))
        {
            ShowUnavailable("The selected image is not supported by this direct-browse provider.");
            return;
        }

        _imagePath = fullPath;
        _currentPath = "/";
        _searchMode = false;
        SourceDescriptionText.Text = $"{Path.GetFileName(fullPath)} • {_provider.DisplayName} • read-only provider";
        RefreshButton.IsEnabled = true;
        SearchButton.IsEnabled = true;
        SearchBox.IsEnabled = true;
        await LoadDirectoryAsync("/");
    }

    private async Task LoadDirectoryAsync(string virtualPath)
    {
        if (_imagePath is null)
            return;

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        BusyRing.IsActive = true;

        try
        {
            var entries = await _provider.ListAsync(_imagePath, virtualPath, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            _currentPath = NormalizeVirtualPath(virtualPath);
            _searchMode = false;
            SearchBox.Text = string.Empty;
            ClearSearchButton.Visibility = Visibility.Collapsed;
            ReplaceItems(entries, searchMode: false);
            AddressText.Text = _currentPath;
            UpButton.IsEnabled = _currentPath != "/";
        }
        catch (OperationCanceledException)
        {
            // Superseded navigation is intentionally silent.
        }
        catch (Exception ex)
        {
            ShowStatus($"Direct browse failed: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
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

    private async void EntryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DirectBrowseEntryViewModel item)
            return;

        EntryList.SelectedItem = item;
        if (item.IsDirectory)
            await LoadDirectoryAsync(item.FullPath);
    }

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateActions();

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == "/")
            return;

        var trimmed = _currentPath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var parent = slash <= 0 ? "/" : trimmed[..slash];
        await LoadDirectoryAsync(parent);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_imagePath is null)
            return;

        if (!File.Exists(_imagePath))
        {
            ShowUnavailable("The source image was removed or moved. Close this tab or reopen the image from its new path.");
            return;
        }

        if (_searchMode && SearchBox.Text.Trim().Length > 0)
            Search_Click(sender, e);
        else
            await LoadDirectoryAsync(_currentPath);
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_imagePath is null)
            return;

        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            await LoadDirectoryAsync(_currentPath);
            return;
        }

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        BusyRing.IsActive = true;

        try
        {
            var entries = await _provider.SearchAsync(_imagePath, "/", query, 300, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            _searchMode = true;
            ReplaceItems(entries, searchMode: true);
            AddressText.Text = $"Search • {query}";
            ClearSearchButton.Visibility = Visibility.Visible;
            UpButton.IsEnabled = false;
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
        => await LoadDirectoryAsync(_currentPath);

    private async void CopyOut_Click(object sender, RoutedEventArgs e)
    {
        if (_copyCts is not null)
        {
            _copyCts.Cancel();
            return;
        }

        if (_imagePath is null || EntryList.SelectedItem is not DirectBrowseEntryViewModel item)
            return;

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, _windowHandle);
        var destination = await picker.PickSingleFolderAsync();
        if (destination is null)
            return;

        var cts = new CancellationTokenSource();
        _copyCts = cts;
        CopyOutButton.Content = "Cancel • 0%";
        CopyOutButton.IsEnabled = true;

        var progress = new Progress<double>(value =>
        {
            var percent = Math.Clamp(value, 0d, 1d);
            CopyOutButton.Content = $"Cancel • {percent:P0}";
        });

        try
        {
            await _provider.CopyOutAsync(_imagePath, item.FullPath, destination.Path, progress, cts.Token);
            ShowStatus($"Copied {item.Name} to {destination.Path} without mounting the image.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Direct Copy out cancelled. Partially written output may remain and can be removed safely.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowStatus($"Copy out failed: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_copyCts, cts))
                _copyCts = null;
            cts.Dispose();
            CopyOutButton.Content = "Copy out";
            UpdateActions();
        }
    }

    private void ReplaceItems(IEnumerable<ExplorerEntry> entries, bool searchMode)
    {
        _items.Clear();
        foreach (var entry in entries)
            _items.Add(DirectBrowseEntryViewModel.FromEntry(entry, searchMode));

        EntryList.SelectedItem = null;
        ItemCountText.Text = _items.Count == 1 ? "1 item" : $"{_items.Count} items";
        EmptyTitleText.Text = _items.Count == 0
            ? (searchMode ? "No matching files" : "This location is empty")
            : EmptyTitleText.Text;
        EmptyDescriptionText.Text = _items.Count == 0
            ? (searchMode ? "No matching entries were found in this image." : "There are no files or folders to show here.")
            : EmptyDescriptionText.Text;
        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EntryList.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateActions();
    }

    private void ShowUnavailable(string message)
    {
        _imagePath = null;
        _items.Clear();
        EntryList.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        EmptyTitleText.Text = "Direct browse unavailable";
        EmptyDescriptionText.Text = message;
        AddressText.Text = "/";
        SearchBox.IsEnabled = false;
        SearchButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        UpButton.IsEnabled = false;
        UpdateActions();
    }

    private void UpdateActions()
    {
        CopyOutButton.IsEnabled = _copyCts is not null || EntryList.SelectedItem is DirectBrowseEntryViewModel;
    }

    private void DirectBrowseView_Unloaded(object sender, RoutedEventArgs e)
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

    private static string NormalizeVirtualPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return "/";
        return "/" + string.Join('/', path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ShortMessage(string message)
    {
        var text = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 220 ? text : text[..217] + "...";
    }
}

public sealed class DirectBrowseEntryViewModel
{
    private DirectBrowseEntryViewModel(string name, string fullPath, bool isDirectory, string glyph, string secondaryText, string meta)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Glyph = glyph;
        SecondaryText = secondaryText;
        Meta = meta;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public string Glyph { get; }
    public string SecondaryText { get; }
    public string Meta { get; }

    public static DirectBrowseEntryViewModel FromEntry(ExplorerEntry entry, bool searchMode)
    {
        var secondary = searchMode
            ? $"{(entry.IsDirectory ? "Folder" : "File")} • {entry.FullPath}"
            : entry.IsDirectory ? "Folder" : "File";
        var meta = entry.IsDirectory
            ? entry.LastWriteTimeUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : $"{entry.SizeDisplay} • {entry.LastWriteTimeUtc.LocalDateTime:yyyy-MM-dd HH:mm}";

        return new DirectBrowseEntryViewModel(
            entry.Name,
            entry.FullPath,
            entry.IsDirectory,
            entry.IsDirectory ? "\uE8B7" : "\uE7C3",
            secondary,
            meta);
    }
}
