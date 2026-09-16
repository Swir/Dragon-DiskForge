using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-RawPipeline-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var service = new RawImagePipelineService();

    var blankProgress = new CaptureProgress();
    var blankPath = Path.Combine(root, "blank.raw");
    const ulong blankSize = 1024UL * 1024 + 17;
    var blankResult = await service.CreateBlankAsync(blankPath, blankSize, progress: blankProgress);
    Require(blankResult.SizeBytes == checked((long)blankSize), "Blank RAW result should report the requested logical size.");
    Require(new FileInfo(blankPath).Length == checked((long)blankSize), "Blank RAW output should have the requested logical size.");
    Require(await ReadByteAtAsync(blankPath, 0) == 0, "New blank RAW output should read as zero at the beginning.");
    Require(await ReadByteAtAsync(blankPath, checked((long)blankSize - 1)) == 0, "New blank RAW output should read as zero at the end.");
    RequireProgress(blankProgress.Values, "Blank RAW progress should be monotonic and committed only at 1.0.");
    RequireNoTemporaryFiles(root, "Blank RAW creation must not leave transaction files.");

    var zeroPath = Path.Combine(root, "zero.raw");
    var zeroResult = await service.CreateBlankAsync(zeroPath, 0);
    Require(zeroResult.SizeBytes == 0 && new FileInfo(zeroPath).Length == 0, "Zero-length RAW creation should be supported deterministically.");

    const ulong syntheticLength = 2UL * 1024 * 1024 + 321;
    await using (var source = new PatternGuestReader(syntheticLength))
    {
        var exportProgress = new CaptureProgress();
        var exportPath = Path.Combine(root, "synthetic.raw");
        var exportResult = await service.ExportGuestToRawAsync(source, exportPath, progress: exportProgress);

        Require(exportResult.SizeBytes == checked((long)syntheticLength), "Guest export should report the exact virtual source length.");
        Require(new FileInfo(exportPath).Length == checked((long)syntheticLength), "Guest export should preserve the exact virtual source length.");
        Require(await ReadByteAtAsync(exportPath, 0) == PatternGuestReader.ValueAt(0), "Guest export should preserve the first byte.");
        Require(await ReadByteAtAsync(exportPath, 1024 * 1024 + 37) == PatternGuestReader.ValueAt(1024UL * 1024 + 37), "Guest export should preserve bytes across the internal buffer boundary.");
        Require(await ReadByteAtAsync(exportPath, checked((long)syntheticLength - 1)) == PatternGuestReader.ValueAt(syntheticLength - 1), "Guest export should preserve the final byte.");
        Require(source.ReadCalls >= 3, "Synthetic export should exercise multiple bounded guest reads.");
        RequireProgress(exportProgress.Values, "Guest export progress should be monotonic and committed only at 1.0.");
    }

    var replacePath = Path.Combine(root, "replace.raw");
    await File.WriteAllTextAsync(replacePath, "original");
    await using (var source = new PatternGuestReader(4097))
    {
        var replaceResult = await service.ExportGuestToRawAsync(
            source,
            replacePath,
            OutputOverwritePolicy.ReplaceExisting);
        Require(replaceResult.ReplacedExisting, "Guest export should report replacement of an existing destination.");
        Require(new FileInfo(replacePath).Length == 4097, "Replacement should publish only the completed guest output.");
    }

    var failIfExistsPath = Path.Combine(root, "fail-if-exists.raw");
    await File.WriteAllTextAsync(failIfExistsPath, "keep");
    await using (var source = new PatternGuestReader(4096))
    {
        await ExpectThrowsAsync<IOException>(
            () => service.ExportGuestToRawAsync(source, failIfExistsPath),
            "FailIfExists should reject the destination before reading guest bytes.");
        Require(source.ReadCalls == 0, "FailIfExists should not consume the source when the destination already exists.");
        Require(await File.ReadAllTextAsync(failIfExistsPath) == "keep", "FailIfExists must preserve the existing destination.");
    }
    RequireNoTemporaryFiles(root, "FailIfExists guest export must not leave transaction files.");

    var cancelledPath = Path.Combine(root, "cancelled.raw");
    using (var cts = new CancellationTokenSource())
    {
        await using var source = new CancelAfterFirstReadGuestReader(2UL * 1024 * 1024, cts);
        await ExpectCanceledAsync(
            () => service.ExportGuestToRawAsync(source, cancelledPath, cancellationToken: cts.Token),
            "Cancellation during a real guest export must abort before final commit.");
    }
    Require(!File.Exists(cancelledPath), "Cancelled guest export must not publish a partial destination.");
    RequireNoTemporaryFiles(root, "Cancelled guest export must clean transaction files when possible.");

    var failingPath = Path.Combine(root, "reader-failure.raw");
    await File.WriteAllTextAsync(failingPath, "preserve-me");
    await using (var source = new FailingGuestReader(2UL * 1024 * 1024 + 1, failAtOrAfterOffset: 1024UL * 1024))
    {
        await ExpectThrowsAsync<InvalidDataException>(
            () => service.ExportGuestToRawAsync(
                source,
                failingPath,
                OutputOverwritePolicy.ReplaceExisting),
            "Guest-reader failure must roll back the output transaction.");
    }
    Require(await File.ReadAllTextAsync(failingPath) == "preserve-me", "Guest-reader failure must preserve the previous destination.");
    RequireNoTemporaryFiles(root, "Guest-reader failure must clean transaction files when possible.");

    await using (var huge = new PatternGuestReader((ulong)long.MaxValue + 1UL))
    {
        await ExpectThrowsAsync<NotSupportedException>(
            () => service.ExportGuestToRawAsync(huge, Path.Combine(root, "too-large.raw")),
            "Guest sizes outside the current FileStream length domain must fail before writing.");
    }

    await ExpectThrowsAsync<NotSupportedException>(
        () => service.CreateBlankAsync(Path.Combine(root, "too-large-blank.raw"), (ulong)long.MaxValue + 1UL),
        "Blank RAW sizes outside the current FileStream length domain must fail before writing.");

    var qcowPath = Path.Combine(root, "source.qcow2");
    CreateQcow2(qcowPath);
    var qcowRawPath = Path.Combine(root, "from-qcow2.raw");
    var qcowResult = await service.ConvertQcow2ToRawAsync(qcowPath, qcowRawPath);
    Require(qcowResult.SizeBytes == 4L * 4096, "QCOW2 conversion should emit the declared guest-visible virtual size.");
    var qcowBytes = await File.ReadAllBytesAsync(qcowRawPath);
    Require(qcowBytes.AsSpan(0, 4096).ToArray().All(value => value == 0xA5), "QCOW2 conversion should materialize allocated cluster bytes.");
    Require(qcowBytes.AsSpan(4096).ToArray().All(value => value == 0), "QCOW2 conversion should materialize proven zero/unallocated clusters as zeroes.");

    await ExpectThrowsAsync<IOException>(
        () => service.ConvertQcow2ToRawAsync(qcowPath, qcowPath, OutputOverwritePolicy.ReplaceExisting),
        "QCOW2 conversion must reject identical source/destination paths before mutation.");

    var vmdkPath = Path.Combine(root, "source.vmdk");
    CreateReadableSparseVmdk(vmdkPath);
    var vmdkRawPath = Path.Combine(root, "from-vmdk.raw");
    var vmdkResult = await service.ConvertVmdkSparseToRawAsync(vmdkPath, vmdkRawPath);
    Require(vmdkResult.SizeBytes == 1024L * 512, "VMDK conversion should emit the declared guest-visible capacity.");
    Require(await ReadByteAtAsync(vmdkRawPath, 0) == 0xA1, "VMDK conversion should materialize the first allocated grain.");
    Require(await ReadByteAtAsync(vmdkRawPath, 128L * 512) == 0xB2, "VMDK conversion should materialize the second allocated grain.");
    Require(await ReadByteAtAsync(vmdkRawPath, 2L * 128 * 512) == 0, "VMDK conversion should materialize unallocated/no-parent grains as zeroes.");
    Require(await ReadByteAtAsync(vmdkRawPath, 1024L * 512 - 1) == 0, "VMDK conversion should preserve zero semantics through the final virtual grain.");

    RequireNoTemporaryFiles(root, "Successful conversion pipelines must not leave transaction files.");
    Console.WriteLine("Dragon DiskForge RAW creation and guest-to-RAW pipeline smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task<byte> ReadByteAtAsync(string path, long offset)
{
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    stream.Position = offset;
    var value = stream.ReadByte();
    if (value < 0)
        throw new EndOfStreamException();
    return checked((byte)value);
}

static void RequireProgress(IReadOnlyList<double> values, string message)
{
    Require(values.Count >= 2, message + " No progress samples were reported.");
    Require(values[0] == 0d, message + " Progress must begin at 0.");
    Require(values[^1] == 1d, message + " Progress must reach 1 only after commit.");

    for (var index = 1; index < values.Count; index++)
    {
        Require(values[index] >= values[index - 1], message + " Progress regressed.");
        Require(values[index] >= 0d && values[index] <= 1d, message + " Progress escaped the 0..1 range.");
    }

    if (values.Count > 1)
        Require(values.Take(values.Count - 1).All(value => value < 1d), message + " Progress reached 1 before commit.");
}

static void RequireNoTemporaryFiles(string directory, string message)
{
    if (Directory.EnumerateFiles(directory, "*.dragon-tmp", SearchOption.AllDirectories).Any())
        throw new InvalidOperationException(message);
}

static void CreateQcow2(string path)
{
    const int clusterBits = 12;
    const int clusterSize = 1 << clusterBits;
    const int fileClusters = 6;
    const ulong copiedBit = 1UL << 63;
    const ulong zeroBit = 1UL;
    var bytes = new byte[fileClusters * clusterSize];

    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x514649FB);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), 3);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), clusterBits);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24, 8), 4UL * clusterSize);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(32, 4), 0);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(36, 4), 1);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(40, 8), (ulong)clusterSize);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(48, 8), 2UL * clusterSize);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(56, 4), 1);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(72, 8), 0);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(80, 8), 0);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(88, 8), 0);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(96, 4), 4);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(100, 4), 104);

    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(clusterSize, 8), copiedBit | 3UL * clusterSize);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(3 * clusterSize, 8), copiedBit | 4UL * clusterSize);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(3 * clusterSize + 8, 8), zeroBit);
    bytes.AsSpan(4 * clusterSize, clusterSize).Fill(0xA5);

    File.WriteAllBytes(path, bytes);
}

