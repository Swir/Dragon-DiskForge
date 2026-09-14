using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DragonDiskForge.App.Views;

public sealed partial class ExplorerWorkspaceView : UserControl
{
    private readonly nint _windowHandle;
    private readonly Dictionary<string, TabViewItem> _tabs = new(StringComparer.OrdinalIgnoreCase);

    public ExplorerWorkspaceView(nint windowHandle)
    {
        _windowHandle = windowHandle;
        InitializeComponent();
        UpdateEmptyState();
    }

    public bool HasTabs => _tabs.Count > 0;

    public async Task OpenVolumeAsync(string rootPath, string imagePath)
    {
        var root = Path.GetFullPath(rootPath);
        var image = Path.GetFullPath(imagePath);
        if (!Directory.Exists(root))
        {
            ShowNoMountedVolume("The mounted drive is no longer available. Refresh Mounted and try again.");
            return;
        }

        var key = BuildKey(image, root);
        if (_tabs.TryGetValue(key, out var existing))
        {
            if (existing.Content is ExplorerView explorer)
                await explorer.OpenVolumeAsync(root, image);
            WorkspaceTabs.SelectedItem = existing;
            return;
        }

        var view = new ExplorerView(_windowHandle);
        await view.OpenVolumeAsync(root, image);

        var tab = new TabViewItem
        {
            Header = Path.GetFileName(image),
            Content = view,
            Tag = key,
            IsClosable = true
        };
        ToolTipService.SetToolTip(tab, $"{image}\n{root}\nClosing this tab does not unmount the image.");

        _tabs.Add(key, tab);
        WorkspaceTabs.TabItems.Add(tab);
        WorkspaceTabs.SelectedItem = tab;
        UpdateEmptyState();
    }

    public void CloseImageTabs(string imagePath)
    {
        var normalized = Path.GetFullPath(imagePath) + "|";
        var keys = _tabs.Keys
            .Where(key => key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var key in keys)
        {
            if (!_tabs.Remove(key, out var tab))
                continue;
            WorkspaceTabs.TabItems.Remove(tab);
        }

        UpdateEmptyState();
    }

    public void ShowNoMountedVolume(string? message = null)
    {
        if (HasTabs)
            return;

        EmptyDescriptionText.Text = message
            ?? "Open a mounted ISO, VHD or VHDX from the Mounted dashboard. Each image opens in its own tab.";
        UpdateEmptyState();
    }

    private void WorkspaceTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is not TabViewItem tab)
            return;

        if (tab.Tag is string key)
            _tabs.Remove(key);

        sender.TabItems.Remove(tab);
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var count = _tabs.Count;
        TabCountText.Text = count == 1 ? "1 tab" : $"{count} tabs";
        WorkspaceTabs.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BuildKey(string imagePath, string rootPath)
        => $"{Path.GetFullPath(imagePath)}|{Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath))}";
}
