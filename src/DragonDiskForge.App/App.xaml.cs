using Microsoft.UI.Xaml;

namespace DragonDiskForge.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragonDiskForge", "logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "crash.log"), $"{DateTimeOffset.UtcNow:O} {e.Exception}\\n");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        mainWindow.EnableDirectBrowseUi();
        var launch = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(launch) && !launch.StartsWith("--"))
            mainWindow.OpenInitialPath(launch);
        _window = mainWindow;
        _window.Activate();
    }
}
