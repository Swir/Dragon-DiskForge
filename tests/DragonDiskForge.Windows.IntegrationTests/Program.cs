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
var service = new WindowsDiskImageMountService();

try
{
    await ValidateImageAsync(new ImageCase("VHD", ".vhd", AssignDriveLetter: false));
    await ValidateImageAsync(new ImageCase("VHDX", ".vhdx", AssignDriveLetter: true));
    Console.WriteLine("\nDragon DiskForge Windows mount integration tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL  Windows mount integration test: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    foreach (var path in Directory.EnumerateFiles(tempRoot, "*.vhd*"))
    {
        try
        {
            var state = await service.GetStateAsync(path);
            if (state.IsMounted)
                await service.UnmountAsync(path);
        }
        catch
        {
            // Best-effort cleanup; the hosted runner is disposable.
        }
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

async Task ValidateImageAsync(ImageCase testCase)
{
    var imagePath = Path.Combine(tempRoot, $"integration{testCase.Extension}");
    Console.WriteLine($"\nINFO  Creating disposable {testCase.Name} test image with DiskPart...");
    await CreateVirtualDiskAsync(imagePath, testCase.AssignDriveLetter);

    Check(service.CanHandle(imagePath), $"Windows mount service accepts {testCase.Name}");
    Check(service.RequiresElevation(imagePath), $"{testCase.Name} reports elevation requirement for normal desktop sessions");

    var initial = await service.GetStateAsync(imagePath);
    Check(!initial.IsMounted, $"fresh test {testCase.Name} starts detached");

    var progress = new CaptureProgress();
    var mounted = await service.MountAsync(
        new MountRequest(
            imagePath,
            ReadOnly: true,
            NoDriveLetter: !testCase.AssignDriveLetter),
        progress);

    Check(mounted.IsMounted, $"native Windows service mounts {testCase.Name}");
    Check(progress.Last >= 0.999d, $"{testCase.Name} mount operation reports completion");

    var isReadOnly = await QueryReadOnlyAsync(imagePath);
    Check(isReadOnly, $"mounted {testCase.Name} is read-only");

    if (testCase.AssignDriveLetter)
    {
        Check(mounted.DriveLetters.Count > 0, $"mounted {testCase.Name} exposes a drive letter");
        var driveRoot = mounted.DriveLetters[0] + "\\";
        Check(Directory.Exists(driveRoot), $"detected {testCase.Name} drive letter is accessible");
        Console.WriteLine($"INFO  {testCase.Name} mounted at {string.Join(", ", mounted.DriveLetters)}");
    }
    else
    {
        Check(mounted.DriveLetters.Count == 0, $"{testCase.Name} honors NoDriveLetter");
    }

    progress.Reset();
    var detached = await service.UnmountAsync(imagePath, progress);
    Check(!detached.IsMounted, $"native Windows service unmounts {testCase.Name}");
    Check(progress.Last >= 0.999d, $"{testCase.Name} unmount operation reports completion");
}

static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

static async Task CreateVirtualDiskAsync(string imagePath, bool assignDriveLetter)
{
    var scriptPath = Path.Combine(
        Path.GetDirectoryName(imagePath)!,
        $"create-{Path.GetExtension(imagePath).TrimStart('.')}.txt");
    var assign = assignDriveLetter ? "assign" : string.Empty;
    var script = $"""
        create vdisk file="{imagePath}" maximum=32 type=expandable
        select vdisk file="{imagePath}"
        attach vdisk
        create partition primary
        format fs=ntfs quick label=DRAGONCI
        {assign}
        detach vdisk
        exit
        """;
    await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII);

    var result = await RunProcessAsync("diskpart.exe", $"/s \"{scriptPath}\"");
    if (result.ExitCode != 0 || result.Output.Contains("DiskPart has encountered an error", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"DiskPart could not create {Path.GetExtension(imagePath)}.\n{result.Output}\n{result.Error}");

    if (!File.Exists(imagePath))
        throw new FileNotFoundException("DiskPart completed without producing the virtual disk.", imagePath);
}

static async Task<bool> QueryReadOnlyAsync(string imagePath)
{
    var literal = $"'{imagePath.Replace("'", "''", StringComparison.Ordinal)}'";
    var script = $"$ErrorActionPreference='Stop'; (Get-DiskImage -ImagePath {literal} | Get-Disk -ErrorAction Stop).IsReadOnly";
    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    var result = await RunProcessAsync(
        "powershell.exe",
        $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}");

    if (result.ExitCode != 0)
        throw new InvalidOperationException($"Could not query virtual-disk read-only state. {result.Error}");

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

sealed record ImageCase(string Name, string Extension, bool AssignDriveLetter);
sealed record ProcessResult(int ExitCode, string Output, string Error);
