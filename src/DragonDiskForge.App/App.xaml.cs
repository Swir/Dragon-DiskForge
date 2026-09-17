using DragonDiskForge.Core.Services;
using Microsoft.UI.Xaml;

namespace DragonDiskForge.App;

public partial class App : Application
{
    private CrashReportService? _crashReports;
    private Window? _window;

    public App()
    {
        // Register the handler before XAML initialization so early managed startup
        // failures are not lost behind a silent WinExe process exit.
        UnhandledException += OnUnhandledException;

        try
        {
            InitializeComponent();
            _crashReports = new CrashReportService(GetCrashReportDirectory());
        }
        catch (Exception exception)
        {
            StartupFailureReporter.Report("App.InitializeComponent", exception);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
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
        catch (Exception exception)
        {
            StartupFailureReporter.Report("App.OnLaunched", exception);
            throw;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (e.Exception is null)
            return;

        _crashReports?.TryRecord(e.Exception);
        StartupFailureReporter.Report("Microsoft.UI.Xaml.UnhandledException", e.Exception);
    }

    private static string GetCrashReportDirectory()
    {
        var statePath = MainWindow.GetApplicationStatePath();
        var directory = Path.GetDirectoryName(statePath)
            ?? throw new InvalidOperationException("Application-state path has no parent directory.");
        return Path.Combine(directory, "crashes");
    }
}
