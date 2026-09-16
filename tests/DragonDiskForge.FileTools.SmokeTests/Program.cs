using System.Security.Cryptography;
using System.Text.Json;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonFileTools-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var tools = new ImageFileToolsService();
int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
async Task Reject(Func<Task> action, string name)
{
    try { await action(); }
    catch (Exception ex) when (ex is IOException or ArgumentException or OperationCanceledException or InvalidDataException)
    { Check(true, name); return; }
    throw new Exception("Expected rejection: " + name);
}
try
{
    var source = Path.Combine(root, "source.raw");
    var bytes = new byte[270001];
    RandomNumberGenerator.Fill(bytes);
    await File.WriteAllBytesAsync(source, bytes);
    var verify = new ImageVerificationService();
    Check(await verify.ComputeHashAsync(source, "SHA-512") == Convert.ToHexString(SHA512.HashData(bytes)), "SHA-512 matches independent hash");
    Check(await verify.ComputeSha256Async(source) == Convert.ToHexString(SHA256.HashData(bytes)), "SHA-256 compatibility");
    await Reject(() => verify.ComputeHashAsync(source, "md5"), "unsupported algorithm rejected");
    var compressed = Path.Combine(root, "source.gz");
    var expanded = Path.Combine(root, "expanded.raw");
    await tools.CompressAsync(source, compressed);
    await tools.DecompressAsync(compressed, expanded, bytes.Length);
    Check((await File.ReadAllBytesAsync(expanded)).SequenceEqual(bytes), "GZip round trip");
    var limited = Path.Combine(root, "limited.raw");
    await Reject(() => tools.DecompressAsync(compressed, limited, bytes.Length - 1), "decompression limit enforced");
    Check(!File.Exists(limited), "no output after decompression limit failure");
    await Reject(() => tools.CompressAsync(source, expanded), "existing destination refused");
    Check((await File.ReadAllBytesAsync(expanded)).SequenceEqual(bytes), "existing destination unchanged");

    var manifestPath = await tools.SplitAsync(source, Path.Combine(root, "parts"), 70000);
    var joined = Path.Combine(root, "joined.raw");
    await tools.JoinAsync(manifestPath, joined);
    Check((await File.ReadAllBytesAsync(joined)).SequenceEqual(bytes), "split/join round trip including partial final part");
    var manifest = JsonSerializer.Deserialize<SplitImageManifest>(await File.ReadAllTextAsync(manifestPath))!;
    Check(manifest.Parts.Count == 4, "expected part count");
    var part = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Parts[0].Name);
    var partBytes = await File.ReadAllBytesAsync(part);
    partBytes[0] ^= 255;
    await File.WriteAllBytesAsync(part, partBytes);
    var corruptDestination = Path.Combine(root, "corrupt.raw");
    await Reject(() => tools.JoinAsync(manifestPath, corruptDestination), "tampered part refused");
    Check(!File.Exists(corruptDestination), "tampering leaves no completed output");

    var unsafeManifest = manifest with { Parts = [manifest.Parts[0] with { Name = "../source.raw" }] };
    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(unsafeManifest));
    await Reject(() => tools.JoinAsync(manifestPath, corruptDestination), "manifest path traversal rejected");

    var raw = Path.Combine(root, "blank.raw");
    await tools.CreateRawAsync(raw, 4096);
    Check(new FileInfo(raw).Length == 4096 && (await File.ReadAllBytesAsync(raw)).All(x => x == 0), "blank RAW exact size and zeros");
    await Reject(() => tools.CreateRawAsync(Path.Combine(root, "bad.raw"), 513), "unaligned raw size rejected");
    var cancelled = Path.Combine(root, "cancelled.raw");
    await Reject(() => tools.CreateRawAsync(cancelled, 4096, new CancellationToken(true)), "pre-cancelled operation rejected");
    Check(!File.Exists(cancelled), "cancel leaves no output");
    await Reject(() => AtomicFileOutput.WriteAsync(cancelled, async stream =>
    {
        await stream.WriteAsync(bytes);
        throw new IOException("simulated disk error");
    }), "partial writer failure");
    Check(!File.Exists(cancelled) && !Directory.EnumerateFiles(root, "*.partial").Any(), "partial output cleaned up");
    var empty = Path.Combine(root, "empty.raw");
    await File.WriteAllBytesAsync(empty, []);
    var emptyManifest = await tools.SplitAsync(empty, Path.Combine(root, "empty-parts"), 1000);
    await tools.JoinAsync(emptyManifest, Path.Combine(root, "empty-joined.raw"));
    Check(new FileInfo(Path.Combine(root, "empty-joined.raw")).Length == 0, "empty-file split/join");
    var report = await new ImageReportService().AnalyzeAsync(raw);
    Check(report.Image.SizeBytes == 4096, "report retains basic image metadata");
    Check(ImageReportService.ToJson(report).Contains("Diagnostics"), "report JSON exports limitations");
    Check((await File.ReadAllBytesAsync(source)).SequenceEqual(bytes), "source remains unchanged");
    Console.WriteLine($"{passed} file-tool checks passed.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Directory.Delete(root, true); }
