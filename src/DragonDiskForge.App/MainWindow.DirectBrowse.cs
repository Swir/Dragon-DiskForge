using DragonDiskForge.Core.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DragonDiskForge.App;

public sealed partial class MainWindow
{
    private readonly DirectBrowseProviderRegistry _directBrowseProviders = DirectBrowseProviderRegistry.CreateDefault();
    private CancellationTokenSource? _directBrowseProbeCts;
    private IDirectBrowseProvider? _currentDirectBrowseProvider;
    private Button? _directExploreButton;

    public void InitializeDirectBrowse()
    {
        _directExploreButton = ResultActions.Children.OfType<Button>().FirstOrDefault();
        if (_directExploreButton is null)
            return;

        _directExploreButton.Click += DirectExplore_Click;
        _directExploreButton.Content = "Explore";
        _directExploreButton.IsEnabled = false;
        ToolTipService.SetToolTip(_directExploreButton, "Direct browsing becomes available only when a tested provider accepts this image.");

        ImagePathText.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
        {
            _ = RefreshDirectBrowseCapabilityAsync(ImagePathText.Text);
        });

        if (!string.IsNullOrWhiteSpace(ImagePathText.Text) && File.Exists(ImagePathText.Text))
            _ = RefreshDirectBrowseCapabilityAsync(ImagePathText.Text);
    }

    private async Task RefreshDirectBrowseCapabilityAsync(string path)
    {
        _directBrowseProbeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _directBrowseProbeCts = cts;
        _currentDirectBrowseProvider = null;

        if (_directExploreButton is not null)
        {
            _directExploreButton.IsEnabled = false;
            _directExploreButton.Content = "Explore";
            ToolTipService.SetToolTip(_directExploreButton, "Checking direct-browse providers...");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ApplyDirectBrowseUnavailable(path);
                return;
            }

            var provider = await _directBrowseProviders.ResolveAsync(path, cts.Token);
            if (cts.IsCancellationRequested || !PathsEqualForDirectBrowse(ImagePathText.Text, path))
                return;

            _currentDirectBrowseProvider = provider;
            if (_directExploreButton is null)
                return;

            if (provider is null)
            {
                ApplyDirectBrowseUnavailable(path);
                return;
            }

            _directExploreButton.IsEnabled = true;
            _directExploreButton.Content = "Explore direct";
            ToolTipService.SetToolTip(
                _directExploreButton,
                $"Browse with the tested {provider.Id} provider without mounting the image");
        }
        catch (OperationCanceledException)
        {
            // A newer image superseded this provider probe.
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested || !PathsEqualForDirectBrowse(ImagePathText.Text, path))
                return;

            _currentDirectBrowseProvider = null;
            if (_directExploreButton is not null)
            {
                _directExploreButton.IsEnabled = false;
                _directExploreButton.Content = "Explore";
                ToolTipService.SetToolTip(_directExploreButton, $"Direct provider check failed: {ShortMessage(ex.Message)}");
            }
        }
        finally
        {
            if (ReferenceEquals(_directBrowseProbeCts, cts))
                _directBrowseProbeCts = null;
            cts.Dispose();
        }
    }

    private void ApplyDirectBrowseUnavailable(string path)
    {
        if (_directExploreButton is null)
            return;

        _directExploreButton.IsEnabled = false;
        _directExploreButton.Content = "Explore";
        ToolTipService.SetToolTip(
            _directExploreButton,
            string.IsNullOrWhiteSpace(path)
                ? "Open an image first."
                : "No tested direct-browse provider is available for this image yet. Mounted Explorer remains separate.");
    }

    private async void DirectExplore_Click(object sender, RoutedEventArgs e)
    {
        if (_directExploreButton is null || _currentDirectBrowseProvider is null || _current is null)
            return;

        var imagePath = _current.Path;
        var provider = _currentDirectBrowseProvider;
        _directExploreButton.IsEnabled = false;
        _directExploreButton.Content = "Opening direct...";

        try
        {
            var session = await provider.OpenAsync(imagePath);
            if (!IsCurrentImage(imagePath))
                return;

            await _explorerWorkspace.OpenDirectAsync(session);
            var explorerItem = ShellNav.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "explorer", StringComparison.OrdinalIgnoreCase));

            if (explorerItem is not null)
                ShellNav.SelectedItem = explorerItem;
            else if (!ReferenceEquals(_mainScroll.Content, _explorerWorkspace))
                _mainScroll.Content = _explorerWorkspace;
        }
        catch (Exception ex)
        {
            if (IsCurrentImage(imagePath))
                await ShowDialogAsync("Direct browse failed", ShortMessage(ex.Message));
        }
        finally
        {
            if (IsCurrentImage(imagePath) && _directExploreButton is not null)
            {
                _directExploreButton.IsEnabled = _currentDirectBrowseProvider is not null;
                _directExploreButton.Content = _currentDirectBrowseProvider is null ? "Explore" : "Explore direct";
            }
        }
    }

    private static bool PathsEqualForDirectBrowse(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
