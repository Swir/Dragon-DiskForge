using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;

namespace DragonDiskForge.App;

public partial class App : Application
{
    private readonly CrashReportService _crashReports = new(GetCrashReportDirectory());
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

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

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (e.Exception is not null)
            _crashReports.TryRecord(e.Exception);
    }

    private static string GetCrashReportDirectory()
    {
        var statePath = MainWindow.GetApplicationStatePath();
        var directory = Path.GetDirectoryName(statePath)
            ?? throw new InvalidOperationException("Application-state path has no parent directory.");
        return Path.Combine(directory, "crashes");
    }
}
