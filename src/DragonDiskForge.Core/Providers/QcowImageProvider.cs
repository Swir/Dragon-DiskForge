using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class QcowImageProvider : IQcowMetadataProvider
{
    private const uint Magic = 0x514649FB; // QFI\xFB, stored big-endian.
    private const int Qcow1HeaderSize = 48;
    private const int Qcow2V2HeaderSize = 72;
    private const int Qcow2V3BaseHeaderSize = 104;
    private const int MaxBackingFileBytes = 1023;
    private const uint MaxSnapshots = 65_536;
    private const ulong KnownIncompatibleFeatures = 0x1FUL; // dirty, corrupt, external-data, compression-type, extended-L2.
    private const ulong KnownCompatibleFeatures = 0x1UL;   // lazy refcounts.
    private static readonly string[] ExtensionsList = [".qcow", ".qcow2"];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string Id => "qcow";
    public string DisplayName => "QCOW / QCOW2 metadata";
    public IReadOnlyCollection<string> Extensions => ExtensionsList;

    public async ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".qcow", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".qcow2", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            _ = await ReadQcowMetadataAsync(path, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or OverflowException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return false;
        }
    }

    public async ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var metadata = await ReadQcowMetadataAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var backing = metadata.HasBackingFile
            ? $"backing metadata={metadata.BackingFileName ?? "present"}"
            : "no backing file";
        var encryption = metadata.IsEncrypted ? $"encryption method={metadata.EncryptionMethod}" : "unencrypted";

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            metadata.Format,
            file.Length,
            $"{metadata.Format} metadata (virtual {metadata.VirtualSizeBytes} bytes; cluster {metadata.ClusterSizeBytes} bytes; {backing}; {encryption})",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<QcowMetadataInfo> ReadQcowMetadataAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("QCOW image was not found.", fullPath);

        var extension = file.Extension;
        if (!extension.Equals(".qcow", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".qcow2", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The QCOW provider accepts only .qcow and .qcow2 files.");
        }

        if (file.Length < Qcow1HeaderSize)
            throw new InvalidDataException("QCOW image is too small to contain a supported header.");

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var prefixLength = (int)Math.Min(file.Length, 112L);
        var header = new byte[prefixLength];
        await stream.ReadExactlyAsync(header, cancellationToken);

        var magic = ReadU32(header, 0, "QCOW magic");
        if (magic != Magic)
            throw new InvalidDataException("QCOW magic does not match QFI\\xFB.");

        var version = ReadU32(header, 4, "QCOW version");
        return version switch
        {
            1 => await ReadQcow1Async(stream, file, header, cancellationToken),
            2 or 3 => await ReadQcow2Async(stream, file, header, version, cancellationToken),
            _ => throw new InvalidDataException($"QCOW version {version} is not supported by this provider slice.")
        };
    }

    private static async ValueTask<QcowMetadataInfo> ReadQcow1Async(
        FileStream stream,
        FileInfo file,
        byte[] header,
        CancellationToken cancellationToken)
    {
        EnsureHeader(header, Qcow1HeaderSize, "QCOW v1");

        var backingOffset = ReadU64(header, 8, "QCOW v1 backing-file offset");
        var backingSize = ReadU32(header, 16, "QCOW v1 backing-file size");
        var mtime = ReadU32(header, 20, "QCOW v1 modification time");
        var virtualSize = ReadU64(header, 24, "QCOW v1 virtual size");
        var clusterBits = header[32];
        var l2Bits = header[33];
        var padding = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(34, 2));
        var cryptMethod = ReadU32(header, 36, "QCOW v1 encryption method");
        var l1Offset = ReadU64(header, 40, "QCOW v1 L1 table offset");

        if (virtualSize < 2)
            throw new InvalidDataException("QCOW v1 virtual size is too small.");
        if (clusterBits is < 9 or > 16)
            throw new InvalidDataException("QCOW v1 cluster_bits must be between 9 and 16.");
        if (l2Bits is < 6 or > 13)
            throw new InvalidDataException("QCOW v1 l2_bits must be between 6 and 13.");
        if (padding != 0)
            throw new InvalidDataException("QCOW v1 reserved padding must be zero.");
        if (cryptMethod > 1)
            throw new InvalidDataException("QCOW v1 encryption method is not supported by this metadata slice.");

        var l1Shift = checked(clusterBits + l2Bits);
        var l1Size = CeilDivByPowerOfTwo(virtualSize, l1Shift);
        if (l1Size == 0 || l1Size > uint.MaxValue)
            throw new InvalidDataException("QCOW v1 L1 entry count is outside supported bounds.");

        if ((l1Offset & 7UL) != 0)
            throw new InvalidDataException("QCOW v1 L1 table offset must be 8-byte aligned.");
        var l1Bytes = CheckedMultiply(l1Size, 8UL, "QCOW v1 L1 table size");
        EnsureRange(file.Length, l1Offset, l1Bytes, "QCOW v1 L1 table");

        var backingName = await ReadBackingNameAsync(
            stream,
            file.Length,
            backingOffset,
            backingSize,
            minimumOffset: Qcow1HeaderSize,
            maximumEnd: (ulong)file.Length,
            cancellationToken);

        return new QcowMetadataInfo(
            file.FullName,
            "QCOW v1",
            1,
            virtualSize,
            clusterBits,
            backingOffset,
            backingSize,
            backingName,
            cryptMethod,
            l2Bits,
            l1Size,
            l1Offset,
            RefcountTableOffset: null,
            RefcountTableClusters: null,
            SnapshotCount: null,
            SnapshotsOffset: null,
            IncompatibleFeatures: 0,
            CompatibleFeatures: 0,
            AutoclearFeatures: 0,
            RefcountOrder: null,
            HeaderLength: Qcow1HeaderSize,
            CompressionType: null,
            ModificationTime: mtime);
    }

    private static async ValueTask<QcowMetadataInfo> ReadQcow2Async(
        FileStream stream,
        FileInfo file,
        byte[] header,
        uint version,
        CancellationToken cancellationToken)
    {
        EnsureHeader(header, Qcow2V2HeaderSize, "QCOW2");

        var backingOffset = ReadU64(header, 8, "QCOW2 backing-file offset");
        var backingSize = ReadU32(header, 16, "QCOW2 backing-file size");
        var clusterBits = checked((int)ReadU32(header, 20, "QCOW2 cluster_bits"));
        var virtualSize = ReadU64(header, 24, "QCOW2 virtual size");
        var cryptMethod = ReadU32(header, 32, "QCOW2 encryption method");
        var l1Size = ReadU32(header, 36, "QCOW2 L1 size");
        var l1Offset = ReadU64(header, 40, "QCOW2 L1 table offset");
        var refcountOffset = ReadU64(header, 48, "QCOW2 refcount table offset");
        var refcountClusters = ReadU32(header, 56, "QCOW2 refcount table clusters");
        var snapshotCount = ReadU32(header, 60, "QCOW2 snapshot count");
        var snapshotsOffset = ReadU64(header, 64, "QCOW2 snapshots offset");

        if (clusterBits is < 9 or > 21)
            throw new InvalidDataException("QCOW2 cluster_bits must be between 9 and 21 in this provider slice.");
        if (virtualSize == 0)
            throw new InvalidDataException("QCOW2 virtual size must be non-zero.");
        if (cryptMethod > 2)
            throw new InvalidDataException("QCOW2 encryption method is outside the defined metadata range.");
        if (l1Size == 0)
            throw new InvalidDataException("QCOW2 L1 table must contain at least one entry.");
        if (refcountClusters == 0)
            throw new InvalidDataException("QCOW2 refcount table must occupy at least one cluster.");
        if (snapshotCount > MaxSnapshots)
            throw new InvalidDataException("QCOW2 snapshot count exceeds the supported safety bound.");

        var clusterSize = 1UL << clusterBits;
        ValidateClusterAlignedRange(file.Length, l1Offset, CheckedMultiply(l1Size, 8UL, "QCOW2 L1 table size"), clusterSize, "QCOW2 L1 table");
        ValidateClusterAlignedRange(file.Length, refcountOffset, CheckedMultiply(refcountClusters, clusterSize, "QCOW2 refcount table size"), clusterSize, "QCOW2 refcount table");

        if (snapshotCount == 0)
        {
            if (snapshotsOffset != 0)
                throw new InvalidDataException("QCOW2 snapshots offset must be zero when snapshot count is zero in this provider slice.");
        }
        else
        {
            if (snapshotsOffset == 0 || snapshotsOffset % clusterSize != 0)
                throw new InvalidDataException("QCOW2 snapshot table offset must be non-zero and cluster-aligned when snapshots exist.");
            EnsureRange(file.Length, snapshotsOffset, 1, "QCOW2 snapshot table start");
        }

        ulong incompatible = 0;
        ulong compatible = 0;
        ulong autoclear = 0;
        uint refcountOrder = 4;
        uint headerLength = Qcow2V2HeaderSize;
        byte? compressionType = null;

        if (version == 3)
        {
            EnsureHeader(header, Qcow2V3BaseHeaderSize, "QCOW2 v3");
            incompatible = ReadU64(header, 72, "QCOW2 incompatible features");
            compatible = ReadU64(header, 80, "QCOW2 compatible features");
            autoclear = ReadU64(header, 88, "QCOW2 autoclear features");
            refcountOrder = ReadU32(header, 96, "QCOW2 refcount order");
            headerLength = ReadU32(header, 100, "QCOW2 header length");

            if ((incompatible & ~KnownIncompatibleFeatures) != 0)
                throw new InvalidDataException("QCOW2 contains unknown incompatible feature bits.");
            if ((compatible & ~KnownCompatibleFeatures) != 0)
                throw new InvalidDataException("QCOW2 contains unknown compatible feature bits in this provider slice.");
            if (autoclear != 0)
                throw new InvalidDataException("QCOW2 autoclear feature state is outside this first metadata slice.");
            if ((incompatible & (1UL << 1)) != 0)
                throw new InvalidDataException("QCOW2 image is marked corrupt.");
            if ((incompatible & (1UL << 2)) != 0)
                throw new InvalidDataException("QCOW2 external-data files are not supported by this metadata slice.");
            if ((incompatible & (1UL << 4)) != 0 && clusterBits < 14)
                throw new InvalidDataException("QCOW2 extended-L2 metadata requires cluster_bits >= 14.");
            if (refcountOrder > 6)
                throw new InvalidDataException("QCOW2 refcount_order exceeds the supported specification bound.");
            if (headerLength < Qcow2V3BaseHeaderSize || (headerLength & 7) != 0)
                throw new InvalidDataException("QCOW2 v3 header length must be at least 104 bytes and 8-byte aligned.");
            if (headerLength > clusterSize || headerLength > (ulong)file.Length)
                throw new InvalidDataException("QCOW2 v3 header length exceeds the first cluster or physical file.");

            if (headerLength >= 112)
            {
                EnsureHeader(header, 105, "QCOW2 compression type");
                compressionType = header[104];
            }

            var compressionFeature = (incompatible & (1UL << 3)) != 0;
            if (compressionFeature)
            {
                if (headerLength < 112 || compressionType != 1)
                    throw new InvalidDataException("QCOW2 non-default compression feature requires a present Zstandard compression-type field.");
            }
            else if (compressionType is not null && compressionType != 0)
            {
                throw new InvalidDataException("QCOW2 compression type is non-default while the compression feature bit is clear.");
            }
        }

        var minimumBackingOffset = version == 2 ? (ulong)Qcow2V2HeaderSize : headerLength;
        var backingName = await ReadBackingNameAsync(
            stream,
            file.Length,
            backingOffset,
            backingSize,
            minimumBackingOffset,
            clusterSize,
            cancellationToken);

        return new QcowMetadataInfo(
            file.FullName,
            version == 2 ? "QCOW2 v2" : "QCOW2 v3",
            version,
            virtualSize,
            clusterBits,
            backingOffset,
            backingSize,
            backingName,
            cryptMethod,
            L2Bits: null,
            l1Size,
            l1Offset,
            refcountOffset,
            refcountClusters,
            snapshotCount,
            snapshotsOffset,
            incompatible,
            compatible,
            autoclear,
            refcountOrder,
            headerLength,
            compressionType,
            ModificationTime: null);
    }

    private static async ValueTask<string?> ReadBackingNameAsync(
        FileStream stream,
        long fileLength,
        ulong offset,
        uint size,
        ulong minimumOffset,
        ulong maximumEnd,
        CancellationToken cancellationToken)
    {
        if ((offset == 0) != (size == 0))
            throw new InvalidDataException("QCOW backing-file offset and size must either both be zero or both be present.");
        if (offset == 0)
            return null;
        if (size == 0 || size > MaxBackingFileBytes)
            throw new InvalidDataException("QCOW backing-file name length exceeds the specification safety bound.");
        if (offset < minimumOffset)
            throw new InvalidDataException("QCOW backing-file name overlaps the fixed header.");

        var end = CheckedAdd(offset, size, "QCOW backing-file name range");
        if (end > maximumEnd)
            throw new InvalidDataException("QCOW backing-file name extends outside the permitted header/cluster region.");
        EnsureRange(fileLength, offset, size, "QCOW backing-file name");

        var buffer = new byte[checked((int)size)];
        stream.Position = checked((long)offset);
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        if (buffer.AsSpan().IndexOf((byte)0) >= 0)
            throw new InvalidDataException("QCOW backing-file name contains an embedded NUL byte.");

        var name = StrictUtf8.GetString(buffer);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("QCOW backing-file name is empty or whitespace.");
        return name;
    }

    private static void ValidateClusterAlignedRange(
        long fileLength,
        ulong offset,
        ulong length,
        ulong clusterSize,
        string label)
    {
        if (offset == 0 || offset % clusterSize != 0)
            throw new InvalidDataException($"{label} offset must be non-zero and cluster-aligned.");
        EnsureRange(fileLength, offset, length, label);
    }

    private static void EnsureHeader(byte[] header, int minimumLength, string label)
    {
        if (header.Length < minimumLength)
            throw new InvalidDataException($"{label} header is truncated.");
    }

    private static uint ReadU32(byte[] buffer, int offset, string label)
    {
        if (offset < 0 || offset > buffer.Length - 4)
            throw new InvalidDataException($"{label} is truncated.");
        return BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset, 4));
    }

    private static ulong ReadU64(byte[] buffer, int offset, string label)
    {
        if (offset < 0 || offset > buffer.Length - 8)
            throw new InvalidDataException($"{label} is truncated.");
        return BinaryPrimitives.ReadUInt64BigEndian(buffer.AsSpan(offset, 8));
    }

    private static ulong CeilDivByPowerOfTwo(ulong value, int shift)
    {
        if (shift is < 0 or >= 64)
            throw new InvalidDataException("QCOW shift value is outside supported bounds.");
        var unit = 1UL << shift;
        return checked((value + unit - 1) / unit);
    }

    private static ulong CheckedMultiply(ulong left, ulong right, string label)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"{label} overflows the supported range.", ex);
        }
    }

    private static ulong CheckedAdd(ulong left, ulong right, string label)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"{label} overflows the supported range.", ex);
        }
    }

    private static void EnsureRange(long boundary, ulong offset, ulong count, string label)
    {
        if (boundary < 0 || offset > (ulong)boundary || count > (ulong)boundary - offset)
            throw new InvalidDataException($"{label} lies outside the physical QCOW file.");
    }
}
