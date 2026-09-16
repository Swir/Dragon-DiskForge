using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-FsRecognition-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await ValidateFatFamiliesAsync(root);
    await ValidateExFatAsync(root);
    await ValidateNtfsAsync(root);
    await ValidateExtFamilyAsync(root);
    await ValidateIsoAndJolietAsync(root);
    await ValidateUdfAsync(root);
    await ValidatePartitionScopedRecognitionAsync(root);
    await ValidateNoFalsePositiveAsync(root);
    await ValidateProviderGateAsync(root);
    await ValidateInvalidPartitionGateAsync(root);
    await ValidateCancellationAsync(root);

    Console.WriteLine("Dragon DiskForge filesystem-recognition smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task ValidateFatFamiliesAsync(string root)
{
    var fat12 = BuildFatBoot(totalSectors: 2880, sectorsPerCluster: 1, reserved: 1, fats: 2,
        rootEntries: 224, fat16Sectors: 9, fat32Sectors: 0, label: "DRAGON12", serial: 0x12001200);
    var fat12Path = await CreateImageAsync(root, "fat12.img", 2880L * 512, (0, fat12));
    var fat12Info = await AnalyzeWholeAsync(fat12Path);
    var d12 = ExpectSingle(fat12Info, FileSystemKind.Fat12, "FAT12 is classified from BPB cluster count.");
    Expect(d12.Label == "DRAGON12" && d12.Identifier == "12001200", "FAT12 label and serial metadata are preserved.");
    Expect(d12.LogicalBlockSize == 512 && d12.AllocationUnitSize == 512, "FAT12 block/allocation geometry is reported.");

    var fat16 = BuildFatBoot(totalSectors: 32768, sectorsPerCluster: 4, reserved: 1, fats: 2,
        rootEntries: 512, fat16Sectors: 32, fat32Sectors: 0, label: "DRAGON16", serial: 0x16001600);
    var fat16Path = await CreateImageAsync(root, "fat16.img", 32768L * 512, (0, fat16));
    var fat16Info = await AnalyzeWholeAsync(fat16Path);
    var d16 = ExpectSingle(fat16Info, FileSystemKind.Fat16, "FAT16 is classified from BPB cluster count.");
    Expect(d16.Label == "DRAGON16" && d16.AllocationUnitSize == 2048, "FAT16 label and cluster geometry are reported.");

    const uint fat32Sectors = 131072;
    var fat32 = BuildFatBoot(totalSectors: fat32Sectors, sectorsPerCluster: 1, reserved: 32, fats: 2,
        rootEntries: 0, fat16Sectors: 0, fat32Sectors: 1000, label: "DRAGON32", serial: 0x32003200);
    var fat32Path = await CreateImageAsync(root, "fat32.img", fat32Sectors * 512L, (0, fat32));
    var fat32Info = await AnalyzeWholeAsync(fat32Path);
    var d32 = ExpectSingle(fat32Info, FileSystemKind.Fat32, "FAT32 is classified from BPB cluster count.");
    Expect(d32.Label == "DRAGON32" && d32.Identifier == "32003200", "FAT32 extended BPB metadata is reported.");
}

static async Task ValidateExFatAsync(string root)
{
    const ulong sectors = 32768;
    var boot = new byte[512];
    boot[0] = 0xEB;
    boot[1] = 0x76;
    boot[2] = 0x90;
    WriteAscii(boot, 3, "EXFAT   ");
    BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(72, 8), sectors);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(80, 4), 24);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(84, 4), 128);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(88, 4), 256);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(92, 4), 4000);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(96, 4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(100, 4), 0xEFA70001);
    boot[108] = 9;
    boot[109] = 3;
    boot[110] = 1;
    boot[510] = 0x55;
    boot[511] = 0xAA;

    var path = await CreateImageAsync(root, "exfat.img", checked((long)sectors * 512), (0, boot));
    var info = await AnalyzeWholeAsync(path);
    var detection = ExpectSingle(info, FileSystemKind.ExFat, "exFAT OEM/geometry is recognized.");
    Expect(detection.Identifier == "EFA70001", "exFAT serial is reported.");
    Expect(detection.LogicalBlockSize == 512 && detection.AllocationUnitSize == 4096,
        "exFAT sector/cluster shifts become bounded byte geometry.");
}

