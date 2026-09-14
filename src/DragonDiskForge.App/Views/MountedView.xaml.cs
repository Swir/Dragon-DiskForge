using System.Collections.ObjectModel;
using System.Diagnostics;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using DragonDiskForge.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DragonDiskForge.App.Views;

public sealed partial class MountedView : UserControl, IDisposable
{
    private readonly IMountService _mountService = new WindowsDiskImageMountService();
    private readonly JsonMountHistoryService _historyService = new(GetMountHistoryPath());
    private readonly ObservableCollection<MountedImageViewModel> _items = new();
    private readonly ObservableCollection<MountedHistoryViewModel> _historyItems = new();
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _operationCts;
    private string? _activeOperationPath;

    public MountedView()
    {
        InitializeComponent();
        MountedList.ItemsSource = _items;
        HistoryList.ItemsSource = _historyItems;
        Loaded += MountedView_Loaded;
        Unloaded += MountedView_Unloaded;
    }

    public event EventHandler<ExploreMountedImageEventArgs>? ExploreRequested;

    public async Task RefreshAsync(bool showSuccess = false)
    {
        _refreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        RefreshRing.IsActive = true;

        try
        {
            var states = await _mountService.GetMountedAsync(cts.Token);
            var history = await _historyService.GetAsync(cts.Token);
            if (cts.IsCancellationRequested)
                return;

            _items.Clear();
            foreach (var state in states.Where(x => x.IsMounted))
                _items.Add(MountedImageViewModel.FromState(state));

            ApplyHistory(history);
            UpdateEmptyState();
            if (showSuccess)
                ShowStatus("Mounted-image state refreshed from Windows Storage. Local history refreshed too.", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            // Superseded refreshes are intentionally silent.
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not refresh mounted images: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts))
            {
                _refreshCts = null;
                RefreshRing.IsActive = false;
            }

            cts.Dispose();
        }
    }

    public async Task RecordHistoryAsync(
        string imagePath,
        MountHistoryAction action,
        string? targetDisplay = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var history = await _historyService.RecordAsync(imagePath, action, targetDisplay, cancellationToken);
            ApplyHistory(history);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ShowStatus($"Disk operation succeeded, but local history could not be saved: {ShortMessage(ex.Message)}", InfoBarSeverity.Warning);
        }
    }

    private async void MountedView_Loaded(object sender, RoutedEventArgs e)
        => await RefreshAsync();

    private void MountedView_Unloaded(object sender, RoutedEventArgs e)
    {
        _refreshCts?.Cancel();
        _operationCts?.Cancel();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshAsync(showSuccess: true);

    private void Explore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not MountedImageViewModel item)
            return;

        if (!item.HasDriveLetter || string.IsNullOrWhiteSpace(item.PrimaryDriveRoot))
        {
            ShowStatus("This mounted image does not currently expose a drive letter for Dragon Explorer.", InfoBarSeverity.Warning);
            return;
        }

        if (!Directory.Exists(item.PrimaryDriveRoot))
        {
            ShowStatus("The mounted drive is no longer available. Refresh the Windows state first.", InfoBarSeverity.Warning);
            return;
        }

        ExploreRequested?.Invoke(
            this,
            new ExploreMountedImageEventArgs(item.ImagePath, item.PrimaryDriveRoot));
    }

    private async void Unmount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string imagePath || string.IsNullOrWhiteSpace(imagePath))
            return;

        if (_operationCts is not null)
        {
            if (string.Equals(_activeOperationPath, imagePath, StringComparison.OrdinalIgnoreCase))
                _operationCts.Cancel();
            return;
        }

        var historyTarget = _items
            .FirstOrDefault(x => string.Equals(x.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            ?.TargetDisplay;

        var cts = new CancellationTokenSource();
        _operationCts = cts;
        _activeOperationPath = imagePath;
        button.Content = "Cancel • 0%";

        var progress = new Progress<double>(value =>
        {
            var percent = Math.Clamp(value, 0d, 1d);
            button.Content = $"Cancel • {percent:P0}";
        });

        try
        {
            var state = await _mountService.UnmountAsync(imagePath, progress, cts.Token);
            if (!state.IsMounted)
            {
                await RecordHistoryAsync(imagePath, MountHistoryAction.Unmounted, historyTarget);
                ShowStatus($"Unmounted {Path.GetFileName(imagePath)}.", InfoBarSeverity.Success);
            }

            await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Unmount request cancelled. Refreshing the real Windows state.", InfoBarSeverity.Informational);
            await RefreshAsync();
        }
        catch (MountOperationException ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowStatus($"Unmount failed: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
            await RefreshAsync();
        }
        finally
        {
            if (ReferenceEquals(_operationCts, cts))
            {
                _operationCts = null;
                _activeOperationPath = null;
            }

            cts.Dispose();
            button.Content = "Unmount";
        }
    }

    private void OpenDrive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string driveRoot || string.IsNullOrWhiteSpace(driveRoot))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{driveRoot}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not open drive: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear mounted history?",
            Content = new TextBlock
            {
                Text = "This removes only Dragon DiskForge local history metadata. It does not mount, unmount or modify any disk image.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Clear history",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            await _historyService.ClearAsync();
            ApplyHistory(Array.Empty<MountHistoryEntry>());
            ShowStatus("Mounted history cleared. Live Windows mount state was not changed.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not clear mounted history: {ShortMessage(ex.Message)}", InfoBarSeverity.Error);
        }
    }

    private void ApplyHistory(IReadOnlyList<MountHistoryEntry> history)
    {
        _historyItems.Clear();
        foreach (var entry in history)
            _historyItems.Add(MountedHistoryViewModel.FromEntry(entry));

        var count = _historyItems.Count;
        HistoryCountText.Text = count == 1 ? "1 event" : $"{count} events";
        HistoryEmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearHistoryButton.IsEnabled = count > 0;
    }

    private void UpdateEmptyState()
    {
        var count = _items.Count;
        MountedCountText.Text = count == 1 ? "1 mounted" : $"{count} mounted";
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MountedList.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static string GetMountHistoryPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "DragonDiskForge", "mount-history.json");
    }

    private static string ShortMessage(string message)
    {
        var text = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 220 ? text : text[..217] + "...";
    }

    public void Dispose() => _historyService.Dispose();
}