static void CreateReadableSparseVmdk(string path)
{
    const int sectorSize = 512;
    const int grainSectors = 128;
    const int grainBytes = grainSectors * sectorSize;
    const int redundantDirectorySector = 4;
    const int primaryDirectorySector = 6;
    const int redundantTableSector = 8;
    const int primaryTableSector = 12;
    const int firstDataSector = 16;
    const int secondDataSector = firstDataSector + grainSectors;
    const int physicalSectors = secondDataSector + grainSectors;

    var bytes = new byte[physicalSectors * sectorSize];
    var header = bytes.AsSpan(0, sectorSize);

    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(0, 4), 0x564D444B);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), 0x00000003);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(12, 8), 1024);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(20, 8), grainSectors);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(28, 8), 1);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(36, 8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(44, 4), 512);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(48, 8), redundantDirectorySector);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(56, 8), primaryDirectorySector);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(64, 8), firstDataSector);
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
    encoded.CopyTo(bytes, sectorSize);

    WriteUInt32(bytes, redundantDirectorySector * sectorSize, redundantTableSector);
    WriteUInt32(bytes, primaryDirectorySector * sectorSize, primaryTableSector);
    WriteUInt32(bytes, redundantTableSector * sectorSize, firstDataSector);
    WriteUInt32(bytes, redundantTableSector * sectorSize + sizeof(uint), secondDataSector);
    WriteUInt32(bytes, primaryTableSector * sectorSize, firstDataSector);
    WriteUInt32(bytes, primaryTableSector * sectorSize + sizeof(uint), secondDataSector);

    bytes.AsSpan(firstDataSector * sectorSize, grainBytes).Fill(0xA1);
    bytes.AsSpan(secondDataSector * sectorSize, grainBytes).Fill(0xB2);
    File.WriteAllBytes(path, bytes);
}