static async Task ValidateNtfsAsync(string root)
{
    const ulong sectors = 32768;
    var boot = new byte[512];
    boot[0] = 0xEB;
    boot[1] = 0x52;
    boot[2] = 0x90;
    WriteAscii(boot, 3, "NTFS    ");
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11, 2), 512);
    boot[13] = 8;
    BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(40, 8), sectors);
    BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(48, 8), 4);
    BinaryPrimitives.WriteUInt64LittleEndian(boot.AsSpan(72, 8), 0x1122334455667788UL);
    boot[510] = 0x55;
    boot[511] = 0xAA;

    var path = await CreateImageAsync(root, "ntfs.img", checked((long)sectors * 512), (0, boot));
    var info = await AnalyzeWholeAsync(path);
    var detection = ExpectSingle(info, FileSystemKind.Ntfs, "NTFS OEM/BPB is recognized.");
    Expect(detection.Identifier == "1122334455667788", "NTFS volume serial is reported.");
    Expect(detection.AllocationUnitSize == 4096, "NTFS cluster size is reported.");
}

static async Task ValidateExtFamilyAsync(string root)
{
    var ext2Path = await CreateExtImageAsync(root, "ext2.img", compat: 0, incompat: 0, "DRAGON2");
    var ext2 = await AnalyzeWholeAsync(ext2Path);
    ExpectSingle(ext2, FileSystemKind.Ext2, "ext2 is recognized from superblock magic without journal/ext4 features.");

    var ext3Path = await CreateExtImageAsync(root, "ext3.img", compat: 0x0004, incompat: 0, "DRAGON3");
    var ext3 = await AnalyzeWholeAsync(ext3Path);
    ExpectSingle(ext3, FileSystemKind.Ext3, "ext3 is recognized from the journal feature.");

    var ext4Path = await CreateExtImageAsync(root, "ext4.img", compat: 0x0004, incompat: 0x0040, "DRAGON4");
    var ext4 = await AnalyzeWholeAsync(ext4Path);
    var detection = ExpectSingle(ext4, FileSystemKind.Ext4, "ext4 is recognized from ext4-specific incompat features.");
    Expect(detection.Label == "DRAGON4", "ext volume label is reported.");
    Expect(detection.Identifier == "01020304-0506-0708-090a-0b0c0d0e0f10", "ext UUID is preserved in byte order.");
    Expect(detection.LogicalBlockSize == 1024, "ext block size is derived from the superblock.");
}

static async Task ValidateIsoAndJolietAsync(string root)
{
    const int sectorSize = 2048;
    const int sectors = 64;
    var pvd = new byte[sectorSize];
    pvd[0] = 1;
    WriteAscii(pvd, 1, "CD001");
    pvd[6] = 1;
    WriteAsciiPadded(pvd, 40, 32, "DRAGONISO");
    BinaryPrimitives.WriteUInt32LittleEndian(pvd.AsSpan(80, 4), sectors);
    BinaryPrimitives.WriteUInt32BigEndian(pvd.AsSpan(84, 4), sectors);
    BinaryPrimitives.WriteUInt16LittleEndian(pvd.AsSpan(128, 2), sectorSize);
    BinaryPrimitives.WriteUInt16BigEndian(pvd.AsSpan(130, 2), sectorSize);

    var svd = (byte[])pvd.Clone();
    svd[0] = 2;
    WriteAscii(svd, 88, "%/E");
    Array.Clear(svd, 40, 32);
    var jolietLabel = Encoding.BigEndianUnicode.GetBytes("DRAGONJOLIET");
    jolietLabel.CopyTo(svd.AsSpan(40, Math.Min(32, jolietLabel.Length)));

    var terminator = new byte[sectorSize];
    terminator[0] = 255;
    WriteAscii(terminator, 1, "CD001");
    terminator[6] = 1;

    var path = await CreateImageAsync(
        root,
        "iso.img",
        sectors * (long)sectorSize,
        (16L * sectorSize, pvd),
        (17L * sectorSize, svd),
        (18L * sectorSize, terminator));

    var info = await AnalyzeWholeAsync(path);
    var detection = ExpectSingle(info, FileSystemKind.Iso9660, "ISO9660 primary descriptor is recognized.");
    Expect(detection.Variant.Contains("Joliet", StringComparison.Ordinal), "Joliet supplementary descriptor is surfaced as a variant.");
    Expect(detection.Label == "DRAGONJOLIET", "Joliet UCS-2 volume label is preferred when present.");
    Expect(detection.LogicalBlockSize == sectorSize, "ISO logical block size is reported.");
}

static async Task ValidateUdfAsync(string root)
{
    const int sectorSize = 2048;
    const int sectors = 64;
    var bea = BuildVrsDescriptor("BEA01");
    var nsr = BuildVrsDescriptor("NSR03");
    var tea = BuildVrsDescriptor("TEA01");

    var path = await CreateImageAsync(
        root,
        "udf.img",
        sectors * (long)sectorSize,
        (16L * sectorSize, bea),
        (17L * sectorSize, nsr),
        (18L * sectorSize, tea));

    var info = await AnalyzeWholeAsync(path);
    var detection = ExpectSingle(info, FileSystemKind.Udf, "UDF VRS ordering is recognized.");
    Expect(detection.Variant == "NSR03", "UDF NSR revision is preserved.");
}

