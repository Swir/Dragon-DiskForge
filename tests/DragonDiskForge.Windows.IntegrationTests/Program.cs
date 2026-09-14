using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
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
var explorer = new MountedFileSystemExplorerService();

try
{
    await ValidateVirtualDiskAsync(new ImageCase("VHD", ".vhd", AssignDriveLetter: false));
    await ValidateVirtualDiskAsync(new ImageCase("VHDX", ".vhdx", AssignDriveLetter: true));
    await ValidateIsoAsync();
    await ValidateUnsupportedFormatErrorAsync();
    Console.WriteLine("\nDragon DiskForge Windows mount integration tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL  Windows mount integration test: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    foreach (var path in Directory.EnumerateFiles(tempRoot).Where(service.CanHandle))
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

async Task ValidateVirtualDiskAsync(ImageCase testCase)
{
    var imagePath = Path.Combine(tempRoot, $"integration{testCase.Extension}");
    Console.WriteLine($"\nINFO  Creating disposable {testCase.Name} test image with DiskPart...");
    await CreateVirtualDiskAsync(imagePath, testCase.AssignDriveLetter);

    Check(service.CanHandle(imagePath), $"Windows mount service accepts {testCase.Name}");
    Check(service.RequiresElevation(imagePath), $"{testCase.Name} reports elevation requirement for normal desktop sessions");

    var initial = await service.GetStateAsync(imagePath);
    Check(!initial.IsMounted, $"fresh test {testCase.Name} starts detached");
    await ValidatePreCancelledMountAsync(imagePath, testCase.Name);

    var progress = new CaptureProgress();
    var mounted = await service.MountAsync(
        new MountRequest(
            imagePath,
            ReadOnly: true,
            NoDriveLetter: !testCase.AssignDriveLetter),
        progress);

    Check(mounted.IsMounted, $"native Windows service mounts {testCase.Name}");
    Check(progress.Last >= 0.999d, $"{testCase.Name} mount operation reports completion");
    await ValidateMountedInventoryContainsAsync(imagePath, testCase.Name);

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
    await ValidateMountedInventoryExcludesAsync(imagePath, testCase.Name);
}

async Task ValidateIsoAsync()
{
    var sourcePath = Path.Combine(tempRoot, "iso-source");
    Directory.CreateDirectory(sourcePath);
    var markerName = "dragon-ci.txt";
    var markerContent = "Dragon DiskForge native ISO integration test.";
    await File.WriteAllTextAsync(Path.Combine(sourcePath, markerName), markerContent);

    var imagePath = Path.Combine(tempRoot, "integration.iso");
    Console.WriteLine("\nINFO  Creating disposable ISO test image with Windows IMAPI2FS...");
    await CreateIsoAsync(sourcePath, imagePath);

    Check(File.Exists(imagePath) && new FileInfo(imagePath).Length > 0, "IMAPI produced a non-empty ISO image");
    Check(service.CanHandle(imagePath), "Windows mount service accepts ISO");
    Check(!service.RequiresElevation(imagePath), "ISO does not request elevation by policy");

    var initial = await service.GetStateAsync(imagePath);
    Check(!initial.IsMounted, "fresh test ISO starts detached");
    await ValidatePreCancelledMountAsync(imagePath, "ISO");

    var progress = new CaptureProgress();
    var mounted = await service.MountAsync(
        new MountRequest(imagePath, ReadOnly: true, NoDriveLetter: false),
        progress);

    Check(mounted.IsMounted, "native Windows service mounts ISO");
    Check(progress.Last >= 0.999d, "ISO mount operation reports completion");
    Check(mounted.DriveLetters.Count > 0, "mounted ISO exposes a drive letter");
    await ValidateMountedInventoryContainsAsync(imagePath, "ISO");

    var driveRoot = mounted.DriveLetters[0] + "\\";
    Check(Directory.Exists(driveRoot), "detected ISO drive letter is accessible");
    Check(File.Exists(Path.Combine(driveRoot, markerName)), "mounted ISO exposes its expected file");
    Console.WriteLine($"INFO  ISO mounted at {string.Join(", ", mounted.DriveLetters)}");

    var entries = await explorer.ListAsync(driveRoot, driveRoot);
    var markerEntry = entries.FirstOrDefault(x => !x.IsDirectory && x.Name.Equals(markerName, StringComparison.OrdinalIgnoreCase));
    Check(markerEntry is not null, "Dragon Explorer lists a file from the real mounted ISO");
    Check(markerEntry?.SizeBytes == Encoding.UTF8.GetByteCount(markerContent), "Dragon Explorer reports mounted ISO file metadata");

    var searchResults = await explorer.SearchAsync(driveRoot, driveRoot, "dragon-ci");
    Check(searchResults.Any(x => x.Name.Equals(markerName, StringComparison.OrdinalIgnoreCase)), "Dragon Explorer search finds a file on the real mounted ISO");

    var exportRoot = Path.Combine(tempRoot, "explorer-export");
    Directory.CreateDirectory(exportRoot);
    var copyProgress = new CaptureProgress();
    await explorer.CopyOutAsync(driveRoot, Path.Combine(driveRoot, markerName), exportRoot, copyProgress);
    var copiedMarker = Path.Combine(exportRoot, markerName);
    Check(File.Exists(copiedMarker), "Dragon Explorer copies a real mounted ISO file out of the image");
    Check(await File.ReadAllTextAsync(copiedMarker) == markerContent, "Dragon Explorer copy-out preserves mounted ISO file content");
    Check(copyProgress.Last >= 0.999d, "Dragon Explorer mounted-volume copy-out reports completion");

    progress.Reset();
    var detached = await service.UnmountAsync(imagePath, progress);
    Check(!detached.IsMounted, "native Windows service unmounts ISO");
    Check(progress.Last >= 0.999d, "ISO unmount operation reports completion");
    await ValidateMountedInventoryExcludesAsync(imagePath, "ISO");
}

async Task ValidatePreCancelledMountAsync(string imagePath, string name)
{
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();

    try
    {
        await service.MountAsync(
            new MountRequest(imagePath, ReadOnly: true),
            cancellationToken: cancelled.Token);
        throw new InvalidOperationException($"pre-cancelled {name} mount unexpectedly completed");
    }
    catch (OperationCanceledException)
    {
        Check(true, $"pre-cancelled {name} mount is rejected before changing storage state");
    }

    var state = await service.GetStateAsync(imagePath);
    Check(!state.IsMounted, $"pre-cancelled {name} mount leaves image detached");
}

async Task ValidateMountedInventoryContainsAsync(string imagePath, string name)
{
    var mounted = await service.GetMountedAsync();
    var entry = mounted.FirstOrDefault(x =>
        string.Equals(x.ImagePath, Path.GetFullPath(imagePath), StringComparison.OrdinalIgnoreCase));
    Check(entry is not null && entry.IsMounted, $"mounted inventory contains {name}");
}

async Task ValidateMountedInventoryExcludesAsync(string imagePath, string name)
{
    var mounted = await service.GetMountedAsync();
    Check(!mounted.Any(x => string.Equals(x.ImagePath, Path.GetFullPath(imagePath), StringComparison.OrdinalIgnoreCase)),
        $"mounted inventory removes {name} after unmount");
}

async Task ValidateUnsupportedFormatErrorAsync()
{
    var unsupportedPath = Path.Combine(tempRoot, "unsupported.img");
    await File.WriteAllBytesAsync(unsupportedPath, new byte[32]);

    try
    {
        await service.MountAsync(new MountRequest(unsupportedPath));
        throw new InvalidOperationException("unsupported mount unexpectedly completed");
    }
    catch (MountOperationException ex)
    {
        Check(ex.Message.Contains("ISO, VHD and VHDX", StringComparison.OrdinalIgnoreCase),
            "unsupported-format mount returns a friendly capability error");
    }
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

static async Task CreateIsoAsync(string sourcePath, string imagePath)
{
    var scriptPath = Path.Combine(Path.GetDirectoryName(imagePath)!, "create-iso.ps1");
    var sourceLiteral = ToPowerShellLiteral(sourcePath);
    var imageLiteral = ToPowerShellLiteral(imagePath);
    var script = $$"""
        $ErrorActionPreference = 'Stop'
        $source = {{sourceLiteral}}
        $target = {{imageLiteral}}

        $code = @'
        using System;
        using System.IO;
        using System.Runtime.InteropServices.ComTypes;

        public static class DragonIsoWriter
        {
            public unsafe static void Create(string path, object stream, int blockSize, int totalBlocks)
            {
                int bytes = 0;
                byte[] buffer = new byte[blockSize];
                IntPtr pointer = (IntPtr)(&bytes);
                IStream input = stream as IStream;
                if (input == null)
                    throw new InvalidOperationException("IMAPI did not return an IStream.");

                FileStream output = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
                try
                {
                    while (totalBlocks-- > 0)
                    {
                        input.Read(buffer, blockSize, pointer);
                        output.Write(buffer, 0, bytes);
                    }
                    output.Flush();
                }
                finally
                {
                    output.Dispose();
                }
            }
        }
        '@

        $compiler = New-Object System.CodeDom.Compiler.CompilerParameters
        $compiler.CompilerOptions = '/unsafe'
        Add-Type -CompilerParameters $compiler -TypeDefinition $code

        $image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
        $image.FileSystemsToCreate = 3
        $image.FreeMediaBlocks = 0
        $image.VolumeName = 'DRAGONCI'
        $image.Root.AddTree($source, $false)
        $result = $image.CreateResultImage()
        [DragonIsoWriter]::Create($target, $result.ImageStream, $result.BlockSize, $result.TotalBlocks)
        """;

    await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8);
    var result = await RunProcessAsync(
        "powershell.exe",
        $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"");

    if (result.ExitCode != 0)
        throw new InvalidOperationException($"Windows IMAPI could not create the ISO.\n{result.Output}\n{result.Error}");
}

static string ToPowerShellLiteral(string value)
    => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

static async Task<bool> QueryReadOnlyAsync(string imagePath)
{
    var literal = ToPowerShellLiteral(imagePath);
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
