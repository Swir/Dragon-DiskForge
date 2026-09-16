using DragonDiskForge.App.Views;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using DragonDiskForge.Windows.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DragonDiskForge.App;

public sealed partial class MainWindow : Window
{
    private readonly ImageDetectionService _detector = new();
    private readonly ImageVerificationService _verification = new();
    private readonly IMountService _mountService = new WindowsDiskImageMountService();
    private readonly JsonImageLibraryService _imageLibrary;
    private readonly ScrollViewer _mainScroll;
    private readonly object? _homeContent;
    private readonly ImagesView _imagesView;
    private readonly MountedView _mountedView;
    private readonly ExplorerWorkspaceView _explorerWorkspace;
    private readonly ToolsView _toolsView;
    private long _loadGeneration;
    private DiskImageInfo? _current;
    private MountState? _mountState;
    private CancellationTokenSource? _verificationCts;
    private CancellationTokenSource? _mountCts;
    private bool _startupShown;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        _mainScroll = ShellNav.Content as ScrollViewer
            ?? throw new InvalidOperationException("Dragon shell content host is unavailable.");
        _homeContent = _mainScroll.Content;
        _imageLibrary = new JsonImageLibraryService(GetImageLibraryPath());
        _imagesView = new ImagesView(_imageLibrary);
        _mountedView = new MountedView();
        _explorerWorkspace = new ExplorerWorkspaceView(WindowNative.GetWindowHandle(this));
        _toolsView = new ToolsView(WindowNative.GetWindowHandle(this));
        _imagesView.OpenRequested += ImagesView_OpenRequested;
        _mountedView.ExploreRequested += MountedView_ExploreRequested;
        MountButton.Click += Mount_Click;
        ShellNav.SelectionChanged += ShellNav_SelectionChanged;
        Closed += (_, _) =>
        {
            _analysisCts?.Cancel();
            _verificationCts?.Cancel();
            _mountCts?.Cancel();
            _imageLibrary.Dispose();
            _mountedView.Dispose();
        };
        LockFutureNavigation();
    }

    private void LockFutureNavigation()
    {
        ShellNav.IsSettingsVisible = false;

        foreach (var item in ShellNav.MenuItems.OfType<NavigationViewItem>())
        {
            var tag = item.Tag?.ToString();
            if (string.Equals(tag, "home", StringComparison.OrdinalIgnoreCase))
            {
                ShellNav.SelectedItem = item;
                continue;
            }

            if (string.Equals(tag, "images", StringComparison.OrdinalIgnoreCase))
            {
                item.IsEnabled = true;
                ToolTipService.SetToolTip(item, "Recent images and favorites stored locally on this PC");
                continue;
            }

            if (string.Equals(tag, "mounted", StringComparison.OrdinalIgnoreCase))
            {
                item.IsEnabled = true;
                ToolTipService.SetToolTip(item, "Live ISO/VHD/VHDX state from Windows Storage plus local mount history");
                continue;
            }

            if (string.Equals(tag, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                item.IsEnabled = true;
                ToolTipService.SetToolTip(item, "Multi-image read-only workspace for currently mounted ISO/VHD/VHDX volumes");
                continue;
            }

            if (tag == "tools")
            {
                item.IsEnabled = true;
                ToolTipService.SetToolTip(item, "Checksums, RAW creation, compression and split/join");
                continue;
            }

            item.IsEnabled = false;
            var milestone = tag switch
            {
                "convert" => "0.6",
                "tools" => "0.8",
                _ => "future"
            };
            ToolTipService.SetToolTip(item, $"Planned for milestone {milestone}");
        }
    }

    private async void ShellNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag?.ToString();
        if (tag == "tools")
        {
            _mainScroll.Content = _toolsView;
            return;
        }
        if (string.Equals(tag, "images", StringComparison.OrdinalIgnoreCase))
        {
            if (!ReferenceEquals(_mainScroll.Content, _imagesView))
                _mainScroll.Content = _imagesView;
            await _imagesView.RefreshAsync();
            return;
        }

        if (string.Equals(tag, "mounted", StringComparison.OrdinalIgnoreCase))
        {
            if (!ReferenceEquals(_mainScroll.Content, _mountedView))
                _mainScroll.Content = _mountedView;
            return;
        }

        if (string.Equals(tag, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            if (!ReferenceEquals(_mainScroll.Content, _explorerWorkspace))
                _mainScroll.Content = _explorerWorkspace;

            if (!_explorerWorkspace.HasTabs)
                await OpenFirstMountedVolumeInExplorerAsync();
            return;
        }

        if (string.Equals(tag, "home", StringComparison.OrdinalIgnoreCase)
            && !ReferenceEquals(_mainScroll.Content, _homeContent))
        {
            _mainScroll.Content = _homeContent;
        }
    }

    private async void ImagesView_OpenRequested(object? sender, OpenImageFromLibraryEventArgs e)
    {
        var homeItem = ShellNav.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "home", StringComparison.OrdinalIgnoreCase));

        if (homeItem is not null)
            ShellNav.SelectedItem = homeItem;
        else if (!ReferenceEquals(_mainScroll.Content, _homeContent))
            _mainScroll.Content = _homeContent;

        await LoadImageAsync(e.ImagePath);
    }

    private async void MountedView_ExploreRequested(object? sender, ExploreMountedImageEventArgs e)
    {
        await _explorerWorkspace.OpenVolumeAsync(e.DriveRoot, e.ImagePath);
        var explorerItem = ShellNav.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "explorer", StringComparison.OrdinalIgnoreCase));

        if (explorerItem is not null)
            ShellNav.SelectedItem = explorerItem;
        else if (!ReferenceEquals(_mainScroll.Content, _explorerWorkspace))
            _mainScroll.Content = _explorerWorkspace;
    }

    private async Task OpenFirstMountedVolumeInExplorerAsync()
    {
        try
        {
            var mounted = await _mountService.GetMountedAsync();
            var state = mounted.FirstOrDefault(x => x.IsMounted && x.DriveLetters.Count > 0);
            if (state is null)
            {
                _explorerWorkspace.ShowNoMountedVolume();
                return;
            }

            await _explorerWorkspace.OpenVolumeAsync(state.DriveLetters[0] + "\\", state.ImagePath);
        }
        catch (Exception ex)
        {
            _explorerWorkspace.ShowNoMountedVolume($"Could not resolve the live mounted-volume state: {ShortMessage(ex.Message)}");
        }
    }

    private async void RootLayout_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupShown)
            return;

        _startupShown = true;
        ApplyResponsiveLayout(RootLayout.ActualWidth);
        await Task.Delay(320);
        AnimateOpacity(StartupOverlay, 1, 0, 340, () => StartupOverlay.Visibility = Visibility.Collapsed);
    }

    private void RootLayout_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < 1200;
        var narrow = width < 700;

        MainContentGrid.Padding = compact
            ? new Thickness(18, 20, 18, 28)
            : new Thickness(30, 24, 30, 34);

        ShellNav.OpenPaneLength = compact ? 238 : 268;
        HeaderStatusPill.Visibility = width < 760 ? Visibility.Collapsed : Visibility.Visible;
        HeroInputPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DragDropPill.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        SafeModeText.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;

        Grid.SetColumnSpan(HeroCopyPanel, compact ? 2 : 1);
        HeroCopyPanel.Margin = compact
            ? new Thickness(2, 4, 2, 4)
            : new Thickness(8, 4, 30, 4);

        HeroWatermark.Width = compact ? 235 : 330;
        HeroWatermark.Height = compact ? 215 : 300;
        DropZone.Padding = compact ? new Thickness(22) : new Thickness(30);
        DropZone.MinHeight = compact ? 285 : 330;

        if (compact)
        {
            SetCapabilityCardLayout(DetectCard, 0);
            SetCapabilityCardLayout(VerifyCard, 1);
            SetCapabilityCardLayout(MountCard, 2);

            Grid.SetRow(ResultActions, 1);
            Grid.SetColumn(ResultActions, 0);
            Grid.SetColumnSpan(ResultActions, 3);
            ResultActions.Margin = new Thickness(0, 4, 0, 0);
        }
        else
        {
            Grid.SetRow(DetectCard, 0);
            Grid.SetColumn(DetectCard, 0);
            Grid.SetColumnSpan(DetectCard, 1);
            Grid.SetRow(VerifyCard, 0);
            Grid.SetColumn(VerifyCard, 1);
            Grid.SetColumnSpan(VerifyCard, 1);
            Grid.SetRow(MountCard, 0);
            Grid.SetColumn(MountCard, 2);
            Grid.SetColumnSpan(MountCard, 1);

            Grid.SetRow(ResultActions, 0);
            Grid.SetColumn(ResultActions, 2);
            Grid.SetColumnSpan(ResultActions, 1);
            ResultActions.Margin = new Thickness(0);
        }

        ResultActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
    }

    private static void SetCapabilityCardLayout(FrameworkElement card, int row)
    {
        Grid.SetRow(card, row);
        Grid.SetColumn(card, 0);
        Grid.SetColumnSpan(card, 3);
    }

    private async void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in SupportedFormats.All.SelectMany(x => x.Extensions).Distinct(StringComparer.OrdinalIgnoreCase))
            picker.FileTypeFilter.Add(extension);
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            await LoadImageAsync(file.Path);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Open in Dragon DiskForge";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>().FirstOrDefault();
        if (file is not null)
            await LoadImageAsync(file.Path);
    }

    private async Task LoadImageAsync(string path)
    {
        var generation = ++_loadGeneration;
        _analysisCts?.Cancel();
        _verificationCts?.Cancel();
        _mountCts?.Cancel();

        try
        {
            var inspected = await _detector.InspectAsync(path);
            if (generation != _loadGeneration) return;
            _current = inspected;
            _mountState = null;
            ImageNameText.Text = _current.FileName;
            ImagePathText.Text = _current.Path;
            UpdateImageMetaText();

            ResultCard.Opacity = 0;
            ResultCard.Visibility = Visibility.Visible;
            AnimateOpacity(ResultCard, 0, 1, 220);

            try
            {
                await _imageLibrary.RecordOpenedAsync(_current.Path);
            }
            catch
            {
                // Library persistence must never prevent an image from opening.
            }

            await RefreshMountStateAsync(_current);
        }
        catch (Exception ex)
        {
            MountButton.IsEnabled = false;
            MountButton.Content = "Mount";
            if (generation != _loadGeneration) return;
            _current = null;
            ResultCard.Visibility = Visibility.Collapsed;
            await ShowDialogAsync("Could not open image", ex.Message);
        }
    }

    private async Task RefreshMountStateAsync(DiskImageInfo image)
    {
        var path = image.Path;
        _mountState = null;
        MountButton.IsEnabled = false;
        MountButton.Content = "Mount";

        if (!IsProvenNativeMountPath(image))
        {
            var message = _mountService.CanHandle(path)
                ? "Native backend exists, but this format is still waiting for integration validation."
                : "Native mounting is not available for this format yet.";
            ToolTipService.SetToolTip(MountButton, message);
            return;
        }

        try
        {
            var state = await _mountService.GetStateAsync(path);
            if (!IsCurrentImage(path))
                return;

            _mountState = state;
            UpdateImageMetaText();
            UpdateMountButton();
        }
        catch (Exception ex)
        {
            if (!IsCurrentImage(path))
                return;

            MountButton.IsEnabled = false;
            MountButton.Content = "Mount unavailable";
            ToolTipService.SetToolTip(MountButton, $"Could not read mount state: {ShortMessage(ex.Message)}");
        }
    }

    private async void Mount_Click(object sender, RoutedEventArgs e)
    {
        if (_mountCts is not null)
        {
            _mountCts.Cancel();
            return;
        }

        if (_current is null || !IsProvenNativeMountPath(_current))
            return;

        var imagePath = _current.Path;
        var unmount = _mountState?.IsMounted == true;
        var previousTarget = _mountState?.TargetDisplay;
        var cts = new CancellationTokenSource();
        _mountCts = cts;

        var progress = new Progress<double>(value =>
        {
            if (!IsCurrentImage(imagePath))
                return;

            var percent = Math.Clamp(value, 0d, 1d);
            MountButton.Content = $"Cancel • {percent:P0}";
        });

        MountButton.IsEnabled = true;
        MountButton.Content = "Cancel • 0%";
        ToolTipService.SetToolTip(MountButton, unmount ? "Cancel unmount request" : "Cancel mount request");

        try
        {
            var state = unmount
                ? await _mountService.UnmountAsync(imagePath, progress, cts.Token)
                : await _mountService.MountAsync(
                    new MountRequest(imagePath, ReadOnly: true, NoDriveLetter: false),
                    progress,
                    cts.Token);

            if (unmount && !state.IsMounted)
            {
                await _mountedView.RecordHistoryAsync(imagePath, MountHistoryAction.Unmounted, previousTarget);
                _explorerWorkspace.CloseImageTabs(imagePath);
            }
            else if (!unmount && state.IsMounted)
            {
                await _mountedView.RecordHistoryAsync(imagePath, MountHistoryAction.Mounted, state.TargetDisplay);
            }

            if (!IsCurrentImage(imagePath))
                return;

            _mountState = state;
            UpdateImageMetaText();
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentImage(imagePath))
                await RecoverMountStateAsync(imagePath);
        }
        catch (MountOperationException ex)
        {
            if (IsCurrentImage(imagePath))
            {
                await RecoverMountStateAsync(imagePath);
                await ShowDialogAsync("Mount operation failed", ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (IsCurrentImage(imagePath))
            {
                await RecoverMountStateAsync(imagePath);
                await ShowDialogAsync("Mount operation failed", ex.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_mountCts, cts))
                _mountCts = null;

            cts.Dispose();
            if (IsCurrentImage(imagePath))
                UpdateMountButton();
        }
    }

    private async Task RecoverMountStateAsync(string imagePath)
    {
        try
        {
            _mountState = await _mountService.GetStateAsync(imagePath);
            if (IsCurrentImage(imagePath))
                UpdateImageMetaText();
        }
        catch
        {
            _mountState = null;
        }
    }

    private void UpdateMountButton()
    {
        if (_current is null || !IsProvenNativeMountPath(_current))
        {
            MountButton.IsEnabled = false;
            MountButton.Content = "Mount";
            return;
        }

        if (_mountCts is not null)
            return;

        MountButton.IsEnabled = true;
        if (_mountState?.IsMounted == true)
        {
            var target = _mountState.DriveLetters.Count > 0
                ? string.Join(", ", _mountState.DriveLetters)
                : _mountState.TargetDisplay;
            MountButton.Content = _mountState.DriveLetters.Count > 0
                ? $"Unmount • {target}"
                : "Unmount";
            ToolTipService.SetToolTip(MountButton, $"Safely unmount {target}");
        }
        else
        {
            MountButton.Content = "Mount read-only";
            var elevation = _mountService.RequiresElevation(_current.Path)
                ? " Windows may request administrator approval."
                : string.Empty;
            ToolTipService.SetToolTip(MountButton, $"Mount this image read-only.{elevation}");
        }
    }

    private void UpdateImageMetaText()
    {
        if (_current is null)
            return;

        var mount = _mountState?.IsMounted == true
            ? $"  •  Mounted {_mountState.TargetDisplay}"
            : string.Empty;
        ImageMetaText.Text = $"{_current.Format}  •  {_current.SizeDisplay}  •  {_current.DetectionMethod}{mount}";
    }

    private static bool IsProvenNativeMountPath(DiskImageInfo image)
    {
        if (!image.CanMount)
            return false;

        var extension = Path.GetExtension(image.Path);
        return extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vhd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vhdx", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCurrentImage(string imagePath)
        => string.Equals(_current?.Path, imagePath, StringComparison.OrdinalIgnoreCase);

    private static string ShortMessage(string message)
    {
        var text = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 180 ? text : text[..177] + "...";
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (_verificationCts is not null)
        {
            _verificationCts.Cancel();
            return;
        }

        if (_current is null || sender is not Button verifyButton)
            return;

        var imagePath = _current.Path;
        var cts = new CancellationTokenSource();
        _verificationCts = cts;

        var progress = new Progress<double>(value =>
        {
            var percent = Math.Clamp(value, 0d, 1d);
            verifyButton.Content = $"Cancel • {percent:P0}";
        });

        verifyButton.Content = "Cancel • 0%";

        try
        {
            var value = await _verification.ComputeSha256Async(imagePath, progress, cts.Token);
            verifyButton.Content = "Hash calculated";
            await ShowDialogAsync("SHA-256", value);
        }
        catch (OperationCanceledException)
        {
            verifyButton.Content = "Cancelled";
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("Verification failed", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_verificationCts, cts))
                _verificationCts = null;

            cts.Dispose();
            await Task.Delay(450);
            verifyButton.Content = "Verify";
        }
    }

    private static string GetImageLibraryPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "DragonDiskForge", "image-library.json");
    }

    private static void AnimateOpacity(UIElement target, double from, double to, int durationMs, Action? completed = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        if (completed is not null)
            storyboard.Completed += (_, _) => completed();
        storyboard.Begin();
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Close",
            XamlRoot = ShellNav.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