static async Task ValidatePartitionScopedRecognitionAsync(string root)
{
    const int sectorSize = 512;
    const int firstLba = 2048;
    const int partitionSectors = 2880;
    var offset = firstLba * (long)sectorSize;
    var size = partitionSectors * (long)sectorSize;
    var boot = BuildFatBoot(partitionSectors, 1, 1, 2, 224, 9, 0, "PARTFAT12", 0xCAFE1200);
    var imagePath = await CreateImageAsync(root, "partitioned.img", offset + size + (1024 * 1024), (offset, boot));

    var table = new PartitionTableInfo(
        PartitionTableScheme.Mbr,
        sectorSize,
        [new PartitionInfo(7, firstLba, partitionSectors, offset, size, "0x01", "FAT12", "Boot", true)]);
    var provider = new FakePartitionProvider("fake-partition-fs", [".img"], table);
    var service = new FileSystemRecognitionService(new ProviderRegistry([provider]));
    var info = await service.AnalyzeAsync(imagePath);

    var detection = ExpectSingle(info, FileSystemKind.Fat12, "filesystem recognition scans provider-reported partition ranges.");
    Expect(detection.PartitionIndex == 7 && detection.PhysicalOffsetBytes == offset,
        "partition-scoped filesystem evidence preserves partition index and physical offset.");
}

static async Task ValidateNoFalsePositiveAsync(string root)
{
    var path = await CreateImageAsync(root, "blank.img", 256 * 1024);
    var info = await AnalyzeWholeAsync(path);
    Expect(!info.HasDetections && info.Detections.Count == 0, "blank recognized images do not gain a fake filesystem.");
}

static async Task ValidateProviderGateAsync(string root)
{
    var path = await CreateImageAsync(root, "unsupported.img", 4096);
    var service = new FileSystemRecognitionService(new ProviderRegistry([
        new FakeProvider("reject", [".img"], canHandle: false)
    ]));

    await ExpectThrowsAsync<NotSupportedException>(
        () => service.AnalyzeAsync(path),
        "filesystem recognition refuses images no registered provider recognizes.");
}

static async Task ValidateInvalidPartitionGateAsync(string root)
{
    var path = await CreateImageAsync(root, "bad-partition.img", 4096);
    var table = new PartitionTableInfo(
        PartitionTableScheme.Mbr,
        512,
        [new PartitionInfo(1, 4, 16, 4 * 512, 16 * 512, "0x01", "FAT12", "bad", false)]);
    var service = new FileSystemRecognitionService(new ProviderRegistry([
        new FakePartitionProvider("bad-layout", [".img"], table)
    ]));

    await ExpectThrowsAsync<InvalidDataException>(
        () => service.AnalyzeAsync(path),
        "filesystem recognition refuses structurally out-of-bounds partition layouts.");
}

static async Task ValidateCancellationAsync(string root)
{
    var path = await CreateImageAsync(root, "cancel.img", 4096);
    var service = new FileSystemRecognitionService(new ProviderRegistry([
        new FakeProvider("cancel", [".img"], canHandle: true)
    ]));
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    await ExpectCanceledAsync(
        () => service.AnalyzeAsync(path, cts.Token),
        "filesystem recognition preserves cancellation as a hard stop.");
}

static async Task<FileSystemRecognitionInfo> AnalyzeWholeAsync(string imagePath)
{
    var provider = new FakeProvider("physical-file", [".img"], canHandle: true);
    var service = new FileSystemRecognitionService(new ProviderRegistry([provider]));
    return await service.AnalyzeAsync(imagePath);
}

static FileSystemDetectionInfo ExpectSingle(
    FileSystemRecognitionInfo info,
    FileSystemKind kind,
    string message)
{
    var matches = info.Detections.Where(x => x.Kind == kind).ToArray();
    Expect(matches.Length == 1, message);
    return matches[0];
}

static byte[] BuildFatBoot(
    uint totalSectors,
    byte sectorsPerCluster,
    ushort reserved,
    byte fats,
    ushort rootEntries,
    ushort fat16Sectors,
    uint fat32Sectors,
    string label,
    uint serial)
{
    var boot = new byte[512];
    boot[0] = 0xEB;
    boot[1] = 0x3C;
    boot[2] = 0x90;
    WriteAscii(boot, 3, "MSDOS5.0");
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11, 2), 512);
    boot[13] = sectorsPerCluster;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(14, 2), reserved);
    boot[16] = fats;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(17, 2), rootEntries);
    if (totalSectors <= ushort.MaxValue)
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(19, 2), (ushort)totalSectors);
    else
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(32, 4), totalSectors);
    boot[21] = 0xF8;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(22, 2), fat16Sectors);
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(24, 2), 63);
    BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(26, 2), 255);

    if (fat16Sectors == 0)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(36, 4), fat32Sectors);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(44, 4), 2);
        boot[64] = 0x80;
        boot[66] = 0x29;
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(67, 4), serial);
        WriteAsciiPadded(boot, 71, 11, label);
        WriteAscii(boot, 82, "FAT32   ");
    }
    else
    {
        boot[36] = 0x80;
        boot[38] = 0x29;
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(39, 4), serial);
        WriteAsciiPadded(boot, 43, 11, label);
        WriteAscii(boot, 54, "FAT16   ");
    }

    boot[510] = 0x55;
    boot[511] = 0xAA;
    return boot;
}

