using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-Floppy-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new FloppyImageProvider();

    var fat12Path = Path.Combine(root, "dragon.ima");
    CreateFat12Floppy(fat12Path);
    Expect(await provider.CanHandleAsync(fat12Path), "A valid 1.44 MB IMA image should be recognized.");

    var info = await provider.ReadMediaGeometryAsync(fat12Path);
    Expect(info.GeometryName.Contains("1.44 MB", StringComparison.Ordinal), "1.44 MB geometry should be reported.");
    Expect(info.BytesPerSector == 512, "FAT12 BPB bytes/sector should be parsed.");
    Expect(info.SectorsPerTrack == 18 && info.Heads == 2 && info.Tracks == 80, "1.44 MB CHS geometry should be parsed.");
    Expect(info.TotalSectors == 2880, "1.44 MB total sectors should be parsed.");
    Expect(info.BootParameterBlockDetected, "Valid FAT12 BPB should be detected.");
    Expect(info.FileSystemHint.Contains("FAT12", StringComparison.OrdinalIgnoreCase), "FAT12 filesystem hint should be exposed.");
    Expect(info.VolumeLabel == "DRAGON", "FAT12 volume label should be decoded.");

    var inspect = await provider.InspectAsync(fat12Path);
    Expect(inspect.Format == "IMA / Floppy", "Inspect should report IMA / Floppy.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify, "Floppy provider must not fake browse/mount/convert.");

    var blankPath = Path.Combine(root, "blank.flp");
    await CreateSizedFileAsync(blankPath, 737_280);
    Expect(await provider.CanHandleAsync(blankPath), "A blank standard-size 720 KB raw floppy should be recognized by geometry.");
    var blank = await provider.ReadMediaGeometryAsync(blankPath);
    Expect(!blank.BootParameterBlockDetected, "A blank image should not fake a BPB.");
    Expect(blank.SectorsPerTrack == 9 && blank.Heads == 2 && blank.Tracks == 80, "720 KB raw geometry should be inferred safely from exact size.");

    var fakePath = Path.Combine(root, "fake.ima");
    await CreateSizedFileAsync(fakePath, 1_000_000);
    Expect(!await provider.CanHandleAsync(fakePath), "Unsupported image size must not be accepted merely because the extension is .ima.");

    var corruptPath = Path.Combine(root, "corrupt.ima");
    CreateFat12Floppy(corruptPath);
    using (var stream = new FileStream(corruptPath, FileMode.Open, FileAccess.Write, FileShare.None))
    {
        stream.Position = 19;
        Span<byte> wrongTotal = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(wrongTotal, 1440);
        stream.Write(wrongTotal);
    }
    Expect(!await provider.CanHandleAsync(corruptPath), "A BPB whose capacity disagrees with the image must be rejected.");

    var foreignPath = Path.Combine(root, "dragon.vhd");
    File.Copy(fat12Path, foreignPath);
    Expect(!await provider.CanHandleAsync(foreignPath), "Floppy provider must not claim foreign container extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadMediaGeometryAsync(fat12Path, cts.Token).AsTask(),
        "Pre-cancelled floppy parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(provider, Priority: 80)
    ]);
    var resolution = await registry.ResolveAsync(fat12Path);
    Expect(resolution.Provider is FloppyImageProvider, "Provider registry should resolve IMA through the floppy provider.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.MediaGeometry) == true, "Floppy provider should report MediaGeometry capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false, "Floppy provider must not advertise DirectBrowse before filesystem support exists.");

    Console.WriteLine("Dragon DiskForge IMA/floppy provider smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateFat12Floppy(string path)
{
    const int imageSize = 1_474_560;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    stream.SetLength(imageSize);

    var boot = new byte[512];
    boot[0] = 0xEB;
    boot[1] = 0x3C;
    boot[2] = 0x90;
    Encoding.ASCII.GetBytes("DRAGON  ").CopyTo(boot, 3);
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11, 2), 512);
    boot[13] = 1;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(14, 2), 1);
    boot[16] = 2;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(17, 2), 224);
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(19, 2), 2880);
    boot[21] = 0xF0;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(22, 2), 9);
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(24, 2), 18);
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(26, 2), 2);
    boot[36] = 0;
    boot[37] = 0;
    boot[38] = 0x29;
    BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(39, 4), 0xD12A600D);
    Encoding.ASCII.GetBytes("DRAGON     ").CopyTo(boot, 43);
    Encoding.ASCII.GetBytes("FAT12   ").CopyTo(boot, 54);
    boot[510] = 0x55;
    boot[511] = 0xAA;

    stream.Write(boot);
}

static async Task CreateSizedFileAsync(string path, long length)
{
    await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
    stream.SetLength(length);
    await stream.FlushAsync();
}

static async Task ExpectCanceledAsync(Func<Task> action, string message)
{
    try
    {
        await action();
        throw new InvalidOperationException(message);
    }
    catch (OperationCanceledException)
    {
    }
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
