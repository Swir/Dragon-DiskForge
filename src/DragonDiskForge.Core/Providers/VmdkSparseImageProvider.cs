using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class VmdkSparseImageProvider : IVirtualDiskMetadataProvider
{
    private const uint SparseMagic = 0x564D444B;
    private const uint SupportedVersion = 1;
    private const int HeaderSize = 512;
    private const int SectorSize = 512;
    private const int MaxDescriptorBytes = 1024 * 1024;
    private const int MaxDescriptorLines = 4096;
    private const int MaxDescriptorLineChars = 4096;
    private const uint MaxGrainTableEntries = 65_536;
    private const uint ValidNewlineFlag = 1u << 0;
    private const uint CompressedFlag = 1u << 16;
    private static readonly string[] VmdkExtensions = [".vmdk"];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string Id => "vmdk-sparse";
    public string DisplayName => "VMDK sparse metadata";
    public IReadOnlyCollection<string> Extensions => VmdkExtensions;

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!Path.GetExtension(path).Equals(".vmdk", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            _ = await ReadVirtualDiskMetadataAsync(path, cancellationToken);
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

    public async ValueTask<DiskImageInfo> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var metadata = await ReadVirtualDiskMetadataAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var descriptor = metadata.HasEmbeddedDescriptor
            ? metadata.CreateType is { Length: > 0 } createType
                ? $"embedded descriptor, createType={createType}"
                : "embedded descriptor"
            : "no embedded descriptor";

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            "VMDK sparse v1",
            file.Length,
            $"VMDK hosted sparse v1 metadata ({metadata.CapacitySectors} virtual sectors; grain {metadata.GrainSizeSectors} sectors; {descriptor})",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<VirtualDiskMetadataInfo> ReadVirtualDiskMetadataAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("VMDK image was not found.", fullPath);
        if (!file.Extension.Equals(".vmdk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The VMDK sparse provider accepts only .vmdk files.");
        if (file.Length < HeaderSize)
            throw new InvalidDataException("VMDK image is too small to contain a sparse extent header.");

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken);

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        if (magic != SparseMagic)
            throw new InvalidDataException("VMDK sparse magic does not match 0x564D444B.");

        var version = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
        if (version != SupportedVersion)
            throw new InvalidDataException($"VMDK sparse header version {version} is not supported by this provider slice.");

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
        var capacity = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(12, 8));
        var grainSize = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(20, 8));
        var descriptorOffset = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(28, 8));
        var descriptorSize = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(36, 8));
        var numGtesPerGt = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(44, 4));
        var rgdOffset = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(48, 8));
        var gdOffset = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(56, 8));
        var overhead = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(64, 8));
        var uncleanShutdown = header[72];
        var singleEndLine = header[73];
        var nonEndLine = header[74];
        var doubleEndLine1 = header[75];
        var doubleEndLine2 = header[76];
        var compressionAlgorithm = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(77, 2));

        ValidateHeader(
            file.Length,
            flags,
            capacity,
            grainSize,
            descriptorOffset,
            descriptorSize,
            numGtesPerGt,
            rgdOffset,
            gdOffset,
            overhead,
            uncleanShutdown,
            singleEndLine,
            nonEndLine,
            doubleEndLine1,
            doubleEndLine2,
            compressionAlgorithm);

        var descriptor = descriptorOffset == 0
            ? DescriptorMetadata.Empty
            : await ReadDescriptorAsync(
                stream,
                file.Length,
                descriptorOffset,
                descriptorSize,
                cancellationToken);

        return new VirtualDiskMetadataInfo(
            file.FullName,
            "VMware hosted sparse v1",
            SectorSize,
            capacity,
            grainSize,
            flags,
            descriptorOffset,
            descriptorSize,
            numGtesPerGt,
            rgdOffset,
            gdOffset,
            overhead,
            compressionAlgorithm,
            uncleanShutdown != 0,
            descriptor.Version,
            descriptor.CreateType,
            descriptor.Cid,
            descriptor.ParentCid,
            descriptor.ExtentCount);
    }

    private static void ValidateHeader(
        long fileLength,
        uint flags,
        ulong capacity,
        ulong grainSize,
        ulong descriptorOffset,
        ulong descriptorSize,
        uint numGtesPerGt,
        ulong rgdOffset,
        ulong gdOffset,
        ulong overhead,
        byte uncleanShutdown,
        byte singleEndLine,
        byte nonEndLine,
        byte doubleEndLine1,
        byte doubleEndLine2,
        ushort compressionAlgorithm)
    {
        if (capacity == 0 || capacity > ulong.MaxValue / SectorSize)
            throw new InvalidDataException("VMDK virtual capacity is invalid or overflows the supported byte range.");
        if (grainSize <= 8 || !IsPowerOfTwo(grainSize) || grainSize > capacity)
            throw new InvalidDataException("VMDK grain size must be a power of two greater than 8 sectors and not exceed capacity.");
        if (capacity % grainSize != 0)
            throw new InvalidDataException("VMDK capacity must be aligned to the sparse grain size in this provider slice.");
        if (numGtesPerGt == 0 || numGtesPerGt > MaxGrainTableEntries)
            throw new InvalidDataException("VMDK grain-table entry count is outside the supported safety bounds.");
        if (uncleanShutdown > 1)
            throw new InvalidDataException("VMDK unclean-shutdown flag is malformed.");

        if ((descriptorOffset == 0) != (descriptorSize == 0))
            throw new InvalidDataException("VMDK descriptor offset and size must either both be zero or both be present.");

        if (descriptorOffset != 0)
        {
            if (descriptorOffset < 1)
                throw new InvalidDataException("VMDK embedded descriptor cannot overlap the sparse header.");

            var descriptorBytes = CheckedSectorBytes(descriptorSize, "VMDK descriptor size");
            if (descriptorBytes <= 0 || descriptorBytes > MaxDescriptorBytes)
                throw new InvalidDataException("VMDK embedded descriptor exceeds the supported safety bound.");

            var descriptorByteOffset = CheckedSectorBytes(descriptorOffset, "VMDK descriptor offset");
            EnsureRange(fileLength, descriptorByteOffset, descriptorBytes, "VMDK embedded descriptor");
        }

        ValidateMetadataOffset(fileLength, rgdOffset, "VMDK redundant grain-directory offset");
        ValidateMetadataOffset(fileLength, gdOffset, "VMDK grain-directory offset");

        if (overhead == 0)
            throw new InvalidDataException("VMDK metadata overhead must be non-zero.");
        var overheadBytes = CheckedSectorBytes(overhead, "VMDK metadata overhead");
        if (overheadBytes > fileLength)
            throw new InvalidDataException("VMDK metadata overhead extends beyond the physical sparse file.");

        if ((flags & ValidNewlineFlag) != 0
            && (singleEndLine != (byte)'\n'
                || nonEndLine != (byte)' '
                || doubleEndLine1 != (byte)'\r'
                || doubleEndLine2 != (byte)'\n'))
        {
            throw new InvalidDataException("VMDK newline-detection bytes are inconsistent with the valid-newline flag.");
        }

        var compressed = (flags & CompressedFlag) != 0;
        if (compressed && compressionAlgorithm == 0)
            throw new InvalidDataException("VMDK compressed flag is set without a compression algorithm.");
        if (!compressed && compressionAlgorithm != 0)
            throw new InvalidDataException("VMDK compression algorithm is present while the compressed flag is clear.");
        if (compressionAlgorithm > 1)
            throw new InvalidDataException("VMDK compression algorithm is not supported by this metadata slice.");
    }

    private static async ValueTask<DescriptorMetadata> ReadDescriptorAsync(
        FileStream stream,
        long fileLength,
        ulong descriptorOffset,
        ulong descriptorSize,
        CancellationToken cancellationToken)
    {
        var byteOffset = CheckedSectorBytes(descriptorOffset, "VMDK descriptor offset");
        var byteCount = CheckedSectorBytes(descriptorSize, "VMDK descriptor size");
        EnsureRange(fileLength, byteOffset, byteCount, "VMDK embedded descriptor");
        if (byteCount > MaxDescriptorBytes)
            throw new InvalidDataException("VMDK descriptor exceeds the supported safety bound.");

        var buffer = new byte[checked((int)byteCount)];
        stream.Position = byteOffset;
        await stream.ReadExactlyAsync(buffer, cancellationToken);

        var zero = Array.IndexOf(buffer, (byte)0);
        var textLength = zero >= 0 ? zero : buffer.Length;
        var text = StrictUtf8.GetString(buffer, 0, textLength);
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        if (lines.Length > MaxDescriptorLines)
            throw new InvalidDataException("VMDK descriptor exceeds the supported line-count safety limit.");

        string? version = null;
        string? createType = null;
        string? cid = null;
        string? parentCid = null;
        var extentCount = 0;

        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rawLine.Length > MaxDescriptorLineChars)
                throw new InvalidDataException("VMDK descriptor contains an overlong line.");

            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (IsExtentLine(line))
            {
                extentCount++;
                if (extentCount > 4096)
                    throw new InvalidDataException("VMDK descriptor contains too many extent declarations.");
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            var key = line[..equals].Trim();
            var value = Unquote(line[(equals + 1)..].Trim());
            if (value.Length > MaxDescriptorLineChars)
                throw new InvalidDataException("VMDK descriptor metadata value is too long.");

            switch (key.ToLowerInvariant())
            {
                case "version":
                    version = SetUnique(version, value, "descriptor version");
                    break;
                case "createtype":
                    createType = SetUnique(createType, value, "createType");
                    break;
                case "cid":
                    cid = SetUnique(cid, value, "CID");
                    break;
                case "parentcid":
                    parentCid = SetUnique(parentCid, value, "parentCID");
                    break;
            }
        }

        if (!string.Equals(version, "1", StringComparison.Ordinal))
            throw new InvalidDataException("Embedded VMDK descriptor must declare version=1 in this provider slice.");
        if (string.IsNullOrWhiteSpace(createType))
            throw new InvalidDataException("Embedded VMDK descriptor must declare createType.");

        return new DescriptorMetadata(version, createType, cid, parentCid, extentCount);
    }

    private static string SetUnique(string? current, string value, string label)
    {
        if (current is null)
            return value;
        if (!string.Equals(current, value, StringComparison.Ordinal))
            throw new InvalidDataException($"VMDK descriptor contains conflicting {label} values.");
        return current;
    }

    private static bool IsExtentLine(string line)
    {
        var firstSpace = line.IndexOfAny([' ', '\t']);
        var token = firstSpace >= 0 ? line[..firstSpace] : line;
        return token.Equals("RW", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RDONLY", StringComparison.OrdinalIgnoreCase)
            || token.Equals("NOACCESS", StringComparison.OrdinalIgnoreCase);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        if (value.StartsWith('"') || value.EndsWith('"'))
            throw new InvalidDataException("VMDK descriptor contains an unterminated quoted value.");
        return value;
    }

    private static void ValidateMetadataOffset(long fileLength, ulong sectorOffset, string label)
    {
        if (sectorOffset == 0)
            return;

        var byteOffset = CheckedSectorBytes(sectorOffset, label);
        EnsureRange(fileLength, byteOffset, SectorSize, label);
    }

    private static long CheckedSectorBytes(ulong sectors, string label)
    {
        if (sectors > (ulong)long.MaxValue / SectorSize)
            throw new InvalidDataException($"{label} exceeds the supported file range.");
        return checked((long)sectors * SectorSize);
    }

    private static void EnsureRange(long boundary, long offset, long count, string label)
    {
        if (offset < 0 || count < 0 || offset > boundary || count > boundary - offset)
            throw new InvalidDataException($"{label} lies outside the physical VMDK file.");
    }

    private static bool IsPowerOfTwo(ulong value)
        => value != 0 && (value & (value - 1)) == 0;

    private sealed record DescriptorMetadata(
        string? Version,
        string? CreateType,
        string? Cid,
        string? ParentCid,
        int ExtentCount)
    {
        public static DescriptorMetadata Empty { get; } = new(null, null, null, null, 0);
    }
}
