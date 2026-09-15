using DragonDiskForge.Core.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DragonDiskForge.App;

public sealed partial class MainWindow
{
    private readonly IDirectBrowseProvider _isoDirectBrowseProvider = new Iso9660DirectBrowseProvider();
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
            "Direct provider browsing becomes available only after the image is positively recognized.");
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
        ReplaceExactText(RootLayout, "Core 0.2", "Core 0.3");
        ReplaceExactText(
            RootLayout,
            "Open, inspect, verify and mount supported disk images in a safe read-only workflow. Dragon Explorer is the next engine milestone.",
            "Open, inspect, verify, mount and browse supported disk images in a safe read-only workflow. Dragon Explorer supports mounted volumes and direct ISO browsing.");
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
        if (!Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            ToolTipService.SetToolTip(
                button,
                "Direct browsing is currently proven for ISO9660/Joliet images. Other provider families arrive in later milestones.");
            return;
        }

        ToolTipService.SetToolTip(button, "Checking ISO direct-browse capability...");
        bool supported;
        try
        {
            supported = await _isoDirectBrowseProvider.CanHandleAsync(path);
        }
        catch
        {
            supported = false;
        }

        if (_current is null || !string.Equals(_current.Path, path, StringComparison.OrdinalIgnoreCase))
            return;

        button.IsEnabled = supported;
        ToolTipService.SetToolTip(
            button,
            supported
                ? "Browse ISO9660/Joliet contents directly without mounting the image."
                : "This ISO was not recognized by the direct ISO9660/Joliet provider. Native Mount may still be available.");
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
            if (!await _isoDirectBrowseProvider.CanHandleAsync(imagePath))
            {
                await ShowDialogAsync(
                    "Direct browse unavailable",
                    "This image is not a supported ISO9660/Joliet filesystem. Dragon DiskForge will not pretend the provider can browse it.");
                return;
            }

            if (_current is null || !string.Equals(_current.Path, imagePath, StringComparison.OrdinalIgnoreCase))
                return;

            await _explorerWorkspace.OpenDirectImageAsync(imagePath, _isoDirectBrowseProvider);
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