static void WriteUInt32(byte[] bytes, int offset, uint value)
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

sealed class CaptureProgress : IProgress<double>
{
    public List<double> Values { get; } = [];
    public void Report(double value) => Values.Add(value);
}

class PatternGuestReader : IGuestByteReader
{
    public PatternGuestReader(ulong length) => Length = length;

    public ulong Length { get; }
    public int ReadCalls { get; private set; }

    public virtual ValueTask ReadExactlyAsync(
        ulong guestOffset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (guestOffset > Length || (ulong)destination.Length > Length - guestOffset)
            throw new ArgumentOutOfRangeException(nameof(guestOffset));

        for (var index = 0; index < destination.Length; index++)
            destination.Span[index] = ValueAt(guestOffset + (ulong)index);

        ReadCalls++;
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static byte ValueAt(ulong offset) => checked((byte)((offset * 31UL + 17UL) % 251UL));
}

sealed class CancelAfterFirstReadGuestReader : PatternGuestReader
{
    private readonly CancellationTokenSource _cts;

    public CancelAfterFirstReadGuestReader(ulong length, CancellationTokenSource cts)
        : base(length)
    {
        _cts = cts;
    }

    public override async ValueTask ReadExactlyAsync(
        ulong guestOffset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        await base.ReadExactlyAsync(guestOffset, destination, cancellationToken);
        _cts.Cancel();
    }
}

sealed class FailingGuestReader : PatternGuestReader
{
    private readonly ulong _failAtOrAfterOffset;

    public FailingGuestReader(ulong length, ulong failAtOrAfterOffset)
        : base(length)
    {
        _failAtOrAfterOffset = failAtOrAfterOffset;
    }

    public override ValueTask ReadExactlyAsync(
        ulong guestOffset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        if (guestOffset >= _failAtOrAfterOffset)
            throw new InvalidDataException("Synthetic guest-reader failure.");

        return base.ReadExactlyAsync(guestOffset, destination, cancellationToken);
    }
}
