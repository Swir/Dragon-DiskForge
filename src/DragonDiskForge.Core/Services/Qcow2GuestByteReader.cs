using System.Buffers.Binary;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Minimal, read-only QCOW2 guest-byte reader for standard uncompressed clusters.
/// It deliberately refuses backing files, encryption, dirty images, external data files,
/// compression-type extensions, extended L2 entries and compressed cluster descriptors.
/// </summary>
public sealed class Qcow2GuestByteReader : IGuestByteReader
{
    private const ulong CopiedBit = 1UL << 63;
    private const ulong CompressedBit = 1UL << 62;
    private const ulong ZeroBit = 1UL;
    private const ulong HostOffsetMask = 0x00FFFFFFFFFFFE00UL; // bits 9..55
    private const ulong L1AllowedMask = CopiedBit | HostOffsetMask;
    private const ulong L2StandardAllowedMask = CopiedBit | HostOffsetMask | ZeroBit;

    private readonly FileStream _stream;
    private readonly QcowMetadataInfo _metadata;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private Qcow2GuestByteReader(FileStream stream, QcowMetadataInfo metadata)
    {
        _stream = stream;
        _metadata = metadata;
    }

    public ulong Length => _metadata.VirtualSizeBytes;

    public static async ValueTask<Qcow2GuestByteReader> OpenAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new QcowImageProvider();
        var metadata = await provider.ReadQcowMetadataAsync(imagePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!metadata.IsQcow2)
            throw new NotSupportedException("Guest-byte translation is currently implemented only for QCOW2 v2/v3 images.");
        if (metadata.HasBackingFile)
            throw new NotSupportedException("QCOW2 backing-file chains are not supported by the first guest-byte reader slice.");
        if (metadata.IsEncrypted)
            throw new NotSupportedException("Encrypted QCOW2 guest data is not supported.");

        if (metadata.Version == 3)
        {
            const ulong dirty = 1UL << 0;
            const ulong externalData = 1UL << 2;
            const ulong compressionType = 1UL << 3;
            const ulong extendedL2 = 1UL << 4;

            if ((metadata.IncompatibleFeatures & dirty) != 0)
                throw new InvalidDataException("QCOW2 image is marked dirty; guest-byte translation refuses potentially inconsistent active metadata.");
            if ((metadata.IncompatibleFeatures & externalData) != 0)
                throw new NotSupportedException("QCOW2 external data files are not supported by this guest-byte reader.");
            if ((metadata.IncompatibleFeatures & compressionType) != 0)
                throw new NotSupportedException("QCOW2 non-default compression metadata is outside this guest-byte reader slice.");
            if ((metadata.IncompatibleFeatures & extendedL2) != 0)
                throw new NotSupportedException("QCOW2 extended L2 entries are outside this guest-byte reader slice.");
        }

