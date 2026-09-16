using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Services;

const int SectorSize = 512;
const int GrainSectors = 128;
const int GrainBytes = GrainSectors * SectorSize;
const int RedundantDirectorySector = 4;
const int PrimaryDirectorySector = 6;
const int RedundantTableSector = 8;
const int PrimaryTableSector = 12;
const int FirstDataSector = 16;
const int SecondDataSector = FirstDataSector + GrainSectors;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-VMDK-Guest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var valid = Path.Combine(root, "valid-monolithic-sparse.vmdk");
    CreateReadableSparseVmdk(valid);

    await using (var reader = await VmdkSparseGuestByteReader.OpenAsync(valid))
    {
        Expect(reader.Length == 1024UL * SectorSize, "VMDK guest length should match the sparse header capacity.");

        var first = new byte[64];
        await reader.ReadExactlyAsync(0, first);
        Expect(first.All(value => value == 0xA1), "Allocated VMDK grain should expose its guest-visible bytes.");

        var cross = new byte[32];
        await reader.ReadExactlyAsync((ulong)GrainBytes - 16, cross);
        Expect(cross.Take(16).All(value => value == 0xA1), "Cross-grain read should preserve the tail of the first grain.");
        Expect(cross.Skip(16).All(value => value == 0xB2), "Cross-grain read should continue into the next allocated grain.");

        var sparse = new byte[64];
        await reader.ReadExactlyAsync(2UL * GrainBytes, sparse);
        Expect(sparse.All(value => value == 0), "Unallocated VMDK grain should read as zeroes when no parent chain exists.");

        await ExpectThrowsAsync<ArgumentOutOfRangeException>(
            () => reader.ReadExactlyAsync(reader.Length - 4, new byte[8]).AsTask(),
            "Guest reads crossing the declared VMDK capacity must be rejected.");

        using var cancelledRead = new CancellationTokenSource();
        cancelledRead.Cancel();
        await ExpectCanceledAsync(
            () => reader.ReadExactlyAsync(0, new byte[1], cancelledRead.Token).AsTask(),
            "Pre-cancelled VMDK guest reads should propagate cancellation.");
    }

    var redundantSelected = Path.Combine(root, "redundant-selected.vmdk");
    CreateReadableSparseVmdk(redundantSelected);
    PatchUInt32(redundantSelected, PrimaryDirectorySector * SectorSize, 4000);
    await using (var reader = await VmdkSparseGuestByteReader.OpenAsync(redundantSelected))
    {
        var data = new byte[16];
        await reader.ReadExactlyAsync(0, data);
        Expect(data.All(value => value == 0xA1),
            "Use-redundant-directory flag must select the redundant VMDK grain-directory path.");
    }

    var primarySelected = Path.Combine(root, "primary-selected.vmdk");
    CreateReadableSparseVmdk(primarySelected, flags: 0x00000001);
    PatchUInt32(primarySelected, RedundantDirectorySector * SectorSize, 4000);
    await using (var reader = await VmdkSparseGuestByteReader.OpenAsync(primarySelected))
    {
        var data = new byte[16];
        await reader.ReadExactlyAsync(0, data);
        Expect(data.All(value => value == 0xA1),
            "Without the redundant-directory flag, the primary VMDK grain directory must be used.");
    }

    var directoryOob = Path.Combine(root, "directory-oob.vmdk");
    CreateReadableSparseVmdk(directoryOob);
    PatchUInt32(directoryOob, RedundantDirectorySector * SectorSize, 4000);
    await using (var reader = await VmdkSparseGuestByteReader.OpenAsync(directoryOob))
    {
        await ExpectThrowsAsync<InvalidDataException>(
            () => reader.ReadExactlyAsync(0, new byte[1]).AsTask(),
            "A VMDK grain-directory pointer outside the physical file must be rejected.");
    }

    var directoryInsideData = Path.Combine(root, "directory-inside-data.vmdk");
    CreateReadableSparseVmdk(directoryInsideData);
    PatchUInt64(directoryInsideData, 48, FirstDataSector);
    await ExpectThrowsAsync<InvalidDataException>(
        () => VmdkSparseGuestByteReader.OpenAsync(directoryInsideData).AsTask(),
        "The active VMDK grain directory must stay inside the declared metadata overhead region.");

    var grainTableInsideData = Path.Combine(root, "grain-table-inside-data.vmdk");
    CreateReadableSparseVmdk(grainTableInsideData);
    PatchUInt32(grainTableInsideData, RedundantDirectorySector * SectorSize, FirstDataSector);
    await using (var reader = await VmdkSparseGuestByteReader.OpenAsync(grainTableInsideData))
    {
        await ExpectThrowsAsync<InvalidDataException>(
            () => reader.ReadExactlyAsync(0, new byte[1]).AsTask(),
            "VMDK grain tables must stay inside the declared metadata overhead region.");
    }

    var grainOob = Path.Combine(root, "grain-oob.vmdk");
    CreateReadableSparseVmdk(grainOob);
    PatchUInt32(grainOob, RedundantTableSector * SectorSize, 4000);
    await using (var reader = await VmdkSparseGuestByteReader.OpenAsync(grainOob))
    {
        await ExpectThrowsAsync<InvalidDataException>(
            () => reader.ReadExactlyAsync(0, new byte[1]).AsTask(),
            "A VMDK grain pointer outside the physical file must be rejected.");
    }

    var grainInsideMetadata = Path.Combine(root, "grain-inside-metadata.vmdk");
    CreateReadableSparseVmdk(grainInsideMetadata);
    PatchUInt32(grainInsideMetadata, RedundantTableSector * SectorSize, 12);
    await using (var reader = await VmdkSparseGuestByteReader.OpenAsync(grainInsideMetadata))
    {
        await ExpectThrowsAsync<InvalidDataException>(
            () => reader.ReadExactlyAsync(0, new byte[1]).AsTask(),
            "VMDK guest data must not be mapped into the declared metadata overhead region.");
    }

    var parented = Path.Combine(root, "parented.vmdk");
    CreateReadableSparseVmdk(parented, parentCid: "12345678");
    await ExpectThrowsAsync<NotSupportedException>(
        () => VmdkSparseGuestByteReader.OpenAsync(parented).AsTask(),
        "VMDK parent/backing chains must fail closed in the first guest-byte reader slice.");

    var splitDescriptor = Path.Combine(root, "split-descriptor.vmdk");
    CreateReadableSparseVmdk(splitDescriptor, createType: "twoGbMaxExtentSparse");
    await ExpectThrowsAsync<NotSupportedException>(
        () => VmdkSparseGuestByteReader.OpenAsync(splitDescriptor).AsTask(),
        "Split sparse VMDK create types must not be claimed by the monolithic guest reader.");

    var unclean = Path.Combine(root, "unclean.vmdk");
    CreateReadableSparseVmdk(unclean, uncleanShutdown: true);
    await ExpectThrowsAsync<InvalidDataException>(
        () => VmdkSparseGuestByteReader.OpenAsync(unclean).AsTask(),
        "Unclean VMDK images must be refused before guest-byte translation.");

    var compressed = Path.Combine(root, "compressed.vmdk");
    CreateReadableSparseVmdk(compressed, flags: 0x00010003, compressionAlgorithm: 1);
    await ExpectThrowsAsync<NotSupportedException>(
        () => VmdkSparseGuestByteReader.OpenAsync(compressed).AsTask(),
        "Compressed VMDK grains must fail closed until a dedicated decoder is implemented.");

    var zeroedEntryFlag = Path.Combine(root, "zeroed-entry-flag.vmdk");
    CreateReadableSparseVmdk(zeroedEntryFlag, flags: 0x00000007);
    await ExpectThrowsAsync<NotSupportedException>(
        () => VmdkSparseGuestByteReader.OpenAsync(zeroedEntryFlag).AsTask(),
        "Zeroed-grain entry overloading must fail closed in the VMDK v1 guest-byte reader slice.");

    var noDescriptor = Path.Combine(root, "no-descriptor.vmdk");
    CreateReadableSparseVmdk(noDescriptor, includeDescriptor: false);
    await ExpectThrowsAsync<NotSupportedException>(
        () => VmdkSparseGuestByteReader.OpenAsync(noDescriptor).AsTask(),
        "VMDK guest translation must require descriptor evidence for parent and extent semantics.");

    using var cancelledOpen = new CancellationTokenSource();
    cancelledOpen.Cancel();
    await ExpectCanceledAsync(
        () => VmdkSparseGuestByteReader.OpenAsync(valid, cancelledOpen.Token).AsTask(),
        "Pre-cancelled VMDK guest-reader open should propagate cancellation.");

    Console.WriteLine("Dragon DiskForge VMDK sparse guest-byte reader smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateReadableSparseVmdk(
    string path,
    uint flags = 0x00000003,
    string parentCid = "ffffffff",
    string createType = "monolithicSparse",
    bool uncleanShutdown = false,
    ushort compressionAlgorithm = 0,
    bool includeDescriptor = true)
{
    const int physicalSectors = SecondDataSector + GrainSectors;
    var bytes = new byte[physicalSectors * SectorSize];
    var header = bytes.AsSpan(0, SectorSize);

    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(0, 4), 0x564D444B);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), flags);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(12, 8), 1024);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(20, 8), GrainSectors);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(28, 8), includeDescriptor ? 1UL : 0UL);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(36, 8), includeDescriptor ? 2UL : 0UL);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(44, 4), 512);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(48, 8), RedundantDirectorySector);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(56, 8), PrimaryDirectorySector);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(64, 8), FirstDataSector);
    header[72] = uncleanShutdown ? (byte)1 : (byte)0;
    header[73] = (byte)'\n';
    header[74] = (byte)' ';
    header[75] = (byte)'\r';
    header[76] = (byte)'\n';
    BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(77, 2), compressionAlgorithm);

    if (includeDescriptor)
    {
        var descriptor = new StringBuilder()
            .AppendLine("# Disk DescriptorFile")
            .AppendLine("version=1")
            .AppendLine("CID=fffffffe")
            .Append("parentCID=").AppendLine(parentCid)
            .Append("createType=\"").Append(createType).AppendLine("\"")
            .AppendLine("RW 1024 SPARSE \"valid-monolithic-sparse.vmdk\"");
        var encoded = Encoding.UTF8.GetBytes(descriptor.ToString());
        if (encoded.Length >= 2 * SectorSize)
            throw new InvalidOperationException("Synthetic VMDK descriptor exceeded its fixture allocation.");
        encoded.CopyTo(bytes, SectorSize);
    }

    WriteUInt32(bytes, RedundantDirectorySector * SectorSize, RedundantTableSector);
    WriteUInt32(bytes, PrimaryDirectorySector * SectorSize, PrimaryTableSector);

    WriteUInt32(bytes, RedundantTableSector * SectorSize, FirstDataSector);
    WriteUInt32(bytes, RedundantTableSector * SectorSize + sizeof(uint), SecondDataSector);
    WriteUInt32(bytes, PrimaryTableSector * SectorSize, FirstDataSector);
    WriteUInt32(bytes, PrimaryTableSector * SectorSize + sizeof(uint), SecondDataSector);

    bytes.AsSpan(FirstDataSector * SectorSize, GrainBytes).Fill(0xA1);
    bytes.AsSpan(SecondDataSector * SectorSize, GrainBytes).Fill(0xB2);

    File.WriteAllBytes(path, bytes);
}

static void WriteUInt32(byte[] bytes, int offset, uint value)
    => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);

static void PatchUInt32(string path, long offset, uint value)
{
    Span<byte> raw = stackalloc byte[sizeof(uint)];
    BinaryPrimitives.WriteUInt32LittleEndian(raw, value);
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.Write(raw);
}

static void PatchUInt64(string path, long offset, ulong value)
{
    Span<byte> raw = stackalloc byte[sizeof(ulong)];
    BinaryPrimitives.WriteUInt64LittleEndian(raw, value);
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.Write(raw);
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

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
