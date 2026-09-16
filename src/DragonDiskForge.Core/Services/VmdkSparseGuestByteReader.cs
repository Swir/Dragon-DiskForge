using System.Buffers.Binary;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Bounded, read-only guest-byte translation for a single hosted sparse VMDK v1 extent.
/// The first slice accepts only clean, uncompressed monolithicSparse images without a parent chain.
/// </summary>
public sealed class VmdkSparseGuestByteReader : IGuestByteReader
{
    private const ulong SectorSize = 512;
    private const uint ValidNewlineFlag = 1u << 0;
    private const uint UseRedundantGrainDirectoryFlag = 1u << 1;
    private const uint ZeroedGrainEntryFlag = 1u << 2;
    private const uint CompressedFlag = 1u << 16;
    private const uint MetadataMarkersFlag = 1u << 17;
    private const uint SupportedFlags = ValidNewlineFlag | UseRedundantGrainDirectoryFlag;

    private readonly FileStream _stream;
    private readonly VirtualDiskMetadataInfo _metadata;
    private readonly ulong _grainSizeBytes;
    private readonly ulong _activeGrainDirectoryOffsetBytes;
    private readonly ulong _grainDirectoryEntries;
    private readonly ulong _grainTableBytes;
    private readonly ulong _metadataEndOffsetBytes;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private VmdkSparseGuestByteReader(
        FileStream stream,
        VirtualDiskMetadataInfo metadata,
        ulong activeGrainDirectoryOffsetBytes,
        ulong grainDirectoryEntries,
        ulong grainTableBytes,
        ulong metadataEndOffsetBytes)
    {
        _stream = stream;
        _metadata = metadata;
        _grainSizeBytes = metadata.GrainSizeBytes;
        _activeGrainDirectoryOffsetBytes = activeGrainDirectoryOffsetBytes;
        _grainDirectoryEntries = grainDirectoryEntries;
        _grainTableBytes = grainTableBytes;
        _metadataEndOffsetBytes = metadataEndOffsetBytes;
    }

    public ulong Length => _metadata.CapacityBytes;

