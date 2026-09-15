using System.Collections.ObjectModel;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DragonDiskForge.App.Views;

public sealed partial class DirectImageExplorerView : UserControl
{
    private readonly ObservableCollection<DirectExplorerEntryViewModel> _items = new();
    private readonly nint _windowHandle;
    private IDirectImageExplorer? _explorer;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _copyCts;
    private string _currentPath = "/";
    private bool _searchMode;

    public DirectImageExplorerView(nint windowHandle)
    {
        _windowHandle = windowHandle;
        InitializeComponent();
        ExplorerList.ItemsSource = _items;
        Unloaded += DirectImageExplorerView_Unloaded;
    }

    public string? ImagePath => _explorer?.ImagePath;

    public async Task OpenAsync(IDirectImageExplorer explorer)
    {
        _loadCts?.Cancel();
        _copyCts?.Cancel();
        _explorer = explorer ?? throw new ArgumentNullException(nameof(explorer));
        _currentPath = explorer.RootPath;
        _searchMode = false;
        RootDescriptionText.Text = $"{Path.GetFileName(explorer.ImagePath)} • {explorer.ProviderId} • no Windows mount";
        ProviderText.Text = $"{explorer.ProviderId.ToUpperInvariant()} • DIRECT";
        SearchBox.IsEnabled = true;
        SearchButton.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        await LoadDirectoryAsync(_currentPath);
    }

    private async Task LoadDirectoryAsync(string path)
    {
        if (_explorer is null)
            return;

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        BusyRing.IsActive = true;

        try
        {
            var entries = await _explorer.ListAsync(path, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            _currentPath = NormalizeVirtual(path);
            _searchMode = false;
            SearchBox.Text = string.Empty;
            ClearSearchButton.Visibility = Visibility.Collapsed;
            ReplaceItems(entries, searchMode: false);
            AddressText.Text = _currentPath;
            UpButton.IsEnabled = _currentPath != _explorer.RootPath;
        }
        catch (OperationCanceledException)
        {
            // Superseded navigation is intentionally silent.
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not browse image: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
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
        if (_explorer is null)
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
            var entries = await _explorer.SearchAsync(_explorer.RootPath, query, 300, cts.Token);
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

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_explorer is null || _currentPath == _explorer.RootPath)
            return;
        await LoadDirectoryAsync(GetParentVirtual(_currentPath));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_searchMode && SearchBox.Text.Trim().Length > 0)
            Search_Click(sender, e);
        else
            await LoadDirectoryAsync(_currentPath);
    }

    private async void ExplorerList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not DirectExplorerEntryViewModel item)
            return;

        ExplorerList.SelectedItem = item;
        if (item.IsDirectory)
            await LoadDirectoryAsync(item.FullPath);
    }

    private void ExplorerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ExplorerList.SelectedItem as DirectExplorerEntryViewModel;
        CopyOutButton.IsEnabled = selected is not null || _copyCts is not null;

        if (selected is null)
        {
            DetailNameText.Text = "Select an entry";
            DetailKindText.Text = _explorer is null ? "Direct provider" : $"{_explorer.ProviderId} direct provider";
            DetailPathText.Text = _currentPath;
            DetailMetaText.Text = string.Empty;
            return;
        }

        DetailNameText.Text = selected.Name;
        DetailKindText.Text = selected.IsDirectory ? "Folder • provider-backed" : "File • provider-backed";
        DetailPathText.Text = selected.FullPath;
        DetailMetaText.Text = selected.Meta;
    }

    private async void CopyOut_Click(object sender, RoutedEventArgs e)
    {
        if (_copyCts is not null)
        {
            _copyCts.Cancel();
            return;
        }

        if (_explorer is null || ExplorerList.SelectedItem is not DirectExplorerEntryViewModel item)
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
            await _explorer.CopyOutAsync(item.FullPath, destination.Path, progress, cts.Token);
            ShowStatus($"Copied {item.Name} to {destination.Path} without mounting the image.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Direct-provider Copy out cancelled. Provider rollback removed incomplete output where possible.", InfoBarSeverity.Informational);
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
            CopyOutButton.IsEnabled = ExplorerList.SelectedItem is DirectExplorerEntryViewModel;
        }
    }

    private void ReplaceItems(IEnumerable<ExplorerEntry> entries, bool searchMode)
    {
        _items.Clear();
        foreach (var entry in entries)
            _items.Add(DirectExplorerEntryViewModel.FromEntry(entry, searchMode));

        ExplorerList.SelectedItem = null;
        ItemCountText.Text = _items.Count == 1 ? "1 item" : $"{_items.Count} items";
        EmptyTitleText.Text = _items.Count == 0 ? "Nothing found" : EmptyTitleText.Text;
        EmptyDescriptionText.Text = _items.Count == 0
            ? (searchMode ? "No provider-backed entries matched this search." : "This ISO location is empty.")
            : EmptyDescriptionText.Text;
        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExplorerList.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CopyOutButton.IsEnabled = false;
        DetailNameText.Text = "Select an entry";
        DetailKindText.Text = _explorer is null ? "Direct provider" : $"{_explorer.ProviderId} direct provider";
        DetailPathText.Text = _currentPath;
        DetailMetaText.Text = string.Empty;
    }

    private void DirectImageExplorerView_Unloaded(object sender, RoutedEventArgs e)
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

    private static string NormalizeVirtual(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return "/";
        return "/" + string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string GetParentVirtual(string path)
    {
        var normalized = NormalizeVirtual(path).TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        return separator <= 0 ? "/" : normalized[..separator];
    }

    private static string ShortMessage(string message)
    {
        var text = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 220 ? text : text[..217] + "...";
    }
}

public sealed class DirectExplorerEntryViewModel
{
    private DirectExplorerEntryViewModel(
        string name,
        string fullPath,
        bool isDirectory,
        string glyph,
        string secondaryText,
        string meta)
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

    public static DirectExplorerEntryViewModel FromEntry(ExplorerEntry entry, bool searchMode)
    {
        var parent = entry.FullPath.Contains('/')
            ? entry.FullPath[..Math.Max(1, entry.FullPath.LastIndexOf('/'))]
            : "/";
        var secondary = searchMode
            ? $"{(entry.IsDirectory ? "Folder" : "File")} • {parent}"
            : (entry.IsDirectory ? "Folder" : "File");
        var meta = entry.IsDirectory
            ? entry.LastWriteTimeUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : $"{entry.SizeDisplay} • {entry.LastWriteTimeUtc.LocalDateTime:yyyy-MM-dd HH:mm}";

        return new DirectExplorerEntryViewModel(
            entry.Name,
            entry.FullPath,
            entry.IsDirectory,
            entry.IsDirectory ? "\uE8B7" : "\uE7C3",
            secondary,
            meta);
    }
}
