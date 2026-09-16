using DragonDiskForge.Core.Services;
using DragonDiskForge.Core.Providers;
using DiscUtils.Streams;

var root = Path.Combine(Path.GetTempPath(), "DragonCreation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
try
{
    var service = new ImageCreationService();
    var raw = Path.Combine(root, "original.raw");
    await new ImageFileToolsService().CreateRawAsync(raw, 64 * 1024 * 1024);
    using (var source = new FileStream(raw, FileMode.Open, FileAccess.Write))
    {
        source.Write(new byte[] { 3, 1, 4, 1, 5, 9 });
        source.Position = source.Length - 512;
        source.Write(new byte[] { 2, 7, 1, 8, 2, 8 });
    }
    var verifier = new ImageVerificationService();
    var original = await verifier.ComputeSha256Async(raw);
    foreach (var format in new[] { "vhd", "vhdx" })
    {
        var diskPath = Path.Combine(root, "converted." + format);
        var roundtrip = Path.Combine(root, "roundtrip-" + format + ".raw");
        await service.ConvertAsync(raw, diskPath, format);
        await service.ConvertAsync(diskPath, roundtrip, "raw");
        Check(await verifier.ComputeSha256Async(roundtrip) == original, format + " preserves every guest byte and disk capacity");
        Check(new FileInfo(diskPath).Length < new FileInfo(raw).Length, format + " zero blocks remain sparse");
        var empty = Path.Combine(root, "empty." + format);
        await service.CreateVirtualDiskAsync(empty, 32 * 1024 * 1024, format);
        using var file = File.OpenRead(empty);
        using DiscUtils.VirtualDisk disk = format == "vhd"
            ? new DiscUtils.Vhd.Disk(file, Ownership.None)
            : new DiscUtils.Vhdx.Disk(file, Ownership.None);
        Check(disk.Capacity == 32 * 1024 * 1024, format + " creation capacity");
    }
    var folder = Path.Combine(root, "folder");
    Directory.CreateDirectory(Path.Combine(folder, "empty"));
    Directory.CreateDirectory(Path.Combine(folder, "nested"));
    await File.WriteAllTextAsync(Path.Combine(folder, "nested", "hello.txt"), "Dragon test data");
    var iso = Path.Combine(root, "created.iso");
    await service.CreateIsoAsync(folder, iso, "DRAGON_TEST");
    var provider = new Iso9660DirectBrowseProvider();
    Check(await provider.CanHandleAsync(iso), "created ISO recognized by independent Dragon parser");
    var entries = await provider.ListAsync(iso, "/");
    Check(entries.Any(x => x.Name == "empty" && x.IsDirectory), "ISO preserves empty folders");
    var found = await provider.SearchAsync(iso, "/", "hello");
    Check(found.Count == 1, "ISO nested file searchable");
    var extracted = Path.Combine(root, "extracted");
    Directory.CreateDirectory(extracted);
    await provider.CopyOutAsync(iso, found[0].FullPath, extracted);
    Check(await File.ReadAllTextAsync(Path.Combine(extracted, "hello.txt")) == "Dragon test data", "ISO content round trip");
    var cancelled = Path.Combine(root, "cancelled.vhd");
    try { await service.ConvertAsync(raw, cancelled, "vhd", token: new CancellationToken(true)); throw new Exception("Cancellation not honored"); }
    catch (OperationCanceledException) { Check(!File.Exists(cancelled), "cancelled conversion leaves no output"); }
    try { await service.CreateIsoAsync(folder, Path.Combine(folder, "recursive.iso")); throw new Exception("Output accepted inside source"); }
    catch (IOException) { Check(true, "ISO output inside source rejected"); }
    Check(await verifier.ComputeSha256Async(raw) == original, "conversion leaves source unchanged");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Directory.Delete(root, true); }
