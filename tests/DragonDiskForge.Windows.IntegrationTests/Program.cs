using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Windows.Services;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("SKIP  Windows mount integration tests require Windows.");
    return;
}

if (!IsAdministrator())
{
    Console.Error.WriteLine("FAIL  Windows mount integration tests require an elevated runner.");
    Environment.ExitCode = 1;
    return;
}

var tempRoot = Path.Combine(Path.GetTempPath(), $"dragon-diskforge-mount-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);
var vhdPath = Path.Combine(tempRoot, "integration.vhd");
var service = new WindowsDiskImageMountService();
var progress = new CaptureProgress();

try
{
    Console.WriteLine("INFO  Creating disposable VHD test image with DiskPart...");
    await CreateVhdAsync(vhdPath);

    Check(service.CanHandle(vhdPath), "Windows mount service accepts VHD");
    Check(service.RequiresElevation(vhdPath), "VHD reports elevation requirement for normal desktop sessions");

    var initial = await service.GetStateAsync(vhdPath);
    Check(!initial.IsMounted, "fresh test VHD starts detached");

    var mounted = await service.MountAsync(
        new MountRequest(vhdPath, ReadOnly: true, NoDriveLetter: true),
        progress);
    Check(mounted.IsMounted, "native Windows service mounts VHD");
    Check(progress.Last >= 0.999d, "mount operation reports completion");

    var isReadOnly = await QueryReadOnlyAsync(vhdPath);
    Check(isReadOnly, "mounted VHD is read-only");

    progress.Reset();
    var detached = await service.UnmountAsync(vhdPath, progress);
    Check(!detached.IsMounted, "native Windows service unmounts VHD");
    Check(progress.Last >= 0.999d, "unmount operation reports completion");

    Console.WriteLine("\nDragon DiskForge Windows mount integration tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL  Windows mount integration test: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    try
    {
        if (File.Exists(vhdPath))
        {
            var state = await service.GetStateAsync(vhdPath);
            if (state.IsMounted)
                await service.UnmountAsync(vhdPath);
        }
    }
    catch
    {
        // Best-effort cleanup; the hosted runner is disposable.
    }

    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
        // Best-effort cleanup; the hosted runner is disposable.
    }
}

static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

static async Task CreateVhdAsync(string vhdPath)
{
    var scriptPath = Path.Combine(Path.GetDirectoryName(vhdPath)!, "create-vhd.txt");
    var script = $"""
        create vdisk file="{vhdPath}" maximum=32 type=expandable
        select vdisk file="{vhdPath}"
        attach vdisk
        create partition primary
        format fs=ntfs quick label=DRAGONCI
        detach vdisk
        exit
        """;
    await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII);

    var result = await RunProcessAsync("diskpart.exe", $"/s \"{scriptPath}\"");
    if (result.ExitCode != 0 || result.Output.Contains("DiskPart has encountered an error", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"DiskPart could not create the integration VHD.\n{result.Output}\n{result.Error}");

    if (!File.Exists(vhdPath))
        throw new FileNotFoundException("DiskPart completed without producing the VHD.", vhdPath);
}

static async Task<bool> QueryReadOnlyAsync(string vhdPath)
{
    var literal = $"'{vhdPath.Replace("'", "''", StringComparison.Ordinal)}'";
    var script = $"$ErrorActionPreference='Stop'; (Get-DiskImage -ImagePath {literal} | Get-Disk -ErrorAction Stop).IsReadOnly";
    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    var result = await RunProcessAsync(
        "powershell.exe",
        $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}");

    if (result.ExitCode != 0)
        throw new InvalidOperationException($"Could not query VHD read-only state. {result.Error}");

    return result.Output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
}

static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };

    process.Start();
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
}

static void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);

    Console.WriteLine($"PASS  {name}");
}

sealed class CaptureProgress : IProgress<double>
{
    public double Last { get; private set; }
    public void Report(double value) => Last = value;
    public void Reset() => Last = 0d;
}

sealed record ProcessResult(int ExitCode, string Output, string Error);
