using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-QCOW-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new QcowImageProvider();

    var qcow1 = Path.Combine(root, "valid-v1.qcow");
    CreateQcow1(qcow1, backingName: "base.raw");
    Expect(await provider.CanHandleAsync(qcow1), "Valid QCOW v1 should be recognized.");
    var v1 = await provider.ReadQcowMetadataAsync(qcow1);
    Expect(v1.Format == "QCOW v1" && v1.Version == 1, "QCOW v1 format/version should be preserved.");
    Expect(v1.VirtualSizeBytes == 4UL * 1024 * 1024, "QCOW v1 virtual size should be parsed.");
    Expect(v1.ClusterBits == 12 && v1.L2Bits == 9, "QCOW v1 cluster/L2 geometry should be parsed.");
    Expect(v1.L1Size == 2 && v1.L1TableOffset == 4096, "QCOW v1 L1 metadata should be derived and parsed.");
    Expect(v1.BackingFileName == "base.raw" && v1.HasBackingFile, "QCOW v1 backing-file name should be read without following it.");
    Expect(v1.ModificationTime == 123456, "QCOW v1 modification time should be parsed.");

    var qcow1NoBacking = Path.Combine(root, "no-backing-v1.qcow");
    CreateQcow1(qcow1NoBacking, backingName: null);
    var v1NoBacking = await provider.ReadQcowMetadataAsync(qcow1NoBacking);
    Expect(!v1NoBacking.HasBackingFile && v1NoBacking.BackingFileName is null,
        "QCOW v1 without backing metadata must not invent a backing file.");

    var qcow2v2 = Path.Combine(root, "valid-v2.qcow2");
    CreateQcow2(qcow2v2, version: 2, backingName: "base-v2.raw");
    Expect(await provider.CanHandleAsync(qcow2v2), "Valid QCOW2 v2 should be recognized.");
    var v2 = await provider.ReadQcowMetadataAsync(qcow2v2);
    Expect(v2.Format == "QCOW2 v2" && v2.Version == 2, "QCOW2 v2 format/version should be preserved.");
    Expect(v2.ClusterBits == 12 && v2.ClusterSizeBytes == 4096, "QCOW2 cluster geometry should be parsed.");
    Expect(v2.RefcountOrder == 4 && v2.HeaderLength == 72, "QCOW2 v2 implicit refcount order/header length should be reported.");
    Expect(v2.BackingFileName == "base-v2.raw", "QCOW2 v2 backing filename should be parsed only as metadata.");

    var qcow2v3 = Path.Combine(root, "valid-v3.qcow2");
    CreateQcow2(qcow2v3, version: 3, compatibleFeatures: 1);
    Expect(await provider.CanHandleAsync(qcow2v3), "Valid QCOW2 v3 should be recognized.");
    var v3 = await provider.ReadQcowMetadataAsync(qcow2v3);
    Expect(v3.Format == "QCOW2 v3" && v3.Version == 3, "QCOW2 v3 format/version should be preserved.");
    Expect(v3.CompatibleFeatures == 1 && v3.IncompatibleFeatures == 0 && v3.AutoclearFeatures == 0,
        "QCOW2 v3 feature masks should be preserved.");
    Expect(v3.RefcountOrder == 4 && v3.HeaderLength == 104, "QCOW2 v3 header metadata should be parsed.");

    var zstd = Path.Combine(root, "valid-v3-zstd.qcow2");
    CreateQcow2(zstd, version: 3, incompatibleFeatures: 1UL << 3, headerLength: 112, compressionType: 1);
    var zstdMetadata = await provider.ReadQcowMetadataAsync(zstd);
    Expect(zstdMetadata.CompressionType == 1, "QCOW2 v3 Zstandard metadata should be reported when feature/header agree.");

    var inspect = await provider.InspectAsync(qcow2v3);
    Expect(inspect.Format == "QCOW2 v3", "Inspect should report the parsed QCOW generation.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify,
        "QCOW provider must remain inspection-only in this slice.");

    var badMagic = Copy(qcow2v3, root, "bad-magic.qcow2");
    PatchU32(badMagic, 0, 0x12345678);
    Expect(!await provider.CanHandleAsync(badMagic), "Bad QCOW magic must be rejected.");

    var badVersion = Copy(qcow2v3, root, "bad-version.qcow2");
    PatchU32(badVersion, 4, 4);
    Expect(!await provider.CanHandleAsync(badVersion), "Unknown QCOW versions must be rejected.");

    var badV1Cluster = Copy(qcow1, root, "bad-v1-cluster.qcow");
    PatchByte(badV1Cluster, 32, 8);
    Expect(!await provider.CanHandleAsync(badV1Cluster), "QCOW v1 cluster_bits below 9 must be rejected.");

    var badV1L2 = Copy(qcow1, root, "bad-v1-l2.qcow");
    PatchByte(badV1L2, 33, 5);
    Expect(!await provider.CanHandleAsync(badV1L2), "QCOW v1 l2_bits below 6 must be rejected.");

    var badV1Padding = Copy(qcow1, root, "bad-v1-padding.qcow");
    PatchU16(badV1Padding, 34, 1);
    Expect(!await provider.CanHandleAsync(badV1Padding), "QCOW v1 reserved padding must be zero.");

    var hugeBacking = Copy(qcow1, root, "huge-backing.qcow");
    PatchU32(hugeBacking, 16, 1024);
    Expect(!await provider.CanHandleAsync(hugeBacking), "Backing filename above 1023 bytes must be rejected.");

    var badV1L1 = Copy(qcow1, root, "bad-v1-l1.qcow");
    PatchU64(badV1L1, 40, 0x100000);
    Expect(!await provider.CanHandleAsync(badV1L1), "QCOW v1 L1 table outside the physical file must be rejected.");

    var badV2Backing = Copy(qcow2v2, root, "bad-v2-backing.qcow2");
    PatchU64(badV2Backing, 8, 4088);
    PatchU32(badV2Backing, 16, 16);
    Expect(!await provider.CanHandleAsync(badV2Backing), "QCOW2 backing name must remain inside the first cluster.");

    var badRefcountAlign = Copy(qcow2v3, root, "bad-refcount-align.qcow2");
    PatchU64(badRefcountAlign, 48, 8193);
    Expect(!await provider.CanHandleAsync(badRefcountAlign), "QCOW2 refcount table must be cluster-aligned.");

    var zeroRefcountClusters = Copy(qcow2v3, root, "zero-refcount-clusters.qcow2");
    PatchU32(zeroRefcountClusters, 56, 0);
    Expect(!await provider.CanHandleAsync(zeroRefcountClusters), "QCOW2 zero refcount-table clusters must be rejected.");

    var tooManySnapshots = Copy(qcow2v3, root, "too-many-snapshots.qcow2");
    PatchU32(tooManySnapshots, 60, 65537);
    Expect(!await provider.CanHandleAsync(tooManySnapshots), "QCOW2 excessive snapshot counts must be rejected.");

    var unknownIncompat = Copy(qcow2v3, root, "unknown-incompat.qcow2");
    PatchU64(unknownIncompat, 72, 1UL << 5);
    Expect(!await provider.CanHandleAsync(unknownIncompat), "Unknown QCOW2 incompatible feature bits must be rejected.");

    var corrupt = Copy(qcow2v3, root, "corrupt.qcow2");
    PatchU64(corrupt, 72, 1UL << 1);
    Expect(!await provider.CanHandleAsync(corrupt), "QCOW2 images marked corrupt must be rejected.");

    var externalData = Copy(qcow2v3, root, "external-data.qcow2");
    PatchU64(externalData, 72, 1UL << 2);
    Expect(!await provider.CanHandleAsync(externalData), "QCOW2 external-data mode must remain outside this metadata slice.");

    var badHeaderLength = Copy(qcow2v3, root, "bad-header-length.qcow2");
    PatchU32(badHeaderLength, 100, 105);
    Expect(!await provider.CanHandleAsync(badHeaderLength), "QCOW2 v3 unaligned header length must be rejected.");

    var badRefcountOrder = Copy(qcow2v3, root, "bad-refcount-order.qcow2");
    PatchU32(badRefcountOrder, 96, 7);
    Expect(!await provider.CanHandleAsync(badRefcountOrder), "QCOW2 refcount_order above 6 must be rejected.");

    var unsupportedAutoclear = Copy(qcow2v3, root, "unsupported-autoclear.qcow2");
    PatchU64(unsupportedAutoclear, 88, 1);
    Expect(!await provider.CanHandleAsync(unsupportedAutoclear), "Unparsed QCOW2 autoclear feature state must be rejected.");

    var compressionMismatch = Copy(qcow2v3, root, "compression-mismatch.qcow2");
    PatchU64(compressionMismatch, 72, 1UL << 3);
    Expect(!await provider.CanHandleAsync(compressionMismatch), "QCOW2 compression feature without its extended field must be rejected.");

    var foreign = Path.Combine(root, "foreign.img");
    File.Copy(qcow2v3, foreign);
    Expect(!await provider.CanHandleAsync(foreign), "QCOW provider must not claim foreign extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadQcowMetadataAsync(qcow2v3, cts.Token).AsTask(),
        "Pre-cancelled QCOW parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(new CcdImageProvider(), Priority: 95),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
        new ProviderRegistration(new CueSheetImageProvider(), Priority: 70),
        new ProviderRegistration(new MdsImageProvider(), Priority: 60),
        new ProviderRegistration(new NrgImageProvider(), Priority: 50),
        new ProviderRegistration(new VmdkSparseImageProvider(), Priority: 40),
        new ProviderRegistration(provider, Priority: 30)
    ]);

    var resolution = await registry.ResolveAsync(qcow2v3);
    Expect(resolution.Provider is QcowImageProvider, "Registry should resolve valid QCOW2 through the QCOW provider.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.VirtualDiskMetadata) == true,
        "QCOW provider should advertise VirtualDiskMetadata capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false,
        "QCOW provider must not advertise DirectBrowse before cluster translation/filesystem support exists.");

    Console.WriteLine("Dragon DiskForge QCOW/QCOW2 metadata smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateQcow1(string path, string? backingName)
{
    const int fileLength = 8192;
    var bytes = new byte[fileLength];
    var header = bytes.AsSpan(0, 48);
    BinaryPrimitives.WriteUInt32BigEndian(header.Slice(0, 4), 0x514649FB);
    BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), 1);

    if (backingName is not null)
    {
        var encoded = Encoding.UTF8.GetBytes(backingName);
        BinaryPrimitives.WriteUInt64BigEndian(header.Slice(8, 8), 48);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(16, 4), checked((uint)encoded.Length));
        encoded.CopyTo(bytes, 48);
    }

    BinaryPrimitives.WriteUInt32BigEndian(header.Slice(20, 4), 123456);
    BinaryPrimitives.WriteUInt64BigEndian(header.Slice(24, 8), 4UL * 1024 * 1024);
    header[32] = 12;
    header[33] = 9;
    BinaryPrimitives.WriteUInt16BigEndian(header.Slice(34, 2), 0);
    BinaryPrimitives.WriteUInt32BigEndian(header.Slice(36, 4), 0);
    BinaryPrimitives.WriteUInt64BigEndian(header.Slice(40, 8), 4096);
    File.WriteAllBytes(path, bytes);
}