public sealed record ExploreMountedImageEventArgs(string ImagePath, string DriveRoot);

public sealed class MountedImageViewModel
{
    private MountedImageViewModel(
        string imagePath,
        string fileName,
        string meta,
        string targetDisplay,
        string? primaryDriveRoot,
        bool hasDriveLetter)
    {
        ImagePath = imagePath;
        FileName = fileName;
        Meta = meta;
        TargetDisplay = targetDisplay;
        PrimaryDriveRoot = primaryDriveRoot;
        HasDriveLetter = hasDriveLetter;
    }

    public string ImagePath { get; }
    public string FileName { get; }
    public string Meta { get; }
    public string TargetDisplay { get; }
    public string? PrimaryDriveRoot { get; }
    public bool HasDriveLetter { get; }

    public static MountedImageViewModel FromState(MountState state)
    {
        var extension = Path.GetExtension(state.ImagePath).TrimStart('.').ToUpperInvariant();
        var target = state.TargetDisplay;
        var primaryDriveRoot = state.DriveLetters.Count > 0 ? state.DriveLetters[0] + "\\" : null;
        var elevation = state.RequiresElevation ? " • admin on unmount if required" : string.Empty;

        return new MountedImageViewModel(
            state.ImagePath,
            Path.GetFileName(state.ImagePath),
            $"{extension} • {target}{elevation}",
            target,
            primaryDriveRoot,
            primaryDriveRoot is not null);
    }
}

public sealed class MountedHistoryViewModel
{
    private MountedHistoryViewModel(string imagePath, string title, string meta, string glyph)
    {
        ImagePath = imagePath;
        Title = title;
        Meta = meta;
        Glyph = glyph;
    }

    public string ImagePath { get; }
    public string Title { get; }
    public string Meta { get; }
    public string Glyph { get; }

    public static MountedHistoryViewModel FromEntry(MountHistoryEntry entry)
    {
        var action = entry.Action == MountHistoryAction.Mounted ? "Mounted" : "Unmounted";
        var glyph = entry.Action == MountHistoryAction.Mounted ? "\uE72E" : "\uE74D";
        var localTime = entry.TimestampUtc.LocalDateTime;
        var target = string.IsNullOrWhiteSpace(entry.TargetDisplay) ? string.Empty : $" • {entry.TargetDisplay}";

        return new MountedHistoryViewModel(
            entry.ImagePath,
            $"{action} • {Path.GetFileName(entry.ImagePath)}",
            $"{localTime:yyyy-MM-dd HH:mm:ss}{target}",
            glyph);
    }
}
