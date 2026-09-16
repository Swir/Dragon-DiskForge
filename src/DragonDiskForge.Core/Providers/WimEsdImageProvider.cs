using System.Buffers.Binary;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class WimEsdImageProvider : IWimMetadataProvider
{
    private const int HeaderSize = 208;
    private const uint StandardVersion = WimMetadataInfo.StandardVersion;
    private const uint SolidVersion = WimMetadataInfo.SolidVersion;
    private const uint FlagCompressed = 0x00000002;
    private const uint FlagSpanned = 0x00000008;
    private const uint FlagWriteInProgress = 0x00000040;
    private const uint KnownLowFlags = 0x000000FF;
    private const uint KnownCompressionFlags = 0x001F0000;
    private const uint MaxImages = 65_535;
    private const uint MinCompressedChunkSize = 4 * 1024;
    private const uint MaxCompressedChunkSize = 1024 * 1024 * 1024;
    private static readonly byte[] Magic = [(byte)'M', (byte)'S', (byte)'W', (byte)'I', (byte)'M', 0, 0, 0];
    private static readonly string[] WimExtensions = [".wim", ".esd"];

    public string Id => "wim-esd";
    public string DisplayName => "WIM / ESD container metadata";
    public IReadOnlyCollection<string> Extensions => WimExtensions;

    public async ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".wim", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".esd", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            _ = await ReadWimMetadataAsync(path, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or OverflowException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var metadata = await ReadWimMetadataAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var integrity = metadata.IntegrityTable.IsPresent ? ", integrity table" : string.Empty;

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            metadata.IsSolidVersion ? "ESD / solid WIM" : "WIM",
            file.Length,
            $"{metadata.ContainerFlavor} metadata (version {metadata.Version}; {metadata.ImageCount} image(s); chunk {metadata.ChunkSize} bytes{integrity})",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<WimMetadataInfo> ReadWimMetadataAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("WIM/ESD image was not found.", fullPath);

        var extension = file.Extension;
        if (!extension.Equals(".wim", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".esd", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The WIM/ESD provider accepts only .wim and .esd files.");
        if (file.Length < HeaderSize)
            throw new InvalidDataException("WIM/ESD file is too small to contain the 208-byte header.");

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var span = header.AsSpan();

        if (!span.Slice(0, 8).SequenceEqual(Magic))
            throw new InvalidDataException("WIM image tag does not match MSWIM\\0\\0\\0.");

        var declaredHeaderSize = ReadU32(span, 8);
        var version = ReadU32(span, 12);
        var flags = ReadU32(span, 16);
        var chunkSize = ReadU32(span, 20);
        var guid = new Guid(span.Slice(24, 16)).ToString("D");
        var partNumber = ReadU16(span, 40);
        var totalParts = ReadU16(span, 42);
        var imageCount = ReadU32(span, 44);
        var lookup = ReadResource(span, 48, "Lookup table");
        var xml = ReadResource(span, 72, "XML data");
        var boot = ReadResource(span, 96, "Boot metadata");
        var bootIndex = ReadU32(span, 120);
        var integrity = ReadResource(span, 124, "Integrity table");

        ValidateHeader(file.Length, declaredHeaderSize, version, flags, chunkSize, partNumber, totalParts, imageCount, bootIndex);
        ValidateResource(file.Length, lookup);
        ValidateResource(file.Length, xml);
        ValidateResource(file.Length, boot);
        ValidateResource(file.Length, integrity);

        return new WimMetadataInfo(
            file.FullName,
            declaredHeaderSize,
            version,
            flags,
            chunkSize,
            guid,
            partNumber,
            totalParts,
            imageCount,
            lookup,
            xml,
            boot,
            bootIndex,
            integrity);
    }

    private static void ValidateHeader(
        long fileLength,
        uint declaredHeaderSize,
        uint version,
        uint flags,
        uint chunkSize,
        ushort partNumber,
        ushort totalParts,
        uint imageCount,
        uint bootIndex)
    {
        if (declaredHeaderSize != HeaderSize)
            throw new InvalidDataException("WIM header size must be exactly 208 bytes.");
        if (version != StandardVersion && version != SolidVersion)
            throw new InvalidDataException($"WIM version {version} is outside the proven WIM/ESD slice.");
        if ((flags & ~(KnownLowFlags | KnownCompressionFlags)) != 0)
            throw new InvalidDataException("WIM header contains unknown flag bits.");
        if ((flags & FlagWriteInProgress) != 0)
            throw new InvalidDataException("WIM is marked write-in-progress and is not safe to claim as stable metadata.");
        if ((flags & FlagSpanned) != 0)
            throw new InvalidDataException("Spanned WIM metadata is outside this standalone provider slice.");
        if (partNumber == 0 || totalParts == 0 || partNumber > totalParts)
            throw new InvalidDataException("WIM part-number metadata is invalid.");
        if (partNumber != 1 || totalParts != 1)
            throw new InvalidDataException("Split/multipart WIM files are outside this first provider slice.");
        if (imageCount > MaxImages)
            throw new InvalidDataException("WIM image count exceeds the provider safety bound.");
        if (bootIndex > imageCount)
            throw new InvalidDataException("WIM boot index exceeds the image count.");

        var compressed = (flags & FlagCompressed) != 0;
        if (compressed)
        {
            if (chunkSize < MinCompressedChunkSize || chunkSize > MaxCompressedChunkSize || !IsPowerOfTwo(chunkSize))
                throw new InvalidDataException("Compressed WIM chunk size is outside supported structural bounds.");
        }
        else if (chunkSize != 0 && (!IsPowerOfTwo(chunkSize) || chunkSize > MaxCompressedChunkSize))
        {
            throw new InvalidDataException("Uncompressed WIM chunk-size metadata is malformed.");
        }

        if (fileLength < HeaderSize)
            throw new InvalidDataException("WIM physical file is shorter than its header.");
    }

    private static WimResourceInfo ReadResource(ReadOnlySpan<byte> header, int offset, string name)
    {
        var packed = ReadU64(header, offset);
        var flags = (byte)(packed >> 56);
        var storedSize = packed & 0x00FFFFFFFFFFFFFFUL;
        var physicalOffset = ReadU64(header, offset + 8);
        var originalSize = ReadU64(header, offset + 16);
        return new WimResourceInfo(name, flags, storedSize, physicalOffset, originalSize);
    }

    private static void ValidateResource(long fileLength, WimResourceInfo resource)
    {
        const byte knownResourceFlags = 0x0F;
        if ((resource.Flags & ~knownResourceFlags) != 0)
            throw new InvalidDataException($"{resource.Name} uses unknown WIM resource flags.");
        if ((resource.Flags & 0x08) != 0)
            throw new InvalidDataException($"{resource.Name} is spanned across WIM parts, which is outside this slice.");

        if (resource.StoredSize == 0)
        {
            if (resource.Offset != 0 || resource.OriginalSize != 0)
                throw new InvalidDataException($"{resource.Name} has an empty size but non-empty offset/original-size metadata.");
            return;
        }

        if (resource.Offset < HeaderSize)
            throw new InvalidDataException($"{resource.Name} overlaps the WIM header.");
        if (resource.Offset > (ulong)long.MaxValue || resource.StoredSize > (ulong)long.MaxValue)
            throw new InvalidDataException($"{resource.Name} exceeds the supported physical file range.");

        var start = checked((long)resource.Offset);
        var count = checked((long)resource.StoredSize);
        if (start > fileLength || count > fileLength - start)
            throw new InvalidDataException($"{resource.Name} lies outside the physical WIM/ESD file.");

        if (!resource.IsCompressed && resource.OriginalSize != 0 && resource.OriginalSize != resource.StoredSize)
            throw new InvalidDataException($"{resource.Name} is uncompressed but stored/original sizes disagree.");
        if (resource.IsCompressed && resource.OriginalSize < resource.StoredSize)
            throw new InvalidDataException($"{resource.Name} compressed size exceeds its original size.");
    }

    private static bool IsPowerOfTwo(uint value)
        => value != 0 && (value & (value - 1)) == 0;

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static ulong ReadU64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
}
