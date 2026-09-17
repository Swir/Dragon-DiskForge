using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;

namespace DragonDiskForge.App;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startupImagePath = ImageLaunchArgumentResolver.Resolve(
            Environment.GetCommandLineArgs().Skip(1));

        var mainWindow = new MainWindow();
        mainWindow.EnableDirectBrowseUi();
        mainWindow.ConfigureBrandingFooter();
        mainWindow.EnableSessionPortability(startupImagePath);
        _window = mainWindow;
        _window.Activate();
    }
}
