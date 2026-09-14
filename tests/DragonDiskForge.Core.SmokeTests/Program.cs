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
var previewer = new FilePreviewService();

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
    var empty = Path.Combine(folder, "EmptyFolder");
    Directory.CreateDirectory(nested);
    Directory.CreateDirectory(empty);
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
        var copiedEmpty = Path.Combine(exportRoot, "FolderA", "EmptyFolder");
        Check(File.Exists(copiedNested), "Explorer copy-out preserves nested directory structure");
        Check(Directory.Exists(copiedEmpty), "Explorer copy-out preserves empty directories");
        Check(await File.ReadAllTextAsync(copiedNested) == "payload-data", "Explorer copy-out preserves file content");
        Check(copyProgress.Last >= 0.999d, "Explorer copy-out reports completion progress");

        try
        {
            await explorer.CopyOutAsync(root, folder, exportRoot);
            Check(false, "Explorer copy-out refuses to overwrite an existing destination");
        }
        catch (IOException)
        {
            Check(true, "Explorer copy-out refuses to overwrite an existing destination");
        }
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

await WithTempDirectoryAsync(async root =>
{
    var textPath = Path.Combine(root, "preview.txt");
    await File.WriteAllTextAsync(textPath, new string('D', 80));
    var textPreview = await previewer.GetPreviewAsync(textPath, maxTextCharacters: 32);
    Check(textPreview.Kind == PreviewKind.Text, "Preview service classifies text files");
    Check(textPreview.Text?.Length == 32 && textPreview.IsTruncated, "Preview service enforces bounded text reads");

    var imagePath = Path.Combine(root, "cover.png");
    await File.WriteAllBytesAsync(imagePath, new byte[] { 1, 2, 3, 4 });
    Check((await previewer.GetPreviewAsync(imagePath)).Kind == PreviewKind.Image, "Preview service classifies images without executing them");

    var pdfPath = Path.Combine(root, "manual.pdf");
    await File.WriteAllTextAsync(pdfPath, "%PDF-1.7");
    Check((await previewer.GetPreviewAsync(pdfPath)).Kind == PreviewKind.PdfMetadata, "Preview service exposes PDF metadata mode");

    var mediaPath = Path.Combine(root, "audio.mp3");
    await File.WriteAllBytesAsync(mediaPath, new byte[16]);
    Check((await previewer.GetPreviewAsync(mediaPath)).Kind == PreviewKind.MediaMetadata, "Preview service exposes media metadata mode without auto-play");

    try
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await previewer.GetPreviewAsync(textPath, cancellationToken: cancelled.Token);
        Check(false, "Preview service honors cancellation");
    }
    catch (OperationCanceledException)
    {
        Check(true, "Preview service honors cancellation");
    }

    var libraryPath = Path.Combine(root, "library", "images.json");
    var imageA = Path.Combine(root, "A.iso");
    var imageB = Path.Combine(root, "B.vhdx");
    var imageC = Path.Combine(root, "C.iso");
    await File.WriteAllBytesAsync(imageA, new byte[1]);
    await File.WriteAllBytesAsync(imageB, new byte[1]);
    await File.WriteAllBytesAsync(imageC, new byte[1]);

    using (var library = new JsonImageLibraryService(libraryPath, maxRecents: 2))
    {
        await library.RecordOpenedAsync(imageA);
        await Task.Delay(5);
        await library.RecordOpenedAsync(imageB);
        var favoriteSnapshot = await library.SetFavoriteAsync(imageA, true);
        Check(favoriteSnapshot.Favorites.Count == 1 && favoriteSnapshot.Favorites[0].Path == Path.GetFullPath(imageA), "Image library persists favorite state");

        await Task.Delay(5);
        var pruned = await library.RecordOpenedAsync(imageC);
        Check(pruned.Recents.Count == 2 && pruned.Recents[0].Path == Path.GetFullPath(imageC), "Image library keeps bounded recents in newest-first order");
        Check(pruned.Favorites.Any(x => x.Path == Path.GetFullPath(imageA)), "Favorite survives recent-list pruning");

        await library.RecordOpenedAsync(imageC);
        var deduplicated = await library.GetAsync();
        Check(deduplicated.Recents.Count(x => x.Path == Path.GetFullPath(imageC)) == 1, "Image library deduplicates repeated opens");
    }

    using (var reloaded = new JsonImageLibraryService(libraryPath, maxRecents: 2))
    {
        var persisted = await reloaded.GetAsync();
        Check(persisted.Favorites.Any(x => x.Path == Path.GetFullPath(imageA)), "Image library survives process-style reload from JSON");
        var removed = await reloaded.RemoveAsync(imageA);
        Check(!removed.Favorites.Any(x => x.Path == Path.GetFullPath(imageA)), "Image library removes entries cleanly");
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
