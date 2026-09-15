using System.Text;
using DiscUtils.Iso9660;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), $"dragon-direct-iso-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
var isoPath = Path.Combine(root, "provider-test.iso");
var outputPath = Path.Combine(root, "output");
Directory.CreateDirectory(outputPath);

try
{
    BuildIso(isoPath);
    var provider = new Iso9660ImageProvider();

    Check(await provider.CanBrowseAsync(isoPath), "provider detects generated ISO9660/Joliet image");

    var inspected = await provider.InspectAsync(isoPath);
    Check(inspected.Format == "ISO" && inspected.CanExplore, "provider reports direct ISO exploration capability");

    var rootEntries = await provider.ListAsync(isoPath, string.Empty);
    Check(rootEntries.Count >= 3, "root listing returns expected entries");
    Check(rootEntries[0].Kind == ExplorerEntryKind.Directory, "root listing keeps directories before files");
    Check(rootEntries.Any(x => x.Name.Equals("docs", StringComparison.OrdinalIgnoreCase) && x.IsDirectory), "root listing contains docs folder");
    Check(rootEntries.Any(x => x.Name.Equals("README.TXT", StringComparison.OrdinalIgnoreCase) && !x.IsDirectory), "root listing contains README.TXT");

    var docs = await provider.ListAsync(isoPath, "docs");
    var guide = docs.Single(x => x.Name.Equals("guide.txt", StringComparison.OrdinalIgnoreCase));
    Check(guide.SizeBytes == Encoding.UTF8.GetByteCount("Dragon direct provider guide."), "direct listing reports file size");
    Check(docs.Any(x => x.Name.Equals("empty", StringComparison.OrdinalIgnoreCase) && x.IsDirectory), "direct listing preserves empty folders");

    var search = await provider.SearchAsync(isoPath, string.Empty, "guide", maxResults: 10);
    Check(search.Count == 1 && search[0].Name.Equals("guide.txt", StringComparison.OrdinalIgnoreCase), "direct search finds nested file");

    var preview = await provider.GetPreviewAsync(isoPath, "docs\\guide.txt", maxTextCharacters: 200);
    Check(preview.Kind == PreviewKind.Text, "direct provider classifies text preview");
    Check(preview.Text == "Dragon direct provider guide.", "direct provider reads exact text without mounting");
    Check(!preview.IsTruncated, "short direct text preview is not truncated");

    var truncated = await provider.GetPreviewAsync(isoPath, "LONG.TXT", maxTextCharacters: 32);
    Check(truncated.Kind == PreviewKind.Text && truncated.Text?.Length == 32 && truncated.IsTruncated,
        "direct provider enforces bounded text preview");

    await provider.CopyOutAsync(isoPath, "docs\\guide.txt", outputPath);
    var copiedGuide = Path.Combine(outputPath, "guide.txt");
    Check(File.Exists(copiedGuide), "direct file Copy out creates destination file");
    Check(await File.ReadAllTextAsync(copiedGuide) == "Dragon direct provider guide.", "direct file Copy out preserves content");

    var folderOutput = Path.Combine(root, "folder-output");
    Directory.CreateDirectory(folderOutput);
    await provider.CopyOutAsync(isoPath, "docs", folderOutput);
    Check(File.Exists(Path.Combine(folderOutput, "docs", "guide.txt")), "direct folder Copy out preserves nested file");
    Check(Directory.Exists(Path.Combine(folderOutput, "docs", "empty")), "direct folder Copy out preserves empty directory");

    await ExpectThrowsAsync<IOException>(
        () => provider.CopyOutAsync(isoPath, "docs\\guide.txt", outputPath),
        "direct Copy out refuses overwrite conflicts");

    await ExpectThrowsAsync<InvalidOperationException>(
        () => provider.ListAsync(isoPath, "..\\escape"),
        "direct provider rejects traversal segments");

    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await ExpectThrowsAsync<OperationCanceledException>(
        () => provider.SearchAsync(isoPath, string.Empty, "guide", cancellationToken: cancelled.Token),
        "direct search honors cancellation");

    Console.WriteLine("\nDragon DiskForge direct ISO provider smoke tests passed.");
}
finally
{
    try
    {
        Directory.Delete(root, recursive: true);
    }
    catch
    {
        // Best-effort cleanup for disposable smoke-test data.
    }
}

static void BuildIso(string path)
{
    var builder = new CDBuilder
    {
        UseJoliet = true,
        VolumeIdentifier = "DRAGONDIRECT"
    };

    builder.AddDirectory("docs\\empty");
    builder.AddDirectory("images");
    builder.AddFile("README.TXT", Encoding.UTF8.GetBytes("Dragon DiskForge direct ISO provider."));
    builder.AddFile("docs\\guide.txt", Encoding.UTF8.GetBytes("Dragon direct provider guide."));
    builder.AddFile("LONG.TXT", Encoding.UTF8.GetBytes(new string('D', 256)));
    builder.AddFile("images\\test.bin", Enumerable.Range(0, 64).Select(x => (byte)x).ToArray());
    builder.Build(path);
}

static void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    Console.WriteLine($"PASS  {name}");
}

static async Task ExpectThrowsAsync<T>(Func<Task> action, string name)
    where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        Check(true, name);
        return;
    }

    throw new InvalidOperationException(name);
}
