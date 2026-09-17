using System.Runtime.InteropServices;
using System.Text;

namespace DragonDiskForge.App;

internal static partial class StartupFailureReporter
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;
    private const string ProductName = "Dragon DiskForge";

    internal static void Report(string stage, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(exception);

        var logPath = TryWriteLog(stage, exception);
        var message = new StringBuilder()
            .AppendLine("Dragon DiskForge could not start correctly.")
            .AppendLine()
            .Append("Stage: ").AppendLine(stage)
            .Append("Error: ").AppendLine(exception.Message)
            .AppendLine()
            .AppendLine(logPath is null
                ? "A startup log could not be written."
                : $"Startup log: {logPath}")
            .AppendLine()
            .Append("Please include the startup log when reporting this problem.")
            .ToString();

        try
        {
            _ = MessageBox(IntPtr.Zero, message, $"{ProductName} - Startup error", MbOk | MbIconError);
        }
        catch
        {
            // Reporting must never mask the original startup failure.
        }
    }

    private static string? TryWriteLog(string stage, Exception exception)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                return null;

            var logDirectory = Path.Combine(localAppData, "DragonDiskForge", "Logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "startup-failure.log");

            var payload = new StringBuilder()
                .AppendLine("Dragon DiskForge startup failure")
                .Append("UTC: ").AppendLine(DateTimeOffset.UtcNow.ToString("O"))
                .Append("Stage: ").AppendLine(stage)
                .Append("Process architecture: ").AppendLine(RuntimeInformation.ProcessArchitecture.ToString())
                .Append("OS architecture: ").AppendLine(RuntimeInformation.OSArchitecture.ToString())
                .Append("OS: ").AppendLine(RuntimeInformation.OSDescription)
                .Append("Framework: ").AppendLine(RuntimeInformation.FrameworkDescription)
                .Append("Base directory: ").AppendLine(AppContext.BaseDirectory)
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();

            File.WriteAllText(logPath, payload, Encoding.UTF8);
            return logPath;
        }
        catch
        {
            return null;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