static async Task<string> CreateExtImageAsync(
    string root,
    string name,
    uint compat,
    uint incompat,
    string label)
{
    const uint blocks = 8192;
    const int blockSize = 1024;
    var super = new byte[1024];
    BinaryPrimitives.WriteUInt32LittleEndian(super.AsSpan(0, 4), 1024);
    BinaryPrimitives.WriteUInt32LittleEndian(super.AsSpan(4, 4), blocks);
    BinaryPrimitives.WriteUInt32LittleEndian(super.AsSpan(24, 4), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(super.AsSpan(32, 4), blocks);
    BinaryPrimitives.WriteUInt32LittleEndian(super.AsSpan(40, 4), 1024);
    BinaryPrimitives.WriteUInt16LittleEndian(super.AsSpan(56, 2), 0xEF53);
    BinaryPrimitives.WriteUInt32LittleEndian(super.AsSpan(76, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(super.AsSpan(92, 4), compat);
    BinaryPrimitives.WriteUInt32LittleEndian(super.AsSpan(96, 4), incompat);
    for (var i = 0; i < 16; i++)
        super[104 + i] = (byte)(i + 1);
    WriteUtf8Padded(super, 120, 16, label);

    return await CreateImageAsync(root, name, blocks * (long)blockSize, (1024, super));
}

static byte[] BuildVrsDescriptor(string identifier)
{
    var descriptor = new byte[2048];
    descriptor[0] = 0;
    WriteAscii(descriptor, 1, identifier);
    descriptor[6] = 1;
    return descriptor;
}

static async Task<string> CreateImageAsync(
    string root,
    string name,
    long length,
    params (long Offset, byte[] Data)[] writes)
{
    var path = Path.Combine(root, name);
    await using var stream = new FileStream(
        path,
        FileMode.Create,
        FileAccess.ReadWrite,
        FileShare.None,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.RandomAccess);
    stream.SetLength(length);

    foreach (var write in writes)
    {
        if (write.Offset < 0 || write.Offset > length || write.Data.Length > length - write.Offset)
            throw new InvalidOperationException("Test fixture write exceeds sparse image bounds.");
        stream.Position = write.Offset;
        await stream.WriteAsync(write.Data);
    }

    await stream.FlushAsync();
    return path;
}

static void WriteAscii(byte[] buffer, int offset, string value)
{
    var bytes = Encoding.ASCII.GetBytes(value);
    bytes.CopyTo(buffer.AsSpan(offset, bytes.Length));
}

static void WriteAsciiPadded(byte[] buffer, int offset, int length, string value)
{
    buffer.AsSpan(offset, length).Fill((byte)' ');
    var bytes = Encoding.ASCII.GetBytes(value);
    bytes.AsSpan(0, Math.Min(bytes.Length, length)).CopyTo(buffer.AsSpan(offset, length));
}

static void WriteUtf8Padded(byte[] buffer, int offset, int length, string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    bytes.AsSpan(0, Math.Min(bytes.Length, length)).CopyTo(buffer.AsSpan(offset, length));
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task ExpectThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
        throw new InvalidOperationException(message);
    }
    catch (TException)
    {
    }
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

class FakeProvider : IDiskImageProvider
{
    private readonly bool _canHandle;

    public FakeProvider(string id, IReadOnlyCollection<string> extensions, bool canHandle)
    {
        Id = id;
        Extensions = extensions;
        _canHandle = canHandle;
    }

    public string Id { get; }
    public IReadOnlyCollection<string> Extensions { get; }

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_canHandle);
    }

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName,
            file.Name,
            "FAKE",
            file.Length,
            "fake physical image",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true));
    }
}

sealed class FakePartitionProvider : FakeProvider, IPartitionTableProvider
{
    private readonly PartitionTableInfo _table;

    public FakePartitionProvider(
        string id,
        IReadOnlyCollection<string> extensions,
        PartitionTableInfo table)
        : base(id, extensions, canHandle: true)
    {
        _table = table;
    }

    public string DisplayName => "Fake partition filesystem provider";

    public ValueTask<PartitionTableInfo> ReadPartitionTableAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_table);
    }
}
