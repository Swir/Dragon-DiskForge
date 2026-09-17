using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using DragonDiskForge.Windows.Services;

namespace DragonDiskForge.SecurityBoundary.SmokeTests;

internal static class Program
{
    private static readonly List<string> Failures = [];

    private static int Main()
    {
        try
        {
            var repositoryRoot = FindRepositoryRoot();
            VerifyElevationBoundary(repositoryRoot);
            VerifyNativeMountMutationBoundary(repositoryRoot);
            VerifyShellBoundary(repositoryRoot);
            VerifyCliBoundary(repositoryRoot);
            VerifyPackageBoundary(repositoryRoot);
            VerifyDisposableWriterCiLock(repositoryRoot);
            VerifyPhysicalMediaConfirmationBoundary();
            VerifyShellCommandQuotingBoundary();
        }
        catch (Exception ex)
        {
            Failures.Add($"Security-boundary test harness failed unexpectedly: {ex}");
        }

        if (Failures.Count == 0)
        {
            Console.WriteLine("Dragon DiskForge security-boundary smoke tests passed.");
            return 0;
        }

        Console.Error.WriteLine("Dragon DiskForge security-boundary smoke tests failed:");
        foreach (var failure in Failures)
            Console.Error.WriteLine($"- {failure}");
        return 1;
    }

    private static void VerifyElevationBoundary(string repositoryRoot)
    {
        var manifest = Read(repositoryRoot, "src", "DragonDiskForge.App", "app.manifest");
        Require(
            manifest.Contains("requestedExecutionLevel level=\"asInvoker\" uiAccess=\"false\"", StringComparison.Ordinal),
            "Desktop application manifest must explicitly remain asInvoker with uiAccess disabled.");
        Require(
            !manifest.Contains("requireAdministrator", StringComparison.OrdinalIgnoreCase)
            && !manifest.Contains("highestAvailable", StringComparison.OrdinalIgnoreCase),
            "Desktop application manifest must not request ambient elevation.");
    }

    private static void VerifyNativeMountMutationBoundary(string repositoryRoot)
    {
        var source = Read(repositoryRoot, "src", "DragonDiskForge.Windows", "Services", "WindowsDiskImageMountService.cs");

        Require(
            source.Contains("cancellationToken.ThrowIfCancellationRequested();", StringComparison.Ordinal)
            && source.Contains("await process.WaitForExitAsync(CancellationToken.None);", StringComparison.Ordinal),
            "Native mount/dismount must honor cancellation before launch but finish a started Windows storage mutation instead of killing it mid-commit.");
        Require(
            source.Contains("ReadToEndAsync(CancellationToken.None)", StringComparison.Ordinal),
            "Native storage mutation stderr capture must not reintroduce caller cancellation after the Windows command starts.");
        Require(
            source.Contains("WaitForCommittedStateAsync(path, true)", StringComparison.Ordinal)
            && source.Contains("WaitForCommittedStateAsync(path, false)", StringComparison.Ordinal),
            "Mount and unmount must reconcile the real Windows state after the native mutation commits.");
        Require(
            source.Contains("new CancellationTokenSource(TimeSpan.FromSeconds(15))", StringComparison.Ordinal)
            && source.Contains("Refresh Mounted before retrying", StringComparison.Ordinal),
            "Post-commit state reconciliation must remain bounded and surface an explicit refresh-safe failure if confirmation times out.");
    }

    private static void VerifyShellBoundary(string repositoryRoot)
    {
        var source = Read(repositoryRoot, "src", "DragonDiskForge.Windows", "Services", "WindowsShellIntegrationService.cs");
        Require(source.Contains("Registry.CurrentUser", StringComparison.Ordinal),
            "Shell integration must remain rooted in the current-user registry hive.");
        Require(source.Contains(@"Software\Classes", StringComparison.Ordinal),
            "Shell integration must remain scoped to the per-user Software\\Classes boundary.");
        Require(!source.Contains("Registry.LocalMachine", StringComparison.Ordinal),
            "Shell integration must not write HKLM.");
        Require(!source.Contains("UserChoice", StringComparison.OrdinalIgnoreCase),
            "Shell integration must not modify Windows UserChoice default-app state.");
    }

    private static void VerifyCliBoundary(string repositoryRoot)
    {
        var runner = Read(repositoryRoot, "src", "DragonDiskForge.Cli", "CliRunner.cs");
        var program = Read(repositoryRoot, "src", "DragonDiskForge.Cli", "Program.cs");
        var combined = runner + "\n" + program;

        Require(!combined.Contains("PhysicalMediaWriteExecutionService", StringComparison.Ordinal),
            "Public CLI must not expose the physical-media write execution service.");
        Require(!combined.Contains("WindowsPhysicalMediaWriteSink", StringComparison.Ordinal),
            "Public CLI must not expose the Windows physical-media write sink.");
        Require(!combined.Contains("DDF_DISPOSABLE_WRITE_OPT_IN", StringComparison.Ordinal),
            "Public CLI must not inherit the disposable-media destructive test opt-in contract.");
    }

