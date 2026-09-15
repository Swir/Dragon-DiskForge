using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DragonDiskForge.App.Views;

public sealed partial class ExplorerWorkspaceView : UserControl
{
    private const string DirectKeyPrefix = "direct|";

    private readonly nint _windowHandle;
    private readonly Dictionary<string, TabViewItem> _tabs = new(StringComparer.OrdinalIgnoreCase);

    public ExplorerWorkspaceView(nint windowHandle)
    {
        _windowHandle = windowHandle;
        InitializeComponent();
        Loaded += ExplorerWorkspaceView_Loaded;
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

        var key = BuildMountedKey(image, root);
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
        ToolTipService.SetToolTip(tab, $"Mounted volume\n{image}\n{root}\nClosing this tab does not unmount the image.");

        _tabs.Add(key, tab);
        WorkspaceTabs.TabItems.Add(tab);
        WorkspaceTabs.SelectedItem = tab;
        UpdateEmptyState();
    }

    public async Task OpenDirectAsync(IDirectImageExplorer explorer)
    {
        ArgumentNullException.ThrowIfNull(explorer);
        var image = Path.GetFullPath(explorer.ImagePath);
        if (!File.Exists(image))
        {
            ShowNoMountedVolume("The direct-browse image is no longer available. Reopen it from the Forge or Images library.");
            return;
        }

        var key = BuildDirectKey(image);
        if (_tabs.TryGetValue(key, out var existing))
        {
            if (existing.Content is DirectImageExplorerView directView)
                await directView.OpenAsync(explorer);
            WorkspaceTabs.SelectedItem = existing;
            return;
        }

        var view = new DirectImageExplorerView(_windowHandle);
        await view.OpenAsync(explorer);

        var tab = new TabViewItem
        {
            Header = $"{Path.GetFileName(image)} • direct",
            Content = view,
            Tag = key,
            IsClosable = true
        };
        ToolTipService.SetToolTip(tab, $"Direct provider: {explorer.ProviderId}\n{image}\nNo Windows mount is used.");

        _tabs.Add(key, tab);
        WorkspaceTabs.TabItems.Add(tab);
        WorkspaceTabs.SelectedItem = tab;
        UpdateEmptyState();
    }

    public void CloseImageTabs(string imagePath)
    {
        var normalized = Path.GetFullPath(imagePath) + "|";
        var keys = _tabs.Keys
            .Where(key => !IsDirectKey(key)
                && key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var key in keys)
            RemoveTab(key);

        UpdateEmptyState();
    }

    public void ShowNoMountedVolume(string? message = null)
    {
        PruneUnavailableTabs();
        if (HasTabs)
            return;

        EmptyDescriptionText.Text = message
            ?? "Open a mounted ISO/VHD/VHDX from Mounted, or open a supported image directly from the Forge without mounting.";
        UpdateEmptyState();
    }

    private void ExplorerWorkspaceView_Loaded(object sender, RoutedEventArgs e)
        => PruneUnavailableTabs();

    private void WorkspaceTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab is not TabViewItem tab)
            return;

        if (tab.Tag is string key)
            RemoveTab(key);
        else
            sender.TabItems.Remove(tab);

        UpdateEmptyState();
    }

    private void PruneUnavailableTabs()
    {
        var staleKeys = _tabs.Keys
            .Where(IsStaleKey)
            .ToArray();

        foreach (var key in staleKeys)
            RemoveTab(key);

        if (staleKeys.Length > 0)
        {
            EmptyDescriptionText.Text = "One or more Explorer tabs were closed because their backing Windows volume or image file is no longer available.";
            UpdateEmptyState();
        }
    }

    private static bool IsStaleKey(string key)
    {
        if (TryGetDirectImageFromKey(key, out var image))
            return !File.Exists(image);

        return !TryGetRootFromMountedKey(key, out var root) || !Directory.Exists(root);
    }

    private void RemoveTab(string key)
    {
        if (!_tabs.Remove(key, out var tab))
            return;
        WorkspaceTabs.TabItems.Remove(tab);
    }

    private void UpdateEmptyState()
    {
        var count = _tabs.Count;
        TabCountText.Text = count == 1 ? "1 tab" : $"{count} tabs";
        WorkspaceTabs.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BuildMountedKey(string imagePath, string rootPath)
        => $"{Path.GetFullPath(imagePath)}|{Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath))}";

    private static string BuildDirectKey(string imagePath)
        => DirectKeyPrefix + Path.GetFullPath(imagePath);

    private static bool IsDirectKey(string key)
        => key.StartsWith(DirectKeyPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetDirectImageFromKey(string key, out string imagePath)
    {
        if (!IsDirectKey(key) || key.Length <= DirectKeyPrefix.Length)
        {
            imagePath = string.Empty;
            return false;
        }

        imagePath = key[DirectKeyPrefix.Length..];
        return true;
    }

    private static bool TryGetRootFromMountedKey(string key, out string root)
    {
        if (IsDirectKey(key))
        {
            root = string.Empty;
            return false;
        }

        var separator = key.LastIndexOf('|');
        if (separator < 0 || separator == key.Length - 1)
        {
            root = string.Empty;
            return false;
        }

        root = key[(separator + 1)..];
        return true;
    }
}