    public static async ValueTask<VmdkSparseGuestByteReader> OpenAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new VmdkSparseImageProvider();
        var metadata = await provider.ReadVirtualDiskMetadataAsync(imagePath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateSupportedSlice(metadata);

        var fullPath = Path.GetFullPath(imagePath);
        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        try
        {
            var fileLength = checked((ulong)stream.Length);
            var metadataEndOffsetBytes = CheckedMultiply(
                metadata.OverheadSectors,
                SectorSize,
                "VMDK metadata overhead");
            var grains = DivideRoundUp(metadata.CapacitySectors, metadata.GrainSizeSectors);
            var directoryEntries = DivideRoundUp(grains, metadata.GrainTableEntries);
            if (directoryEntries == 0)
                throw new InvalidDataException("VMDK sparse geometry does not require any grain-directory entry.");

            var directorySector = (metadata.Flags & UseRedundantGrainDirectoryFlag) != 0
                ? metadata.RedundantGrainDirectoryOffsetSectors
                : metadata.GrainDirectoryOffsetSectors;
            if (directorySector == 0)
                throw new InvalidDataException("The active VMDK grain directory is not present.");

            var directoryOffsetBytes = CheckedMultiply(directorySector, SectorSize, "VMDK grain-directory offset");
            var directoryBytes = CheckedMultiply(directoryEntries, sizeof(uint), "VMDK grain-directory size");
            EnsurePhysicalRange(fileLength, directoryOffsetBytes, directoryBytes, "VMDK active grain directory");
            EnsureMetadataRange(metadataEndOffsetBytes, directoryOffsetBytes, directoryBytes, "VMDK active grain directory");

            var grainTableBytes = CheckedMultiply(metadata.GrainTableEntries, sizeof(uint), "VMDK grain-table size");
            if (grainTableBytes == 0)
                throw new InvalidDataException("VMDK grain-table size is zero.");

            return new VmdkSparseGuestByteReader(
                stream,
                metadata,
                directoryOffsetBytes,
                directoryEntries,
                grainTableBytes,
                metadataEndOffsetBytes);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
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
            var currentGuestOffset = guestOffset;
            var destinationOffset = 0;

            while (destinationOffset < destination.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var grainIndex = currentGuestOffset / _grainSizeBytes;
                var withinGrain = currentGuestOffset % _grainSizeBytes;
                var chunk = checked((int)Math.Min(
                    (ulong)(destination.Length - destinationOffset),
                    _grainSizeBytes - withinGrain));

                var directoryIndex = grainIndex / _metadata.GrainTableEntries;
                var grainTableIndex = grainIndex % _metadata.GrainTableEntries;
                if (directoryIndex >= _grainDirectoryEntries)
                    throw new InvalidDataException("VMDK guest offset requires a grain-directory entry outside the declared virtual geometry.");

                var directoryEntryOffset = CheckedAdd(
                    _activeGrainDirectoryOffsetBytes,
                    CheckedMultiply(directoryIndex, sizeof(uint), "VMDK grain-directory entry offset"),
                    "VMDK grain-directory entry offset");
                var grainTableSector = await ReadUInt32AtAsync(directoryEntryOffset, cancellationToken);

                if (grainTableSector == 0)
                {
                    destination.Slice(destinationOffset, chunk).Span.Clear();
                }
                else
                {
                    var grainTableOffset = CheckedMultiply(grainTableSector, SectorSize, "VMDK grain-table offset");
                    EnsurePhysicalRange(grainTableOffset, _grainTableBytes, "VMDK grain table");
                    EnsureMetadataRange(_metadataEndOffsetBytes, grainTableOffset, _grainTableBytes, "VMDK grain table");

                    var grainEntryOffset = CheckedAdd(
                        grainTableOffset,
                        CheckedMultiply(grainTableIndex, sizeof(uint), "VMDK grain-table entry offset"),
                        "VMDK grain-table entry offset");
                    var grainSector = await ReadUInt32AtAsync(grainEntryOffset, cancellationToken);

                    await ReadMappedChunkAsync(
                        grainSector,
                        withinGrain,
                        destination.Slice(destinationOffset, chunk),
                        cancellationToken);
                }

                currentGuestOffset = CheckedAdd(currentGuestOffset, (ulong)chunk, "VMDK guest read cursor");
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
        uint grainSector,
        ulong withinGrain,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        if (grainSector == 0)
        {
            // This reader accepts no parent chain, so an unallocated grain is guest-visible zeroes.
            destination.Span.Clear();
            return;
        }

        var grainOffset = CheckedMultiply(grainSector, SectorSize, "VMDK grain offset");
        if (grainOffset < _metadataEndOffsetBytes)
            throw new InvalidDataException("VMDK grain data points inside the declared metadata overhead region.");

        EnsurePhysicalRange(grainOffset, _grainSizeBytes, "VMDK grain data");
        var physicalOffset = CheckedAdd(grainOffset, withinGrain, "VMDK mapped guest byte offset");
        await ReadExactlyAtAsync(physicalOffset, destination, cancellationToken);
    }

    private static void ValidateSupportedSlice(VirtualDiskMetadataInfo metadata)
    {
        if (!string.Equals(metadata.ContainerType, "VMware hosted sparse v1", StringComparison.Ordinal))
            throw new NotSupportedException("Guest-byte translation is currently implemented only for hosted sparse VMDK v1 images.");
        if (!metadata.HasEmbeddedDescriptor)
            throw new NotSupportedException("The first VMDK guest-byte reader requires an embedded descriptor to prove extent and parent semantics.");
        if (!string.Equals(metadata.CreateType, "monolithicSparse", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("The first VMDK guest-byte reader supports only monolithicSparse images.");
        if (metadata.DescriptorExtentCount != 1)
            throw new NotSupportedException("The first VMDK guest-byte reader supports exactly one embedded sparse extent.");
        if (!string.Equals(metadata.ParentCid, "ffffffff", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("VMDK parent/backing chains are not supported by this guest-byte reader.");
        if (metadata.UncleanShutdown)
            throw new InvalidDataException("VMDK image is marked as unclean; guest-byte translation refuses potentially inconsistent sparse metadata.");
        if (metadata.CompressionAlgorithm != 0 || (metadata.Flags & CompressedFlag) != 0)
            throw new NotSupportedException("Compressed VMDK grains are outside this guest-byte reader slice.");
        if ((metadata.Flags & MetadataMarkersFlag) != 0)
            throw new NotSupportedException("Stream-optimized VMDK metadata markers are outside this guest-byte reader slice.");
        if ((metadata.Flags & ZeroedGrainEntryFlag) != 0)
            throw new NotSupportedException("Zeroed-grain table entry overloading is outside this VMDK v1 guest-byte reader slice.");
        if ((metadata.Flags & ~SupportedFlags) != 0)
            throw new NotSupportedException("VMDK sparse flags contain semantics outside the proven guest-byte reader slice.");
        if ((ulong)metadata.SectorSize != SectorSize)
            throw new InvalidDataException("VMDK sparse sector size is not the expected 512 bytes.");
    }

    private async ValueTask<uint> ReadUInt32AtAsync(ulong fileOffset, CancellationToken cancellationToken)
    {
        var buffer = new byte[sizeof(uint)];
        await ReadExactlyAtAsync(fileOffset, buffer, cancellationToken);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private async ValueTask ReadExactlyAtAsync(
        ulong fileOffset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        EnsurePhysicalRange(fileOffset, (ulong)destination.Length, "VMDK physical read");
        _stream.Position = checked((long)fileOffset);
        await _stream.ReadExactlyAsync(destination, cancellationToken);
    }

    private void ValidateGuestRange(ulong guestOffset, int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (guestOffset > Length || (ulong)length > Length - guestOffset)
            throw new ArgumentOutOfRangeException(nameof(guestOffset), "Requested VMDK guest range extends beyond the virtual disk.");
    }

    private void EnsurePhysicalRange(ulong offset, ulong length, string description)
        => EnsurePhysicalRange(checked((ulong)_stream.Length), offset, length, description);

    private static void EnsurePhysicalRange(ulong boundary, ulong offset, ulong length, string description)
    {
        if (offset > boundary || length > boundary - offset)
            throw new InvalidDataException($"{description} extends outside the physical VMDK file.");
    }

    private static void EnsureMetadataRange(ulong metadataEndOffset, ulong offset, ulong length, string description)
    {
        if (offset >= metadataEndOffset || length > metadataEndOffset - offset)
            throw new InvalidDataException($"{description} extends beyond the declared VMDK metadata overhead region.");
    }

    private static ulong DivideRoundUp(ulong value, ulong divisor)
    {
        if (divisor == 0)
            throw new InvalidDataException("VMDK sparse geometry contains a zero divisor.");
        return value / divisor + (value % divisor == 0 ? 0UL : 1UL);
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
