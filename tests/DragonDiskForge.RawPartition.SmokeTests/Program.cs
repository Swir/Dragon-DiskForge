using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-RawPartition-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new RawPartitionImageProvider();

    var mbrPath = Path.Combine(root, "mbr.img");
    CreateMbrImage(mbrPath);
    Expect(await provider.CanHandleAsync(mbrPath), "MBR image should be recognized.");

    var mbr = await provider.ReadPartitionTableAsync(mbrPath);
    Expect(mbr.Scheme == PartitionTableScheme.Mbr, "MBR scheme should be reported.");
    Expect(mbr.SectorSize == 512, "MBR logical sector size should be 512.");
    Expect(mbr.Partitions.Count == 2, "MBR image should expose primary + logical partitions.");
    Expect(mbr.Partitions[0].Index == 1, "Primary partition index should be 1.");
    Expect(mbr.Partitions[0].FirstLba == 2048, "Primary partition LBA should match.");
    Expect(mbr.Partitions[0].IsBootable, "Primary boot flag should be preserved.");
    Expect(mbr.Partitions[0].TypeName.Contains("FAT32", StringComparison.Ordinal), "Primary type should be decoded.");
    Expect(mbr.Partitions[1].Index == 5, "Logical partition numbering should start at 5.");
    Expect(mbr.Partitions[1].FirstLba == 14336, "Logical partition absolute LBA should be resolved through the EBR chain.");

    var mbrInfo = await provider.InspectAsync(mbrPath);
    Expect(mbrInfo.Format == "IMG / RAW (MBR)", "Inspect should report MBR RAW format.");
    Expect(!mbrInfo.CanExplore && !mbrInfo.CanMount && mbrInfo.CanVerify, "RAW provider must stay read-only and must not fake browse/mount.");

    var gptPath = Path.Combine(root, "gpt.raw");
    CreateGptImage(gptPath);
    Expect(await provider.CanHandleAsync(gptPath), "GPT image should be recognized.");

    var gpt = await provider.ReadPartitionTableAsync(gptPath);
    Expect(gpt.Scheme == PartitionTableScheme.Gpt, "GPT scheme should be reported.");
    Expect(gpt.SectorSize == 512, "GPT sector size should be detected.");
    Expect(gpt.Partitions.Count == 2, "GPT image should expose two partitions.");
    Expect(gpt.Partitions[0].Name == "EFI", "GPT UTF-16 partition name should be decoded.");
    Expect(gpt.Partitions[0].TypeName == "EFI System", "EFI GPT type should be decoded.");
    Expect(gpt.Partitions[1].Name == "DragonData", "Second GPT partition name should be decoded.");
    Expect(gpt.Partitions[1].TypeName == "Microsoft Basic Data", "Microsoft Basic Data GUID should be decoded.");

    var fakePath = Path.Combine(root, "fake.img");
    await File.WriteAllBytesAsync(fakePath, new byte[4096]);
    Expect(!await provider.CanHandleAsync(fakePath), "A fake .img extension must not be accepted without a valid partition table.");

    var foreignExtensionPath = Path.Combine(root, "mbr.vhd");
    File.Copy(mbrPath, foreignExtensionPath);
    Expect(!await provider.CanHandleAsync(foreignExtensionPath), "RAW provider must not claim a foreign container extension merely because its payload starts with an MBR.");

    var outOfBoundsPath = Path.Combine(root, "bad.img");
    CreateOutOfBoundsMbr(outOfBoundsPath);
    Expect(!await provider.CanHandleAsync(outOfBoundsPath), "Out-of-bounds MBR partition must be rejected.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadPartitionTableAsync(mbrPath, cts.Token).AsTask(),
        "Pre-cancelled RAW parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(provider, Priority: 90)
    ]);

    var resolution = await registry.ResolveAsync(gptPath);
    Expect(resolution.Provider is RawPartitionImageProvider, "Provider registry should resolve GPT RAW through the RAW provider.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.PartitionTable) == true, "RAW provider should report PartitionTable capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false, "RAW provider must not advertise DirectBrowse before filesystem support exists.");

    var foreignResolution = await registry.ResolveAsync(foreignExtensionPath);
    Expect(foreignResolution.Provider is null, "Provider fallback must not let the RAW provider steal a foreign container extension.");

    Console.WriteLine("Dragon DiskForge RAW/IMG partition-provider smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateMbrImage(string path)
{
    const int sectorSize = 512;
    const int sectors = 32768;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    stream.SetLength((long)sectorSize * sectors);

    var mbr = new byte[sectorSize];
    WriteMbrEntry(mbr, 0, 0x80, 0x0C, 2048, 4096);
    WriteMbrEntry(mbr, 1, 0x00, 0x0F, 12288, 12288);
    mbr[510] = 0x55;
    mbr[511] = 0xAA;
    stream.Position = 0;
    stream.Write(mbr);

    var ebr = new byte[sectorSize];
    WriteMbrEntry(ebr, 0, 0x00, 0x83, 2048, 4096);
    ebr[510] = 0x55;
    ebr[511] = 0xAA;
    stream.Position = 12288L * sectorSize;
    stream.Write(ebr);
}

static void CreateGptImage(string path)
{
    const int sectorSize = 512;
    const int sectors = 32768;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    stream.SetLength((long)sectorSize * sectors);

    var mbr = new byte[sectorSize];
    WriteMbrEntry(mbr, 0, 0x00, 0xEE, 1, (uint)(sectors - 1));
    mbr[510] = 0x55;
    mbr[511] = 0xAA;
    stream.Position = 0;
    stream.Write(mbr);

    var header = new byte[sectorSize];
    Encoding.ASCII.GetBytes("EFI PART").CopyTo(header, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 0x00010000);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), 92);
    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(24, 8), 1);
    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32, 8), (ulong)(sectors - 1));
    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(40, 8), 34);
    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(48, 8), (ulong)(sectors - 34));
    Guid.NewGuid().ToByteArray().CopyTo(header, 56);
    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(72, 8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80, 4), 128);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(84, 4), 128);
    stream.Position = sectorSize;
    stream.Write(header);

    var entry = new byte[128];
    WriteGptEntry(
        entry,
        Guid.Parse("C12A7328-F81F-11D2-BA4B-00A0C93EC93B"),
        2048,
        4095,
        "EFI");
    stream.Position = 2L * sectorSize;
    stream.Write(entry);

    Array.Clear(entry, 0, entry.Length);
    WriteGptEntry(
        entry,
        Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"),
        4096,
        16383,
        "DragonData");
    stream.Position = (2L * sectorSize) + 128;
    stream.Write(entry);
}

static void CreateOutOfBoundsMbr(string path)
{
    const int sectorSize = 512;
    using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    stream.SetLength(4096);

    var mbr = new byte[sectorSize];
    WriteMbrEntry(mbr, 0, 0x00, 0x07, 7, 100);
    mbr[510] = 0x55;
    mbr[511] = 0xAA;
    stream.Write(mbr);
}

static void WriteMbrEntry(byte[] sector, int slot, byte status, byte type, uint firstLba, uint sectorCount)
{
    var offset = 446 + (slot * 16);
    sector[offset] = status;
    sector[offset + 4] = type;
    BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(offset + 8, 4), firstLba);
    BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(offset + 12, 4), sectorCount);
}

static void WriteGptEntry(byte[] entry, Guid typeGuid, ulong firstLba, ulong lastLba, string name)
{
    typeGuid.ToByteArray().CopyTo(entry, 0);
    Guid.NewGuid().ToByteArray().CopyTo(entry, 16);
    BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(32, 8), firstLba);
    BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(40, 8), lastLba);

    var nameBytes = Encoding.Unicode.GetBytes(name);
    Array.Copy(nameBytes, 0, entry, 56, Math.Min(nameBytes.Length, 72));
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
