using DragonDiskForge.Core.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DragonDiskForge.App;

public sealed partial class MainWindow
{
    private readonly ProviderRegistry _providerRegistry = new([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(new CcdImageProvider(), Priority: 95),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
        new ProviderRegistration(new CueSheetImageProvider(), Priority: 70),
        new ProviderRegistration(new MdsImageProvider(), Priority: 60),
        new ProviderRegistration(new NrgImageProvider(), Priority: 50),
        new ProviderRegistration(new VmdkSparseImageProvider(), Priority: 40),
        new ProviderRegistration(new QcowImageProvider(), Priority: 30)
    ]);
    private Button? _directBrowseButton;
    private long _directBrowsePathCallbackToken;

    internal void EnableDirectBrowseUi()
    {
        RefreshMilestoneUiText();

        _directBrowseButton = ResultActions.Children
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Explore", StringComparison.OrdinalIgnoreCase));

        if (_directBrowseButton is null)
            return;

        _directBrowseButton.Content = "Explore directly";
        _directBrowseButton.IsEnabled = false;
        ToolTipService.SetToolTip(
            _directBrowseButton,
            "Direct provider browsing becomes available only after a registered provider positively recognizes the image.");
        _directBrowseButton.Click += DirectBrowse_Click;

        _directBrowsePathCallbackToken = ImagePathText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => _ = RefreshDirectBrowseCapabilityAsync());

        Closed += (_, _) =>
        {
            if (_directBrowsePathCallbackToken != 0)
                ImagePathText.UnregisterPropertyChangedCallback(TextBlock.TextProperty, _directBrowsePathCallbackToken);
        };

        _ = RefreshDirectBrowseCapabilityAsync();
    }

    private void RefreshMilestoneUiText()
    {
        ReplaceExactText(RootLayout, "Core 0.2", "Core 0.4");
        ReplaceExactText(RootLayout, "Core 0.3", "Core 0.4");
        ReplaceExactText(
            RootLayout,
            "Open, inspect, verify and mount supported disk images in a safe read-only workflow. Dragon Explorer is the next engine milestone.",
            "Open, inspect, verify, mount and browse supported disk images in a safe read-only workflow. Dragon Explorer supports mounted volumes and provider-backed direct browsing.");
        ReplaceExactText(
            RootLayout,
            "Open, inspect, verify, mount and browse supported disk images in a safe read-only workflow. Dragon Explorer supports mounted volumes and direct ISO browsing.",
            "Open, inspect, verify, mount and browse supported disk images in a safe read-only workflow. Dragon Explorer supports mounted volumes and provider-backed direct browsing.");
    }

    private static void ReplaceExactText(DependencyObject root, string oldText, string newText)
    {
        if (root is TextBlock textBlock && string.Equals(textBlock.Text, oldText, StringComparison.Ordinal))
            textBlock.Text = newText;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
            ReplaceExactText(VisualTreeHelper.GetChild(root, index), oldText, newText);
    }

    private async Task RefreshDirectBrowseCapabilityAsync()
    {
        var button = _directBrowseButton;
        var image = _current;
        if (button is null)
            return;

        button.Content = "Explore directly";
        button.IsEnabled = false;

        if (image is null)
        {
            ToolTipService.SetToolTip(button, "Open an image first.");
            return;
        }

        var path = image.Path;
        ToolTipService.SetToolTip(button, "Checking registered direct-browse providers...");

        ProviderResolution resolution;
        try
        {
            resolution = await _providerRegistry.ResolveAsync(path);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            ToolTipService.SetToolTip(button, "Provider capability detection failed safely.");
            return;
        }

        if (_current is null || !string.Equals(_current.Path, path, StringComparison.OrdinalIgnoreCase))
            return;

        var direct = resolution.Provider as IDirectBrowseProvider;
        var supported = direct is not null
            && resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == true;

        button.IsEnabled = supported;
        ToolTipService.SetToolTip(
            button,
            supported
                ? $"Browse with {resolution.Descriptor!.DisplayName} directly without mounting the image."
                : BuildProviderUnavailableMessage(resolution));
    }

    private static string BuildProviderUnavailableMessage(ProviderResolution resolution)
    {
        if (resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.PartitionTable) == true)
        {
            return $"{resolution.Descriptor.DisplayName} recognized this image and can safely inspect its partition table. Direct filesystem browsing stays disabled until filesystem support is implemented and tested.";
        }

        if (resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.MediaGeometry) == true)
        {
            return $"{resolution.Descriptor.DisplayName} recognized this image and can safely inspect its media geometry. Direct filesystem browsing stays disabled until filesystem support is implemented and tested.";
        }

        if (resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.TrackLayout) == true)
        {
            return $"{resolution.Descriptor.DisplayName} recognized this image and can safely inspect its optical track layout. Direct filesystem browsing stays disabled until filesystem support is implemented and tested.";
        }

        if (resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.VirtualDiskMetadata) == true)
        {
            return $"{resolution.Descriptor.DisplayName} recognized this image and can safely inspect its virtual-disk metadata. Direct filesystem browsing stays disabled until extent translation and filesystem support are implemented and tested.";
        }

        var failed = resolution.Diagnostics.Count(x => !string.IsNullOrWhiteSpace(x.ErrorMessage));
        return failed > 0
            ? $"No direct-browse provider accepted this image. {failed} provider probe(s) failed safely; native Mount may still be available."
            : "No registered direct-browse provider currently accepts this image. Native Mount may still be available for supported formats.";
    }

    private async void DirectBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (_directBrowseButton is null || _current is null)
            return;

        var imagePath = _current.Path;
        _directBrowseButton.IsEnabled = false;
        _directBrowseButton.Content = "Opening direct...";

        try
        {
            var resolution = await _providerRegistry.ResolveAsync(imagePath);
            if (resolution.Provider is not IDirectBrowseProvider directProvider
                || resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) != true)
            {
                await ShowDialogAsync(
                    "Direct browse unavailable",
                    "No registered direct-browse provider positively recognized this image. Dragon DiskForge will not pretend the format can be browsed.");
                return;
            }

            if (_current is null || !string.Equals(_current.Path, imagePath, StringComparison.OrdinalIgnoreCase))
                return;

            await _explorerWorkspace.OpenDirectImageAsync(imagePath, directProvider);
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
            await ShowDialogAsync("Could not open direct browser", ShortMessage(ex.Message));
        }
        finally
        {
            if (_current is not null && string.Equals(_current.Path, imagePath, StringComparison.OrdinalIgnoreCase))
                await RefreshDirectBrowseCapabilityAsync();
        }
    }
}
