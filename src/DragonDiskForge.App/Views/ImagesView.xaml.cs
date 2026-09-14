using System.Collections.ObjectModel;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DragonDiskForge.App.Views;

public sealed partial class ImagesView : UserControl
{
    private readonly IImageLibraryService _library;
    private readonly ObservableCollection<ImageLibraryItemViewModel> _favorites = new();
    private readonly ObservableCollection<ImageLibraryItemViewModel> _recents = new();
    private CancellationTokenSource? _refreshCts;

    public ImagesView(IImageLibraryService library)
    {
        _library = library;
        InitializeComponent();
        FavoritesList.ItemsSource = _favorites;
        RecentsList.ItemsSource = _recents;
        Loaded += ImagesView_Loaded;
        Unloaded += ImagesView_Unloaded;
    }

    public event EventHandler<OpenImageFromLibraryEventArgs>? OpenRequested;

    public async Task RefreshAsync()
    {
        _refreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        BusyRing.IsActive = true;

        try
        {
            var snapshot = await _library.GetAsync(cts.Token);
            if (!cts.IsCancellationRequested)
                ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException)
        {
            // Superseded refreshes are intentionally silent.
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not load image library: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts))
            {
                _refreshCts = null;
                BusyRing.IsActive = false;
            }
            cts.Dispose();
        }
    }

    private async void ImagesView_Loaded(object sender, RoutedEventArgs e)
        => await RefreshAsync();

    private void ImagesView_Unloaded(object sender, RoutedEventArgs e)
        => _refreshCts?.Cancel();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshAsync();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string path || string.IsNullOrWhiteSpace(path))
            return;

        if (!File.Exists(path))
        {
            ShowStatus("This image is no longer available at the saved path. You can remove the stale entry from the library.", InfoBarSeverity.Warning);
            return;
        }

        OpenRequested?.Invoke(this, new OpenImageFromLibraryEventArgs(path));
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string path || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var current = _favorites.Concat(_recents)
                .FirstOrDefault(x => PathsEqual(x.Path, path));
            var snapshot = await _library.SetFavoriteAsync(path, !(current?.IsFavorite ?? false));
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not update favorite: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string path || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var snapshot = await _library.RemoveAsync(path);
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not remove library entry: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
    }

    private void ApplySnapshot(ImageLibrarySnapshot snapshot)
    {
        _favorites.Clear();
        foreach (var entry in snapshot.Favorites)
            _favorites.Add(ImageLibraryItemViewModel.FromEntry(entry));

        _recents.Clear();
        foreach (var entry in snapshot.Recents)
            _recents.Add(ImageLibraryItemViewModel.FromEntry(entry));

        FavoriteCountText.Text = _favorites.Count == 1 ? "1 favorite" : $"{_favorites.Count} favorites";
        RecentCountText.Text = _recents.Count == 1 ? "1 recent" : $"{_recents.Count} recent";
        FavoritesEmpty.Visibility = _favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesList.Visibility = _favorites.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentsEmpty.Visibility = _recents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentsList.Visibility = _recents.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            System.IO.Path.GetFullPath(left),
            System.IO.Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string ShortMessage(string message)
    {
        var text = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 220 ? text : text[..217] + "...";
    }
}

public sealed record OpenImageFromLibraryEventArgs(string ImagePath);

public sealed class ImageLibraryItemViewModel
{
    private ImageLibraryItemViewModel(
        string path,
        string fileName,
        string meta,
        bool isFavorite,
        bool canOpen)
    {
        Path = path;
        FileName = fileName;
        Meta = meta;
        IsFavorite = isFavorite;
        CanOpen = canOpen;
        FavoriteActionText = isFavorite ? "Unfavorite" : "Favorite";
    }

    public string Path { get; }
    public string FileName { get; }
    public string Meta { get; }
    public bool IsFavorite { get; }
    public bool CanOpen { get; }
    public string FavoriteActionText { get; }

    public static ImageLibraryItemViewModel FromEntry(ImageLibraryEntry entry)
    {
        var exists = File.Exists(entry.Path);
        var state = exists ? "Available" : "Missing";
        var local = entry.LastOpenedUtc.LocalDateTime;
        return new ImageLibraryItemViewModel(
            entry.Path,
            System.IO.Path.GetFileName(entry.Path),
            $"{state} • opened {local:yyyy-MM-dd HH:mm}",
            entry.IsFavorite,
            exists);
    }
}
