using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Providers;

const int SectorSize = 512;
var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-VMDK-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new VmdkSparseImageProvider();

    var valid = Path.Combine(root, "valid-sparse.vmdk");
    CreateSparseVmdk(valid);

    Expect(await provider.CanHandleAsync(valid), "Valid hosted sparse VMDK v1 should be recognized.");
    var metadata = await provider.ReadVirtualDiskMetadataAsync(valid);
    Expect(metadata.ContainerType == "VMware hosted sparse v1", "VMDK container type should identify the proven sparse slice.");
    Expect(metadata.SectorSize == SectorSize, "VMDK sector size should be 512 bytes.");
    Expect(metadata.CapacitySectors == 1024 && metadata.CapacityBytes == 1024UL * SectorSize,
        "VMDK virtual capacity should come from the sparse header.");
    Expect(metadata.GrainSizeSectors == 128 && metadata.GrainSizeBytes == 128UL * SectorSize,
        "VMDK grain size should come from the sparse header.");
    Expect(metadata.DescriptorOffsetSectors == 1 && metadata.DescriptorSizeSectors == 2,
        "VMDK embedded descriptor location should be preserved.");
    Expect(metadata.GrainTableEntries == 512, "VMDK grain-table entry count should be preserved.");
    Expect(metadata.RedundantGrainDirectoryOffsetSectors == 4 && metadata.GrainDirectoryOffsetSectors == 6,
        "VMDK grain-directory offsets should be preserved.");
    Expect(metadata.OverheadSectors == 8, "VMDK metadata overhead should be preserved.");
    Expect(metadata.CreateType == "monolithicSparse", "Embedded descriptor createType should be parsed.");
    Expect(metadata.DescriptorVersion == "1", "Embedded descriptor version should be parsed.");
    Expect(metadata.Cid == "fffffffe" && metadata.ParentCid == "ffffffff", "Embedded descriptor CIDs should be parsed.");
    Expect(metadata.DescriptorExtentCount == 1, "Embedded descriptor extent declaration should be counted.");

    var inspect = await provider.InspectAsync(valid);
    Expect(inspect.Format == "VMDK sparse v1", "Inspect should identify the VMDK sparse v1 slice.");
    Expect(inspect.DetectionMethod.Contains("monolithicSparse", StringComparison.Ordinal),
        "Inspect should expose the descriptor createType when present.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify,
        "VMDK provider must remain inspection-only in this slice.");

    var noDescriptor = Path.Combine(root, "no-descriptor.vmdk");
    CreateSparseVmdk(noDescriptor, includeDescriptor: false);
    Expect(await provider.CanHandleAsync(noDescriptor), "A valid sparse extent without an embedded descriptor should still expose bounded header metadata.");
    var noDescriptorMetadata = await provider.ReadVirtualDiskMetadataAsync(noDescriptor);
    Expect(!noDescriptorMetadata.HasEmbeddedDescriptor && noDescriptorMetadata.CreateType is null,
        "No-descriptor VMDK must not invent descriptor metadata.");

    var badMagic = Path.Combine(root, "bad-magic.vmdk");
    File.Copy(valid, badMagic);
    PatchUInt32(badMagic, 0, 0x12345678);
    Expect(!await provider.CanHandleAsync(badMagic), "Bad VMDK sparse magic must be rejected.");

    var badVersion = Path.Combine(root, "bad-version.vmdk");
    File.Copy(valid, badVersion);
    PatchUInt32(badVersion, 4, 2);
    Expect(!await provider.CanHandleAsync(badVersion), "Unproven VMDK sparse header versions must be rejected.");

    var badGrain = Path.Combine(root, "bad-grain.vmdk");
    File.Copy(valid, badGrain);
    PatchUInt64(badGrain, 20, 7);
    Expect(!await provider.CanHandleAsync(badGrain), "VMDK grain sizes that are too small or not powers of two must be rejected.");

    var misalignedCapacity = Path.Combine(root, "bad-capacity.vmdk");
    File.Copy(valid, misalignedCapacity);
    PatchUInt64(misalignedCapacity, 12, 1000);
    Expect(!await provider.CanHandleAsync(misalignedCapacity), "VMDK capacity not aligned to grain size must be rejected in this slice.");

    var badGtes = Path.Combine(root, "bad-gtes.vmdk");
    File.Copy(valid, badGtes);
    PatchUInt32(badGtes, 44, 0);
    Expect(!await provider.CanHandleAsync(badGtes), "VMDK zero grain-table entry count must be rejected.");

    var descriptorOutOfBounds = Path.Combine(root, "descriptor-oob.vmdk");
    File.Copy(valid, descriptorOutOfBounds);
    PatchUInt64(descriptorOutOfBounds, 28, 1000);
    Expect(!await provider.CanHandleAsync(descriptorOutOfBounds), "VMDK embedded descriptor outside the physical file must be rejected.");

    var descriptorTooLarge = Path.Combine(root, "descriptor-too-large.vmdk");
    File.Copy(valid, descriptorTooLarge);
    PatchUInt64(descriptorTooLarge, 36, 4096);
    Expect(!await provider.CanHandleAsync(descriptorTooLarge), "VMDK embedded descriptor above the bounded read limit must be rejected.");

    var gdOutOfBounds = Path.Combine(root, "gd-oob.vmdk");
    File.Copy(valid, gdOutOfBounds);
    PatchUInt64(gdOutOfBounds, 56, 1000);
    Expect(!await provider.CanHandleAsync(gdOutOfBounds), "VMDK grain-directory offset outside the physical file must be rejected.");

    var badNewline = Path.Combine(root, "bad-newline.vmdk");
    File.Copy(valid, badNewline);
    PatchByte(badNewline, 73, (byte)'X');
    Expect(!await provider.CanHandleAsync(badNewline), "VMDK valid-newline flag with contradictory newline bytes must be rejected.");

    var badCompression = Path.Combine(root, "bad-compression.vmdk");
    File.Copy(valid, badCompression);
    PatchUInt16(badCompression, 77, 1);
    Expect(!await provider.CanHandleAsync(badCompression), "VMDK compression algorithm without compressed flag must be rejected.");

    var badDescriptorVersion = Path.Combine(root, "bad-descriptor-version.vmdk");
    CreateSparseVmdk(badDescriptorVersion, descriptorVersion: "2");
    Expect(!await provider.CanHandleAsync(badDescriptorVersion), "Embedded VMDK descriptor version other than 1 must be rejected.");

    var missingCreateType = Path.Combine(root, "missing-create-type.vmdk");
    CreateSparseVmdk(missingCreateType, includeCreateType: false);
    Expect(!await provider.CanHandleAsync(missingCreateType), "Embedded VMDK descriptor without createType must be rejected.");

    var textDescriptor = Path.Combine(root, "text-descriptor.vmdk");
    await File.WriteAllTextAsync(textDescriptor, "# Disk DescriptorFile\nversion=1\ncreateType=\"twoGbMaxExtentSparse\"\n");
    Expect(!await provider.CanHandleAsync(textDescriptor), "Text-only VMDK descriptors are outside this first sparse-header slice and must not be claimed.");

    var foreign = Path.Combine(root, "foreign.img");
    File.Copy(valid, foreign);
    Expect(!await provider.CanHandleAsync(foreign), "VMDK provider must not claim foreign extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadVirtualDiskMetadataAsync(valid, cts.Token).AsTask(),
        "Pre-cancelled VMDK parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(new CcdImageProvider(), Priority: 95),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
        new ProviderRegistration(new CueSheetImageProvider(), Priority: 70),
        new ProviderRegistration(new MdsImageProvider(), Priority: 60),
        new ProviderRegistration(new NrgImageProvider(), Priority: 50),
        new ProviderRegistration(provider, Priority: 40)
    ]);

    var resolution = await registry.ResolveAsync(valid);
    Expect(resolution.Provider is VmdkSparseImageProvider, "Provider registry should resolve valid sparse VMDK through the VMDK provider.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.VirtualDiskMetadata) == true,
        "VMDK provider should advertise VirtualDiskMetadata capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false,
        "VMDK provider must not advertise DirectBrowse before virtual-disk data translation/filesystem support exists.");

    Console.WriteLine("Dragon DiskForge VMDK sparse metadata smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateSparseVmdk(
    string path,
    bool includeDescriptor = true,
    string descriptorVersion = "1",
    bool includeCreateType = true)
{
    const int sectorSize = 512;
    const int physicalSectors = 16;
    var bytes = new byte[physicalSectors * sectorSize];
    var header = bytes.AsSpan(0, sectorSize);

    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(0, 4), 0x564D444B);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), 0x00000003);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(12, 8), 1024);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(20, 8), 128);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(28, 8), includeDescriptor ? 1UL : 0UL);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(36, 8), includeDescriptor ? 2UL : 0UL);
    BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(44, 4), 512);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(48, 8), 4);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(56, 8), 6);
    BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(64, 8), 8);
    header[72] = 0;
    header[73] = (byte)'\n';
    header[74] = (byte)' ';
    header[75] = (byte)'\r';
    header[76] = (byte)'\n';
    BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(77, 2), 0);

    if (includeDescriptor)
    {
        var descriptor = new StringBuilder()
            .AppendLine("# Disk DescriptorFile")
            .Append("version=").AppendLine(descriptorVersion)
            .AppendLine("CID=fffffffe")
            .AppendLine("parentCID=ffffffff");
        if (includeCreateType)
            descriptor.AppendLine("createType=\"monolithicSparse\"");
        descriptor.AppendLine("RW 1024 SPARSE \"valid-sparse.vmdk\"");

        var encoded = Encoding.UTF8.GetBytes(descriptor.ToString());
        if (encoded.Length >= 2 * sectorSize)
            throw new InvalidOperationException("Synthetic VMDK descriptor exceeded its fixture allocation.");
        encoded.CopyTo(bytes, sectorSize);
    }

    File.WriteAllBytes(path, bytes);
}

static void PatchByte(string path, long offset, byte value)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.WriteByte(value);
}

static void PatchUInt16(string path, long offset, ushort value)
{
    Span<byte> raw = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(raw, value);
    Patch(path, offset, raw);
}

static void PatchUInt32(string path, long offset, uint value)
{
    Span<byte> raw = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(raw, value);
    Patch(path, offset, raw);
}

static void PatchUInt64(string path, long offset, ulong value)
{
    Span<byte> raw = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(raw, value);
    Patch(path, offset, raw);
}

static void Patch(string path, long offset, ReadOnlySpan<byte> raw)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.Write(raw);
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
