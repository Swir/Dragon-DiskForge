using System.Security.Cryptography;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), $"dragon-raw-provider-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    var provider = new RawDiskImageProvider();

    var imgPath = Path.Combine(root, "disk.img");
    var rawPath = Path.Combine(root, "disk.raw");
    var tinyPath = Path.Combine(root, "tiny.img");
    var renamedIsoPath = Path.Combine(root, "renamed.img");

    await File.WriteAllBytesAsync(imgPath, Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray());
    await File.WriteAllBytesAsync(rawPath, new byte[2048]);
    await File.WriteAllBytesAsync(tinyPath, new byte[128]);

    var renamedIso = new byte[0x9000];
    "CD001"u8.CopyTo(renamedIso.AsSpan(0x8001, 5));
    await File.WriteAllBytesAsync(renamedIsoPath, renamedIso);

    Check(await provider.CanHandleAsync(imgPath), "provider accepts a flat .img image");
    Check(await provider.CanHandleAsync(rawPath), "provider accepts a flat .raw image");
    Check(!await provider.CanHandleAsync(tinyPath), "provider rejects implausibly small raw images");
    Check(!await provider.CanHandleAsync(renamedIsoPath), "provider rejects a structured ISO renamed to .img");
    Check(!await provider.CanHandleAsync(Path.Combine(root, "missing.img")), "provider safely rejects missing images during probing");

    var beforeHash = await HashAsync(imgPath);
    var info = await provider.InspectAsync(imgPath);
    var afterHash = await HashAsync(imgPath);

    Check(info.Format == "IMG raw disk image", "IMG inspection reports the provider-backed format");
    Check(info.SizeBytes == 4096, "inspection preserves the exact source size");
    Check(info.CanVerify, "raw images remain verifiable");
    Check(!info.CanExplore && !info.CanMount && !info.CanConvert,
        "unimplemented RAW browse/mount/convert capabilities stay disabled");
    Check(beforeHash.SequenceEqual(afterHash), "inspection is read-only and leaves source bytes unchanged");

    var registry = new ProviderRegistry([
        new ProviderRegistration(provider, Priority: 100),
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 50)
    ]);

    var rawResolution = await registry.ResolveAsync(imgPath);
    Check(rawResolution.Provider?.Id == provider.Id, "registry resolves a real flat IMG to the RAW provider");

    var isoResolution = await registry.ResolveAsync(renamedIsoPath);
    Check(isoResolution.Provider is Iso9660DirectBrowseProvider,
        "registry falls through RAW extension preference to ISO signature detection");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try
    {
        _ = await provider.CanHandleAsync(imgPath, cts.Token);
        throw new InvalidOperationException("pre-cancelled RAW provider probe unexpectedly completed");
    }
    catch (OperationCanceledException)
    {
        Check(true, "RAW provider preserves cancellation");
    }

    Console.WriteLine("\nDragon DiskForge IMG/RAW provider smoke tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL  IMG/RAW provider smoke test: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task<byte[]> HashAsync(string path)
{
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    return await SHA256.HashDataAsync(stream);
}

static void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    Console.WriteLine($"PASS  {name}");
}
