using System.Buffers.Binary;
using System.Xml;
using System.Xml.Linq;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class DmgUdifImageProvider : IDmgMetadataProvider
{
    private const uint KolyMagic = 0x6B6F6C79;
    private const uint SupportedVersion = 4;
    private const int TrailerSize = 512;
    private const int LogicalSectorSize = 512;
    private const int MaxXmlBytes = 16 * 1024 * 1024;
    private const int MaxBlkxEntries = 4096;
    private static readonly string[] DmgExtensions = [".dmg"];

    public string Id => "dmg-udif";
    public string DisplayName => "DMG / UDIF metadata";
    public IReadOnlyCollection<string> Extensions => DmgExtensions;

    public async ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;
        if (!Path.GetExtension(path).Equals(".dmg", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            _ = await ReadDmgMetadataAsync(path, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or OverflowException or UnauthorizedAccessException or XmlException)
        {
            return false;
        }
    }

    public async ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var metadata = await ReadDmgMetadataAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var plist = metadata.HasXmlPlist ? $"XML plist, {metadata.BlkxEntryCount} blkx entr{(metadata.BlkxEntryCount == 1 ? "y" : "ies")}" : "no XML plist";

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            "DMG / UDIF",
            file.Length,
            $"UDIF v{metadata.Version} metadata ({metadata.SectorCount} sectors; {plist})",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<DmgMetadataInfo> ReadDmgMetadataAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("DMG image was not found.", fullPath);
        if (!file.Extension.Equals(".dmg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The DMG provider accepts only .dmg files.");
        if (file.Length < TrailerSize)
            throw new InvalidDataException("DMG image is too small to contain a UDIF trailer.");

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var trailer = new byte[TrailerSize];
        stream.Position = file.Length - TrailerSize;
        await stream.ReadExactlyAsync(trailer, cancellationToken);
        var span = trailer.AsSpan();

        var signature = ReadU32(span, 0);
        if (signature != KolyMagic)
            throw new InvalidDataException("DMG UDIF trailer does not contain the 'koly' signature.");

        var version = ReadU32(span, 4);
        var headerSize = ReadU32(span, 8);
        var flags = ReadU32(span, 12);
        var runningDataForkOffset = ReadU64(span, 16);
        var dataForkOffset = ReadU64(span, 24);
        var dataForkLength = ReadU64(span, 32);
        var resourceForkOffset = ReadU64(span, 40);
        var resourceForkLength = ReadU64(span, 48);
        var segmentNumber = ReadU32(span, 56);
        var segmentCount = ReadU32(span, 60);
        var segmentId = Convert.ToHexString(span.Slice(64, 16));
        var dataChecksumType = ReadU32(span, 80);
        var dataChecksumSize = ReadU32(span, 84);
        var xmlOffset = ReadU64(span, 216);
        var xmlLength = ReadU64(span, 224);
        var masterChecksumType = ReadU32(span, 352);
        var masterChecksumSize = ReadU32(span, 356);
        var imageVariant = ReadU32(span, 488);
        var sectorCount = ReadU64(span, 492);

        var payloadBoundary = checked(file.Length - TrailerSize);
        ValidateTrailer(
            payloadBoundary,
            version,
            headerSize,
            dataForkOffset,
            dataForkLength,
            resourceForkOffset,
            resourceForkLength,
            segmentNumber,
            segmentCount,
            dataChecksumSize,
            xmlOffset,
            xmlLength,
            masterChecksumSize,
            sectorCount);

        var blkxEntryCount = xmlLength == 0
            ? 0
            : await ReadBlkxEntryCountAsync(stream, xmlOffset, xmlLength, cancellationToken);

        return new DmgMetadataInfo(
            file.FullName,
            version,
            headerSize,
            flags,
            runningDataForkOffset,
            dataForkOffset,
            dataForkLength,
            resourceForkOffset,
            resourceForkLength,
            segmentNumber,
            segmentCount,
            segmentId,
            dataChecksumType,
            dataChecksumSize,
            xmlOffset,
            xmlLength,
            masterChecksumType,
            masterChecksumSize,
            imageVariant,
            sectorCount,
            blkxEntryCount);
    }

    private static void ValidateTrailer(
        long payloadBoundary,
        uint version,
        uint headerSize,
        ulong dataForkOffset,
        ulong dataForkLength,
        ulong resourceForkOffset,
        ulong resourceForkLength,
        uint segmentNumber,
        uint segmentCount,
        uint dataChecksumSize,
        ulong xmlOffset,
        ulong xmlLength,
        uint masterChecksumSize,
        ulong sectorCount)
    {
        if (version != SupportedVersion)
            throw new InvalidDataException($"UDIF version {version} is outside the proven DMG slice.");
        if (headerSize != TrailerSize)
            throw new InvalidDataException("UDIF trailer size must be exactly 512 bytes.");
        if (sectorCount == 0 || sectorCount > ulong.MaxValue / LogicalSectorSize)
            throw new InvalidDataException("UDIF sector count is invalid or overflows the supported virtual-size range.");

        if (segmentCount > 1)
            throw new InvalidDataException("Segmented DMG images are outside this first provider slice.");
        if (segmentCount <= 1 && segmentNumber > 1)
            throw new InvalidDataException("UDIF segment metadata is inconsistent for a single-file image.");

        if (dataChecksumSize > 1024 || masterChecksumSize > 1024)
            throw new InvalidDataException("UDIF checksum-size metadata exceeds the 128-byte checksum fields.");

        ValidatePhysicalRange(payloadBoundary, dataForkOffset, dataForkLength, "UDIF data fork");

        if ((resourceForkOffset == 0) != (resourceForkLength == 0))
            throw new InvalidDataException("UDIF resource-fork offset and length must either both be zero or both be present.");
        ValidatePhysicalRange(payloadBoundary, resourceForkOffset, resourceForkLength, "UDIF resource fork");

        if ((xmlOffset == 0) != (xmlLength == 0))
            throw new InvalidDataException("UDIF XML offset and length must either both be zero or both be present.");
        if (xmlLength > MaxXmlBytes)
            throw new InvalidDataException("UDIF XML plist exceeds the bounded metadata-read limit.");
        ValidatePhysicalRange(payloadBoundary, xmlOffset, xmlLength, "UDIF XML plist");
    }

    private static void ValidatePhysicalRange(long boundary, ulong offset, ulong length, string label)
    {
        if (length == 0)
            return;
        if (offset > (ulong)long.MaxValue || length > (ulong)long.MaxValue)
            throw new InvalidDataException($"{label} exceeds the supported file range.");

        var start = checked((long)offset);
        var count = checked((long)length);
        if (start < 0 || count < 0 || start > boundary || count > boundary - start)
            throw new InvalidDataException($"{label} lies outside the DMG payload area.");
    }

    private static async ValueTask<int> ReadBlkxEntryCountAsync(
        FileStream stream,
        ulong xmlOffset,
        ulong xmlLength,
        CancellationToken cancellationToken)
    {
        var length = checked((int)xmlLength);
        var buffer = new byte[length];
        stream.Position = checked((long)xmlOffset);
        await stream.ReadExactlyAsync(buffer, cancellationToken);

        using var memory = new MemoryStream(buffer, writable: false);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            MaxCharactersInDocument = MaxXmlBytes
        };

        using var reader = XmlReader.Create(memory, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;
        if (root is null || root.Name.LocalName != "plist")
            throw new InvalidDataException("UDIF XML metadata is not an Apple plist document.");

        var blkxKeys = document
            .Descendants()
            .Where(element => element.Name.LocalName == "key" && string.Equals(element.Value, "blkx", StringComparison.Ordinal))
            .Take(2)
            .ToArray();

        if (blkxKeys.Length == 0)
            return 0;
        if (blkxKeys.Length > 1)
            throw new InvalidDataException("UDIF plist contains multiple blkx collections.");

        var array = blkxKeys[0].ElementsAfterSelf().FirstOrDefault();
        if (array is null || array.Name.LocalName != "array")
            throw new InvalidDataException("UDIF blkx key is not followed by an array.");

        var count = array.Elements().Count(element => element.Name.LocalName == "dict");
        if (count > MaxBlkxEntries)
            throw new InvalidDataException("UDIF plist contains too many blkx entries for this provider slice.");
        return count;
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private static ulong ReadU64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset, 8));
}
