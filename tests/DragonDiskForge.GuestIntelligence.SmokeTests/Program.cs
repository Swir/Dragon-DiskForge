using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

const int ClusterBits = 12;
const int ClusterSize = 1 << ClusterBits;
const ulong CopiedBit = 1UL << 63;
const int SectorSize = 512;
const int GrainSectors = 128;
const int RedundantDirectorySector = 4;
const int PrimaryDirectorySector = 6;
const int RedundantTableSector = 8;
const int PrimaryTableSector = 12;
const int FirstDataSector = 16;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-GuestIntel-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var registry = new ProviderRegistry(new IDiskImageProvider[]
    {
        new QcowImageProvider(),
        new VmdkSparseImageProvider()
    });
    var service = new GuestImageIntelligenceService(registry);

    var qcow = Path.Combine(root, "guest-intel.qcow2");
    CreateQcow2WithFat12(qcow, partitionSectors: 31);
    var qcowAnalysis = await service.AnalyzeAsync(qcow);
    VerifyGuestFat12(qcowAnalysis, expectedGuestSize: 4L * ClusterSize, expectedPartitionSectors: 31, "QCOW2");
    Require(qcowAnalysis.ReaderKind.Contains("QCOW2", StringComparison.Ordinal),
        "QCOW2 analysis should identify the reader kind truthfully.");

    var report = await new ImageReportService(registry).AnalyzeAsync(qcow);
    Require(report.GuestAnalysis is not null, "Image reports should include proven guest-byte analysis for QCOW2.");
    var json = ImageReportService.ToJson(report);
    using (var document = JsonDocument.Parse(json))
    {
        Require(document.RootElement.GetProperty("GuestAnalysis").ValueKind == JsonValueKind.Object,
            "JSON reports should expose guest analysis as a structured object.");
    }
    var text = ImageReportService.ToText(report);
    Require(text.Contains("GUEST ADDRESS SPACE", StringComparison.Ordinal),
        "Text reports should label guest-address-space evidence explicitly.");
    Require(text.Contains("GUEST PARTITIONS", StringComparison.Ordinal) && text.Contains("FAT12", StringComparison.Ordinal),
        "Text reports should expose bounded guest partition/filesystem evidence.");

    var vmdk = Path.Combine(root, "guest-intel.vmdk");
    CreateVmdkWithFat12(vmdk);
    var vmdkAnalysis = await service.AnalyzeAsync(vmdk);
    VerifyGuestFat12(vmdkAnalysis, expectedGuestSize: 1024L * SectorSize, expectedPartitionSectors: 1023, "VMDK");
    Require(vmdkAnalysis.ReaderKind.Contains("VMDK", StringComparison.Ordinal),
        "VMDK analysis should identify the reader kind truthfully.");

    var invalidRange = Path.Combine(root, "invalid-range.qcow2");
    CreateQcow2WithFat12(invalidRange, partitionSectors: 1000);
    await ExpectThrowsAsync<InvalidDataException>(
        () => service.AnalyzeAsync(invalidRange),
        "Guest partition layouts that extend beyond the virtual address space must fail closed.");

    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        await ExpectCanceledAsync(
            () => service.AnalyzeAsync(qcow, cts.Token),
            "Pre-cancelled guest intelligence should propagate cancellation.");
    }

    Console.WriteLine("Dragon DiskForge guest partition/filesystem intelligence smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void VerifyGuestFat12(
    GuestImageIntelligenceInfo analysis,
    long expectedGuestSize,
    ulong expectedPartitionSectors,
    string container)
{
    Require(analysis.GuestSizeBytes == expectedGuestSize,
        $"{container} guest analysis should preserve the reader virtual size.");
    Require(analysis.PartitionLayout is { Scheme: PartitionTableScheme.Mbr },
        $"{container} guest analysis should discover the synthetic MBR.");
    var partition = analysis.PartitionLayout!.Partitions.Single();
    Require(partition.FirstLba == 1 && partition.SectorCount == expectedPartitionSectors,
        $"{container} guest partition geometry should come from guest-visible MBR bytes.");
    Require(partition.OffsetBytes == SectorSize,
        $"{container} guest partition offset should be expressed in guest bytes.");

    var fileSystem = analysis.FileSystems.Detections.Single();
    Require(fileSystem.Kind == FileSystemKind.Fat12 && fileSystem.PartitionIndex == 1,
        $"{container} guest filesystem recognition should identify FAT12 inside partition 1.");
    Require(fileSystem.GuestOffsetBytes == SectorSize,
        $"{container} filesystem offset must remain explicitly guest-relative.");
    Require(fileSystem.Label == "GUESTVOL",
        $"{container} guest filesystem recognition should preserve the FAT label.");
}

