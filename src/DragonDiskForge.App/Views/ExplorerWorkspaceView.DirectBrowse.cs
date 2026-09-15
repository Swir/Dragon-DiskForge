using DragonDiskForge.Core.Providers;
using Microsoft.UI.Xaml.Controls;

namespace DragonDiskForge.App.Views;

public sealed partial class ExplorerWorkspaceView
{
    public async Task OpenDirectImageAsync(string imagePath, IDirectBrowseProvider provider)
    {
        var image = Path.GetFullPath(imagePath);
        if (!File.Exists(image))
        {
            ShowNoMountedVolume("The image is no longer available for direct browsing.");
            return;
        }

        var sourceDirectory = Path.GetDirectoryName(image)
            ?? throw new InvalidOperationException("Could not resolve the image directory.");
        var key = $"direct:{provider.Id}:{image}|{Path.TrimEndingDirectorySeparator(sourceDirectory)}";

        if (_tabs.TryGetValue(key, out var existing))
        {
            if (existing.Content is DirectBrowseView directView)
                await directView.OpenImageAsync(image);
            WorkspaceTabs.SelectedItem = existing;
            return;
        }

        var view = new DirectBrowseView(provider, _windowHandle);
        await view.OpenImageAsync(image);

        var tab = new TabViewItem
        {
            Header = $"{Path.GetFileName(image)} • direct",
            Content = view,
            Tag = key,
            IsClosable = true
        };
        ToolTipService.SetToolTip(
            tab,
            $"{image}\n{provider.DisplayName}\nProvider-backed read-only browsing • image is not mounted.");

        _tabs.Add(key, tab);
        WorkspaceTabs.TabItems.Add(tab);
        WorkspaceTabs.SelectedItem = tab;
        UpdateEmptyState();
    }
}
