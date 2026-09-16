using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

const int BlockSize = 2048;
const int BlockCount = 700;
const uint SequenceLocation = 260;
const uint PartitionStart = 300;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-UdfTraversal-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await VerifyBoundedRootTraversalAsync(root);
    await VerifyCorruptFileIdentifierAsync(root);
    await VerifyOutOfRangeRootIcbAsync(root);
    await VerifyUnsupportedPartitionMapIsNotFollowedAsync(root);
    await VerifyCancellationAsync(root);
    Console.WriteLine("Dragon DiskForge UDF traversal smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task VerifyBoundedRootTraversalAsync(string root)
{
    var path = Path.Combine(root, "valid-udf.img");
    var bytes = BuildFixture();
    await File.WriteAllBytesAsync(path, bytes);

    var result = await AnalyzeAsync(path);
    Expect(result.Traversed, "validated physical Type 1 UDF mapping is traversed.");
    Expect(result.RootEntryCount == 1 && result.RootEntryNames.Single() == "hello.txt",
        "bounded root traversal decodes the generated file identifier.");
    Expect(result.Identity.Any(x => x.Kind == "udf-file-set-id" && x.Value == "DRAGON_SET"),
        "validated File Set Identifier becomes identity evidence.");
    Expect(result.HealthFindings.Count == 0,
        "healthy bounded Type 1 fixture produces no UDF traversal findings.");
}

static async Task VerifyCorruptFileIdentifierAsync(string root)
{
    var path = Path.Combine(root, "corrupt-fid.img");
    var bytes = BuildFixture();
    bytes[(PartitionStart + 3) * BlockSize + 4] ^= 0x01;
    await File.WriteAllBytesAsync(path, bytes);

    var result = await AnalyzeAsync(path);
    Expect(result.Traversed, "root extent was reached before the corrupted FID was rejected.");
    Expect(result.HealthFindings.Any(x => x.Code == "UDF_FILE_IDENTIFIER_INVALID"),
        "corrupt root-directory FID tag checksum is reported.");
}

static async Task VerifyOutOfRangeRootIcbAsync(string root)
{
    var path = Path.Combine(root, "root-oob.img");
    var bytes = BuildFixture();
    var fsd = bytes.AsSpan((int)(PartitionStart + 1) * BlockSize, BlockSize);
    WriteLongAd(fsd.Slice(400, 16), BlockSize, 999, 0);
    FinalizeDescriptorTag(fsd, 256, 1, 400);
    await File.WriteAllBytesAsync(path, bytes);

    var result = await AnalyzeAsync(path);
    Expect(!result.Traversed, "out-of-range root ICB is never traversed.");
    Expect(result.HealthFindings.Any(x => x.Code == "UDF_ROOT_DIRECTORY_ICB_OUT_OF_RANGE"),
        "root ICB outside the physical Type 1 partition is reported.");
}

static async Task VerifyUnsupportedPartitionMapIsNotFollowedAsync(string root)
{
    var path = Path.Combine(root, "type2-map.img");
    var bytes = BuildFixture();
    var lvd = bytes.AsSpan(((int)SequenceLocation + 1) * BlockSize, BlockSize);
    lvd[440] = 2;
    FinalizeDescriptorTag(lvd, 6, SequenceLocation + 1, 430);
    await File.WriteAllBytesAsync(path, bytes);

    var result = await AnalyzeAsync(path);
    Expect(!result.Traversed, "Type 2 UDF partition maps are not guessed into physical mappings.");
    Expect(result.RootEntryCount == 0,
        "unsupported virtual/sparable/metadata-style map does not expose root entries.");
}

static async Task VerifyCancellationAsync(string root)
{
    var path = Path.Combine(root, "cancel.img");
    await File.WriteAllBytesAsync(path, BuildFixture());
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try
    {
        await AnalyzeAsync(path, cts.Token);
        throw new InvalidOperationException("cancellation must stop UDF traversal.");
    }
    catch (OperationCanceledException)
    {
    }
}

static byte[] BuildFixture()
{
    var bytes = new byte[BlockCount * BlockSize];

    var anchor = bytes.AsSpan(256 * BlockSize, BlockSize);
    BinaryPrimitives.WriteUInt32LittleEndian(anchor.Slice(16, 4), 3 * BlockSize);
    BinaryPrimitives.WriteUInt32LittleEndian(anchor.Slice(20, 4), SequenceLocation);
    FinalizeDescriptorTag(anchor, 2, 256, 16);

    var partition = bytes.AsSpan((int)SequenceLocation * BlockSize, BlockSize);
    BinaryPrimitives.WriteUInt16LittleEndian(partition.Slice(22, 2), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(partition.Slice(188, 4), PartitionStart);
    BinaryPrimitives.WriteUInt32LittleEndian(partition.Slice(192, 4), 300);
    FinalizeDescriptorTag(partition, 5, SequenceLocation, 180);

    var logical = bytes.AsSpan(((int)SequenceLocation + 1) * BlockSize, BlockSize);
    BinaryPrimitives.WriteUInt32LittleEndian(logical.Slice(212, 4), BlockSize);
    Encoding.ASCII.GetBytes("*OSTA UDF Compliant").CopyTo(logical.Slice(217, 23));
    WriteLongAd(logical.Slice(248, 16), BlockSize, 1, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(logical.Slice(264, 4), 6);
    BinaryPrimitives.WriteUInt32LittleEndian(logical.Slice(268, 4), 1);
    logical[440] = 1;
    logical[441] = 6;
    BinaryPrimitives.WriteUInt16LittleEndian(logical.Slice(442, 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(logical.Slice(444, 2), 0);
    FinalizeDescriptorTag(logical, 6, SequenceLocation + 1, 430);

    var terminator = bytes.AsSpan(((int)SequenceLocation + 2) * BlockSize, BlockSize);
    FinalizeDescriptorTag(terminator, 8, SequenceLocation + 2, 0);

    var fsd = bytes.AsSpan((int)(PartitionStart + 1) * BlockSize, BlockSize);
    WriteDString(fsd.Slice(304, 32), "DRAGON_SET");
    WriteLongAd(fsd.Slice(400, 16), BlockSize, 2, 0);
    FinalizeDescriptorTag(fsd, 256, 1, 400);

    var rootEntry = bytes.AsSpan((int)(PartitionStart + 2) * BlockSize, BlockSize);
    BinaryPrimitives.WriteUInt16LittleEndian(rootEntry.Slice(20, 2), 4);
    rootEntry[27] = 4;
    BinaryPrimitives.WriteUInt16LittleEndian(rootEntry.Slice(34, 2), 0);
    BinaryPrimitives.WriteUInt64LittleEndian(rootEntry.Slice(56, 8), 48);
    BinaryPrimitives.WriteUInt32LittleEndian(rootEntry.Slice(168, 4), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(rootEntry.Slice(172, 4), 8);
    BinaryPrimitives.WriteUInt32LittleEndian(rootEntry.Slice(176, 4), BlockSize);
    BinaryPrimitives.WriteUInt32LittleEndian(rootEntry.Slice(180, 4), 3);
    FinalizeDescriptorTag(rootEntry, 261, 2, 168);

    var fid = bytes.AsSpan((int)(PartitionStart + 3) * BlockSize, 48);
    BinaryPrimitives.WriteUInt16LittleEndian(fid.Slice(16, 2), 1);
    fid[18] = 0;
    var encodedName = Encoding.Latin1.GetBytes("hello.txt");
    fid[19] = checked((byte)(encodedName.Length + 1));
    WriteLongAd(fid.Slice(20, 16), BlockSize, 4, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(fid.Slice(36, 2), 0);
    fid[38] = 8;
    encodedName.CopyTo(fid.Slice(39, encodedName.Length));
    FinalizeDescriptorTag(fid, 257, 3, 32);

    return bytes;
}

static Task<UdfTraversalInfo> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
{
    var file = new FileInfo(path);
    var recognition = new FileSystemRecognitionInfo(
        "generated-physical",
        "Generated Physical Fixture",
        file.Length,
        [new FileSystemDetectionInfo(
            null,
            0,
            file.Length,
            FileSystemKind.Udf,
            "UDF",
            string.Empty,
            string.Empty,
            BlockSize,
            null,
            "generated UDF fixture")]);

    return new UdfTraversalService().AnalyzeAsync(path, recognition, cancellationToken);
}

static void WriteLongAd(Span<byte> field, uint length, uint logicalBlock, ushort partitionReference)
{
    field.Clear();
    BinaryPrimitives.WriteUInt32LittleEndian(field.Slice(0, 4), length);
    BinaryPrimitives.WriteUInt32LittleEndian(field.Slice(4, 4), logicalBlock);
    BinaryPrimitives.WriteUInt16LittleEndian(field.Slice(8, 2), partitionReference);
}

static void WriteDString(Span<byte> field, string value)
{
    field.Clear();
    var encoded = Encoding.Latin1.GetBytes(value);
    field[0] = 8;
    encoded.CopyTo(field[1..]);
    field[^1] = checked((byte)(encoded.Length + 1));
}

static void FinalizeDescriptorTag(Span<byte> descriptor, ushort tagId, uint location, ushort crcLength)
{
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.Slice(0, 2), tagId);
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.Slice(2, 2), 2);
    descriptor[4] = 0;
    descriptor[5] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.Slice(6, 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.Slice(10, 2), crcLength);
    BinaryPrimitives.WriteUInt32LittleEndian(descriptor.Slice(12, 4), location);
    BinaryPrimitives.WriteUInt16LittleEndian(
        descriptor.Slice(8, 2),
        crcLength == 0 ? (ushort)0 : ComputeCrc16(descriptor.Slice(16, crcLength)));

    byte checksum = 0;
    for (var index = 0; index < 16; index++)
    {
        if (index != 4)
            checksum = unchecked((byte)(checksum + descriptor[index]));
    }
    descriptor[4] = checksum;
}

static ushort ComputeCrc16(ReadOnlySpan<byte> data)
{
    ushort crc = 0;
    foreach (var value in data)
    {
        crc ^= (ushort)(value << 8);
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 0x8000) != 0
                ? (ushort)((crc << 1) ^ 0x1021)
                : (ushort)(crc << 1);
        }
    }
    return crc;
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