    private static void VerifyPackageBoundary(string repositoryRoot)
    {
        var packageScript = Read(repositoryRoot, "scripts", "package-windows.ps1");
        var verifyScript = Read(repositoryRoot, "scripts", "verify-package.ps1");

        Require(packageScript.Contains("$_.Extension -ine \".pdb\"", StringComparison.Ordinal),
            "Public package staging must continue excluding PDB files.");
        Require(packageScript.Contains("SmokeTests|IntegrationTests", StringComparison.Ordinal),
            "Public package staging must continue rejecting test-only payloads.");
        Require(packageScript.Contains("entryPointSha256", StringComparison.Ordinal)
                && packageScript.Contains("cliEntryPointSha256", StringComparison.Ordinal)
                && packageScript.Contains("shellIntegrationEntryPointSha256", StringComparison.Ordinal),
            "Package manifest must continue binding application, CLI and shell-helper entry points by SHA-256.");
        Require(verifyScript.Contains("SHA256", StringComparison.OrdinalIgnoreCase),
            "Independent package verification must retain SHA-256 validation.");
    }

    private static void VerifyDisposableWriterCiLock(string repositoryRoot)
    {
        var guard = Read(repositoryRoot, ".github", "workflows", "disposable-physical-media-guard.yml");
        Require(guard.Contains("if ($env:DDF_DISPOSABLE_WRITE_OPT_IN)", StringComparison.Ordinal),
            "CI disposable-media guard must fail if destructive opt-in is present.");
        Require(guard.Contains("without destructive opt-in", StringComparison.OrdinalIgnoreCase),
            "CI disposable-media harness must document and execute its non-destructive mode.");
    }

    private static void VerifyPhysicalMediaConfirmationBoundary()
    {
        const long sourceLength = 8L * 1024 * 1024;
        var destination = new PhysicalDiskInfo(
            DiskNumber: 7,
            DevicePath: @"\\.\PhysicalDrive7",
            CapacityBytes: 16L * 1024 * 1024,
            BusType: "USB",
            IsRemovable: true,
            IsSystemDisk: false,
            Vendor: "Dragon",
            Product: "Disposable",
            Revision: "1",
            SerialNumber: "SERIAL-7",
            StableId: "USB:DRAGON:SERIAL-7",
            HasStableIdentity: true,
            Evidence: Array.Empty<string>());

        var safety = new PhysicalMediaSafetyService();
        var valid = safety.PreviewImageToDiskWrite(@"C:\images\candidate.img", sourceLength, destination);
        Require(valid.IsAllowed && valid.RequiresExplicitConfirmation && !string.IsNullOrWhiteSpace(valid.ConfirmationToken),
            "Eligible disposable media must still require a destination-bound explicit confirmation token.");
        Require(safety.ConfirmationMatches(valid, valid.ConfirmationToken),
            "Exact destination-bound confirmation token must be accepted.");
        Require(!safety.ConfirmationMatches(valid, valid.ConfirmationToken!.ToLowerInvariant()),
            "Confirmation token must remain case-sensitive and exact.");

        var systemDisk = safety.PreviewImageToDiskWrite(
            @"C:\images\candidate.img",
            sourceLength,
            destination with { IsSystemDisk = true });
        Require(!systemDisk.IsAllowed && systemDisk.ConfirmationToken is null,
            "System-disk destinations must fail closed and receive no confirmation token.");

        var unstable = safety.PreviewImageToDiskWrite(
            @"C:\images\candidate.img",
            sourceLength,
            destination with { StableId = string.Empty, HasStableIdentity = false });
        Require(!unstable.IsAllowed && unstable.ConfirmationToken is null,
            "Destinations without stable hardware identity must fail closed.");

        var unknownCapacity = safety.PreviewImageToDiskWrite(
            @"C:\images\candidate.img",
            sourceLength,
            destination with { CapacityBytes = null });
        Require(!unknownCapacity.IsAllowed,
            "Destinations with unknown capacity must fail closed.");

        var tooSmall = safety.PreviewImageToDiskWrite(
            @"C:\images\candidate.img",
            sourceLength,
            destination with { CapacityBytes = sourceLength - 512 });
        Require(!tooSmall.IsAllowed,
            "Destinations smaller than the source image must fail closed.");

        var physicalSource = safety.PreviewImageToDiskWrite(
            @"\\.\PhysicalDrive3",
            sourceLength,
            destination);
        Require(!physicalSource.IsAllowed,
            "Physical-device source paths must remain refused by the image-to-disk write contract.");
    }

    private static void VerifyShellCommandQuotingBoundary()
    {
        var appPath = @"C:\Program Files\Dragon DiskForge\DragonDiskForge.App.exe";
        var command = WindowsShellIntegrationService.BuildOpenCommand(appPath);
        Require(command.StartsWith("\"", StringComparison.Ordinal)
                && command.EndsWith("\" \"%1\"", StringComparison.Ordinal),
            "Shell open command must quote both the executable path and the selected image placeholder.");

        RequireThrows<ArgumentException>(
            () => WindowsShellIntegrationService.BuildOpenCommand(@"C:\Tools\other.exe"),
            "Shell integration must reject executables other than DragonDiskForge.App.exe.");
        RequireThrows<ArgumentException>(
            () => WindowsShellIntegrationService.BuildOpenCommand("C:\\DragonDiskForge.App.exe\\\" --inject"),
            "Shell integration must reject quote injection in the registered application path.");
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "DragonDiskForge.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate DragonDiskForge.sln from the test process.");
    }

    private static string Read(string repositoryRoot, params string[] parts)
    {
        var path = parts.Aggregate(repositoryRoot, Path.Combine);
        if (!File.Exists(path))
            throw new FileNotFoundException("Required security-review source file was not found.", path);
        return File.ReadAllText(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            Failures.Add(message);
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
            Failures.Add(message);
        }
        catch (TException)
        {
        }
        catch (Exception ex)
        {
            Failures.Add($"{message} Expected {typeof(TException).Name}, got {ex.GetType().Name}.");
        }
    }
}