        var fullPath = Path.GetFullPath(imagePath);
        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        return new Qcow2GuestByteReader(stream, metadata);
    }

    public async ValueTask ReadExactlyAsync(
        ulong guestOffset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateGuestRange(guestOffset, destination.Length);
        if (destination.Length == 0)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var clusterSize = _metadata.ClusterSizeBytes;
            var l2Entries = clusterSize / sizeof(ulong);
            if (l2Entries == 0)
                throw new InvalidDataException("QCOW2 cluster geometry cannot contain an L2 table entry.");

            var currentGuestOffset = guestOffset;
            var destinationOffset = 0;

            while (destinationOffset < destination.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var guestCluster = currentGuestOffset / clusterSize;
                var withinCluster = currentGuestOffset % clusterSize;
                var chunk = checked((int)Math.Min(
                    (ulong)(destination.Length - destinationOffset),
                    clusterSize - withinCluster));

                var l1Index = guestCluster / l2Entries;
                var l2Index = guestCluster % l2Entries;
                if (l1Index >= _metadata.L1Size)
                    throw new InvalidDataException("QCOW2 guest offset requires an L1 entry outside the declared active L1 table.");

                var l1EntryOffset = CheckedAdd(
                    _metadata.L1TableOffset,
                    CheckedMultiply(l1Index, sizeof(ulong), "QCOW2 L1 entry offset"),
                    "QCOW2 L1 entry offset");
                var l1Entry = await ReadU64AtAsync(l1EntryOffset, cancellationToken);
                ValidateL1Entry(l1Entry, clusterSize);

                var l2TableOffset = l1Entry & HostOffsetMask;
                if (l2TableOffset == 0)
                {
                    destination.Slice(destinationOffset, chunk).Span.Clear();
                }
                else
                {
                    EnsurePhysicalRange(l2TableOffset, clusterSize, "QCOW2 L2 table");
                    var l2EntryOffset = CheckedAdd(
                        l2TableOffset,
                        CheckedMultiply(l2Index, sizeof(ulong), "QCOW2 L2 entry offset"),
                        "QCOW2 L2 entry offset");
                    var l2Entry = await ReadU64AtAsync(l2EntryOffset, cancellationToken);

                    await ReadMappedChunkAsync(
                        l2Entry,
                        withinCluster,
                        destination.Slice(destinationOffset, chunk),
                        clusterSize,
                        cancellationToken);
                }

                currentGuestOffset = CheckedAdd(currentGuestOffset, (ulong)chunk, "QCOW2 guest read cursor");
                destinationOffset += chunk;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _stream.DisposeAsync();
        _gate.Dispose();
    }

    private async ValueTask ReadMappedChunkAsync(
        ulong l2Entry,
        ulong withinCluster,
        Memory<byte> destination,
        ulong clusterSize,
        CancellationToken cancellationToken)
    {
        if ((l2Entry & CompressedBit) != 0)
            throw new NotSupportedException("QCOW2 compressed clusters are not supported by this guest-byte reader slice.");
        if ((l2Entry & ~L2StandardAllowedMask) != 0)
            throw new InvalidDataException("QCOW2 standard L2 entry contains reserved bits.");

        var zero = (l2Entry & ZeroBit) != 0;
        if (_metadata.Version == 2 && zero)
            throw new InvalidDataException("QCOW2 v2 standard L2 entries cannot use the zero-cluster flag.");

        var hostClusterOffset = l2Entry & HostOffsetMask;
        var copied = (l2Entry & CopiedBit) != 0;

        if (zero)
        {
            destination.Span.Clear();
            return;
        }

        if (hostClusterOffset == 0)
        {
            if (copied)
                throw new InvalidDataException("QCOW2 standard L2 entry uses a zero host offset with COPIED set outside external-data mode.");

            // No backing file is allowed by OpenAsync, so an unallocated cluster reads as zeroes.
            destination.Span.Clear();
            return;
        }

        if (hostClusterOffset % clusterSize != 0)
            throw new InvalidDataException("QCOW2 standard data cluster offset is not cluster-aligned.");

        EnsurePhysicalRange(hostClusterOffset, clusterSize, "QCOW2 data cluster");
        var physicalOffset = CheckedAdd(hostClusterOffset, withinCluster, "QCOW2 mapped guest byte offset");
        await ReadExactlyAtAsync(physicalOffset, destination, cancellationToken);
    }

    private void ValidateL1Entry(ulong entry, ulong clusterSize)
    {
        if ((entry & ~L1AllowedMask) != 0)
            throw new InvalidDataException("QCOW2 L1 entry contains reserved bits.");

        var offset = entry & HostOffsetMask;
        var copied = (entry & CopiedBit) != 0;
        if (offset == 0)
        {
            if (copied)
                throw new InvalidDataException("QCOW2 unallocated L1 entry cannot set COPIED in this reader slice.");
            return;
        }

        if (offset % clusterSize != 0)
            throw new InvalidDataException("QCOW2 L2 table offset from the active L1 table is not cluster-aligned.");
        EnsurePhysicalRange(offset, clusterSize, "QCOW2 L2 table");
    }

    private async ValueTask<ulong> ReadU64AtAsync(ulong fileOffset, CancellationToken cancellationToken)
    {
        var buffer = new byte[sizeof(ulong)];
        await ReadExactlyAtAsync(fileOffset, buffer, cancellationToken);
        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private async ValueTask ReadExactlyAtAsync(
        ulong fileOffset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        EnsurePhysicalRange(fileOffset, (ulong)destination.Length, "QCOW2 physical read");
        _stream.Position = checked((long)fileOffset);
        await _stream.ReadExactlyAsync(destination, cancellationToken);
    }

    private void ValidateGuestRange(ulong guestOffset, int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (guestOffset > Length || (ulong)length > Length - guestOffset)
            throw new ArgumentOutOfRangeException(nameof(guestOffset), "Requested QCOW2 guest range extends beyond the virtual disk.");
    }

    private void EnsurePhysicalRange(ulong offset, ulong length, string description)
    {
        var fileLength = checked((ulong)_stream.Length);
        if (offset > fileLength || length > fileLength - offset)
            throw new InvalidDataException($"{description} extends outside the physical QCOW2 file.");
    }

    private static ulong CheckedAdd(ulong left, ulong right, string description)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"{description} overflows 64-bit addressing.", ex);
        }
    }

    private static ulong CheckedMultiply(ulong left, ulong right, string description)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"{description} overflows 64-bit addressing.", ex);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
