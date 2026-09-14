using System.Security.Cryptography;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS  {name}");
        return;
    }

    failures.Add(name);
    Console.Error.WriteLine($"FAIL  {name}");
}

async Task<string> WithTempFileAsync(string extension, byte[] content, Func<string, Task<string>> action)
{
    var path = Path.Combine(Path.GetTempPath(), $"dragon-diskforge-{Guid.NewGuid():N}{extension}");
    await File.WriteAllBytesAsync(path, content);
    try
    {
        return await action(path);
    }
    finally
    {
        File.Delete(path);
    }
}

async Task WithTempDirectoryAsync(Func<string, Task> action)
{
    var path = Path.Combine(Path.GetTempPath(), $"dragon-diskforge-dir-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    try
    {
        await action(path);
    }
    finally
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort smoke-test cleanup.
        }
    }
}

var detector = new ImageDetectionService();
var verifier = new ImageVerificationService();
var explorer = new MountedFileSystemExplorerService();

Check(SupportedFormats.FromPath("sample.ISO")?.Name == "ISO", "extension lookup is case-insensitive");
Check(SupportedFormats.FromPath("disk.qcow2")?.Name == "QCOW/QCOW2", "QCOW2 extension is catalogued");
Check(SupportedFormats.FromPath("unknown.xyz") is null, "unknown extension stays unknown");
Check(SupportedFormats.FromPath("disk.vhdx")?.NativeWindowsMount == true, "VHDX is marked for native Windows mount");

var mountRequest = new MountRequest("sample.iso");
Check(mountRequest.ReadOnly, "mount requests default to read-only");
Check(!mountRequest.NoDriveLetter, "mount requests assign a drive letter by default");
var detachedState = MountState.Detached("sample.iso", requiresElevation: false);
Check(!detachedState.IsMounted && detachedState.DriveLetters.Count == 0, "detached mount state has no drive letters");

var vhdx = new byte[64];
Encoding.ASCII.GetBytes("vhdxfile").CopyTo(vhdx, 0);
var vhdxFormat = await WithTempFileAsync(".bin", vhdx, async path => (await detector.InspectAsync(path)).Format);
Check(vhdxFormat == "VHDX", "VHDX signature overrides extension");

var qcow = new byte[64];
qcow[0] = 0x51;
qcow[1] = 0x46;
qcow[2] = 0x49;
qcow[3] = 0xFB;
var qcowFormat = await WithTempFileAsync(".img", qcow, async path => (await detector.InspectAsync(path)).Format);
Check(qcowFormat == "QCOW/QCOW2", "QCOW signature overrides extension");

var iso = new byte[0x9000];
Encoding.ASCII.GetBytes("CD001").CopyTo(iso, 0x8001);
var isoInfo = await WithTempFileAsync(".bin", iso, async path =>
{
    var info = await detector.InspectAsync(path);
    Check(info.DetectionMethod == "Signature", "ISO reports signature detection");
    Check(info.CanMount, "ISO is marked native-mount capable");
    return info.Format;
});
Check(isoInfo == "ISO", "ISO-9660 signature is detected");

var extensionFallback = await WithTempFileAsync(".vmdk", new byte[32], async path =>
{
    var info = await detector.InspectAsync(path);
    Check(info.DetectionMethod == "Extension", "known extension uses extension fallback");
    return info.Format;
});
Check(extensionFallback == "VMDK", "VMDK extension fallback is detected");

var verifyPayload = Encoding.UTF8.GetBytes("Dragon DiskForge verification smoke test");
var progress = new CaptureProgress();
var computedSha256 = await WithTempFileAsync(
    ".img",
    verifyPayload,
    path => verifier.ComputeSha256Async(path, progress));
var expectedSha256 = Convert.ToHexString(SHA256.HashData(verifyPayload));
Check(computedSha256 == expectedSha256, "Core SHA-256 verification returns the expected digest");
Check(progress.Last >= 0.999d, "Core verification reports completion progress");

try
{
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await WithTempFileAsync(
        ".img",
        verifyPayload,
        path => verifier.ComputeSha256Async(path, null, cancelled.Token));
    Check(false, "Core verification honors cancellation");
}
catch (OperationCanceledException)
{
    Check(true, "Core verification honors cancellation");
}

try
{
    await detector.InspectAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.iso"));
    Check(false, "missing image throws FileNotFoundException");
}
catch (FileNotFoundException)
{
    Check(true, "missing image throws FileNotFoundException");
}

await WithTempDirectoryAsync(async root =>
{
    var folder = Path.Combine(root, "FolderA");
    var nested = Path.Combine(folder, "Nested");
    Directory.CreateDirectory(nested);
    await File.WriteAllTextAsync(Path.Combine(root, "root.txt"), "root");
    await File.WriteAllTextAsync(Path.Combine(folder, "dragon-note.txt"), "dragon");
    await File.WriteAllTextAsync(Path.Combine(nested, "payload.bin"), "payload-data");

    var listed = await explorer.ListAsync(root, root);
    Check(listed.Count == 2, "Explorer lists the current directory without recursive blocking");
    Check(listed[0].IsDirectory && listed[0].Name == "FolderA", "Explorer sorts folders before files");
    Check(listed.Any(x => !x.IsDirectory && x.Name == "root.txt" && x.SizeBytes == 4), "Explorer reports file metadata");

    var search = await explorer.SearchAsync(root, root, "dragon");
    Check(search.Count == 1 && search[0].Name == "dragon-note.txt", "Explorer recursive search finds nested matches");

    try
    {
        var escaped = Path.GetFullPath(Path.Combine(root, ".."));
        await explorer.ListAsync(root, escaped);
        Check(false, "Explorer rejects paths that escape the mounted root");
    }
    catch (InvalidOperationException)
    {
        Check(true, "Explorer rejects paths that escape the mounted root");
    }

    try
    {
        await explorer.CopyOutAsync(root, Path.Combine(root, "root.txt"), Path.Combine(root, "inside"));
        Check(false, "Explorer copy-out rejects destinations inside the mounted root");
    }
    catch (InvalidOperationException)
    {
        Check(true, "Explorer copy-out rejects destinations inside the mounted root");
    }

    var exportRoot = Path.Combine(Path.GetTempPath(), $"dragon-diskforge-export-{Guid.NewGuid():N}");
    Directory.CreateDirectory(exportRoot);
    try
    {
        var copyProgress = new CaptureProgress();
        await explorer.CopyOutAsync(root, folder, exportRoot, copyProgress);
        var copiedNested = Path.Combine(exportRoot, "FolderA", "Nested", "payload.bin");
        Check(File.Exists(copiedNested), "Explorer copy-out preserves nested directory structure");
        Check(await File.ReadAllTextAsync(copiedNested) == "payload-data", "Explorer copy-out preserves file content");
        Check(copyProgress.Last >= 0.999d, "Explorer copy-out reports completion progress");
    }
    finally
    {
        Directory.Delete(exportRoot, recursive: true);
    }

    try
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await explorer.SearchAsync(root, root, "payload", cancellationToken: cancelled.Token);
        Check(false, "Explorer search honors cancellation");
    }
    catch (OperationCanceledException)
    {
        Check(true, "Explorer search honors cancellation");
    }
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"\n{failures.Count} smoke test(s) failed.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("\nDragon DiskForge Core smoke tests passed.");

sealed class CaptureProgress : IProgress<double>
{
    public double Last { get; private set; }

    public void Report(double value)
        => Last = value;
}