static void CreateQcow2WithFat12(string path, uint partitionSectors)
{
    const int fileClusters = 6;
    var bytes = new byte[fileClusters * ClusterSize];

    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x514649FB);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), 3);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), ClusterBits);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24, 8), 4UL * ClusterSize);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(32, 4), 0);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(36, 4), 1);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(40, 8), ClusterSize);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(48, 8), 2UL * ClusterSize);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(56, 4), 1);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(60, 4), 0);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(64, 8), 0);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(72, 8), 0);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(80, 8), 0);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(88, 8), 0);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(96, 4), 4);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(100, 4), 104);

    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(ClusterSize, 8), CopiedBit | 3UL * ClusterSize);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(3 * ClusterSize, 8), CopiedBit | 4UL * ClusterSize);

    var guestFirstCluster = bytes.AsSpan(4 * ClusterSize, ClusterSize);
    WriteMbrAndFat12(guestFirstCluster, partitionSectors);
    File.WriteAllBytes(path, bytes);
}

static void CreateVmdkWithFat12(string path)
{
    const int physicalSectors = FirstDataSector + GrainSectors;
    var bytes = new byte[physicalSectors * SectorSize];
    var header = bytes.AsSpan(0, SectorSize);

    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(0, 4), 0x564D444B);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), 0x00000003);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(12, 8), 1024);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(20, 8), GrainSectors);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(28, 8), 1);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(36, 8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(44, 4), 512);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(48, 8), RedundantDirectorySector);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(56, 8), PrimaryDirectorySector);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(64, 8), FirstDataSector);
    header[72] = 0;
    header[73] = (byte)'\n';
    header[74] = (byte)' ';
    header[75] = (byte)'\r';
    header[76] = (byte)'\n';
    BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(77, 2), 0);

    var descriptor = new StringBuilder()
        .AppendLine("# Disk DescriptorFile")
        .AppendLine("version=1")
        .AppendLine("CID=fffffffe")
        .AppendLine("parentCID=ffffffff")
        .AppendLine("createType=\"monolithicSparse\"")
        .Append("RW 1024 SPARSE \"").Append(Path.GetFileName(path)).AppendLine("\"");
    var encoded = Encoding.UTF8.GetBytes(descriptor.ToString());
    encoded.CopyTo(bytes, SectorSize);

    WriteUInt32Little(bytes, RedundantDirectorySector * SectorSize, RedundantTableSector);
    WriteUInt32Little(bytes, PrimaryDirectorySector * SectorSize, PrimaryTableSector);
    WriteUInt32Little(bytes, RedundantTableSector * SectorSize, FirstDataSector);
    WriteUInt32Little(bytes, PrimaryTableSector * SectorSize, FirstDataSector);

    var firstGrain = bytes.AsSpan(FirstDataSector * SectorSize, GrainSectors * SectorSize);
    WriteMbrAndFat12(firstGrain, partitionSectors: 1023);
    File.WriteAllBytes(path, bytes);
}

static void WriteMbrAndFat12(Span<byte> guest, uint partitionSectors)
{
    if (guest.Length < 4096)
        throw new InvalidOperationException("Synthetic guest fixture requires at least 4096 bytes.");

    const int entry = 446;
    guest[entry] = 0x80;
    guest[entry + 4] = 0x01;
    BinaryPrimitives.WriteUInt32LittleEndian(guest.Slice(entry + 8, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(guest.Slice(entry + 12, 4), partitionSectors);
    guest[510] = 0x55;
    guest[511] = 0xAA;

    var boot = guest.Slice(SectorSize, SectorSize);
    boot[0] = 0xEB;
    boot[1] = 0x3C;
    boot[2] = 0x90;
    "DDFTEST "u8.CopyTo(boot.Slice(3, 8));
    BinaryPrimitives.WriteUInt16LittleEndian(boot.Slice(11, 2), SectorSize);
    boot[13] = 1;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.Slice(14, 2), 1);
    boot[16] = 1;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.Slice(17, 2), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(boot.Slice(19, 2), checked((ushort)Math.Min(partitionSectors, ushort.MaxValue)));
    boot[21] = 0xF8;
    BinaryPrimitives.WriteUInt16LittleEndian(boot.Slice(22, 2), 1);
    boot[38] = 0x29;
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(39, 4), 0x1234ABCD);
    "GUESTVOL   "u8.CopyTo(boot.Slice(43, 11));
    boot[510] = 0x55;
    boot[511] = 0xAA;
}

static void WriteUInt32Little(byte[] bytes, int offset, uint value)
    => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

static async Task ExpectThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static async Task ExpectCanceledAsync(Func<Task> action, string message)
{
    try
    {
        await action();
    }
    catch (OperationCanceledException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