static void CreateQcow2(
    string path,
    uint version,
    string? backingName = null,
    ulong incompatibleFeatures = 0,
    ulong compatibleFeatures = 0,
    ulong autoclearFeatures = 0,
    uint headerLength = 0,
    byte compressionType = 0)
{
    const int clusterBits = 12;
    const int clusterSize = 1 << clusterBits;
    const int fileLength = clusterSize * 4;
    var bytes = new byte[fileLength];

    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x514649FB);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), version);

    var fixedHeader = version == 2 ? 72U : headerLength == 0 ? 104U : headerLength;
    if (backingName is not null)
    {
        var encoded = Encoding.UTF8.GetBytes(backingName);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), fixedHeader);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), checked((uint)encoded.Length));
        encoded.CopyTo(bytes, checked((int)fixedHeader));
    }

    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), clusterBits);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24, 8), 8UL * 1024 * 1024);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(32, 4), 0);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(36, 4), 4);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(40, 8), clusterSize);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(48, 8), clusterSize * 2UL);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(56, 4), 1);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(60, 4), 0);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(64, 8), 0);

    if (version == 3)
    {
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(72, 8), incompatibleFeatures);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(80, 8), compatibleFeatures);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(88, 8), autoclearFeatures);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(96, 4), 4);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(100, 4), fixedHeader);
        if (fixedHeader >= 112)
            bytes[104] = compressionType;
    }

    File.WriteAllBytes(path, bytes);
}

static string Copy(string source, string root, string name)
{
    var target = Path.Combine(root, name);
    File.Copy(source, target);
    return target;
}

static void PatchByte(string path, long offset, byte value)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.WriteByte(value);
}

static void PatchU16(string path, long offset, ushort value)
{
    Span<byte> raw = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(raw, value);
    Patch(path, offset, raw);
}

static void PatchU32(string path, long offset, uint value)
{
    Span<byte> raw = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(raw, value);
    Patch(path, offset, raw);
}

static void PatchU64(string path, long offset, ulong value)
{
    Span<byte> raw = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(raw, value);
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
