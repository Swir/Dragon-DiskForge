using Microsoft.UI.Xaml;

namespace DragonDiskForge.App;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        mainWindow.EnableDirectBrowseUi();
        _window = mainWindow;
        _window.Activate();
    }
}
