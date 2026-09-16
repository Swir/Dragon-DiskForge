using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class FloppyImageProvider : IMediaGeometryProvider
{
    private const int BootSectorReadSize = 512;
    private static readonly string[] FloppyExtensions = [".ima", ".flp"];

    private static readonly IReadOnlyDictionary<long, FloppyGeometry> KnownGeometries =
        new Dictionary<long, FloppyGeometry>
        {
            [163_840] = new("160 KB 5.25-inch", 512, 8, 1, 40),
            [184_320] = new("180 KB 5.25-inch", 512, 9, 1, 40),
            [327_680] = new("320 KB 5.25-inch", 512, 8, 2, 40),
            [368_640] = new("360 KB 5.25-inch", 512, 9, 2, 40),
            [737_280] = new("720 KB 3.5-inch", 512, 9, 2, 80),
            [1_228_800] = new("1.2 MB 5.25-inch", 512, 15, 2, 80),
            [1_474_560] = new("1.44 MB 3.5-inch", 512, 18, 2, 80),
            [1_720_320] = new("1.68 MB DMF", 512, 21, 2, 80),
            [2_949_120] = new("2.88 MB 3.5-inch", 512, 36, 2, 80)
        };

    public string Id => "floppy-ima";
    public string DisplayName => "IMA / Floppy geometry";
    public IReadOnlyCollection<string> Extensions => FloppyExtensions;

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!FloppyExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return false;

        try
        {
            _ = await ReadMediaGeometryAsync(path, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or OverflowException)
        {
            return false;
        }
    }

    public async ValueTask<DiskImageInfo> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var geometry = await ReadMediaGeometryAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var metadata = geometry.BootParameterBlockDetected
            ? $"{geometry.GeometryName}; BPB {geometry.FileSystemHint}"
            : $"{geometry.GeometryName}; raw geometry";

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            "IMA / Floppy",
            file.Length,
            metadata,
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<MediaGeometryInfo> ReadMediaGeometryAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Floppy image was not found.", fullPath);

        if (!KnownGeometries.TryGetValue(file.Length, out var geometry))
            throw new InvalidDataException("The image size does not match a supported raw floppy geometry.");

        var boot = new byte[BootSectorReadSize];
        await using (var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await stream.ReadExactlyAsync(boot, cancellationToken);
        }

        var bpb = TryParseBpb(boot, file.Length);
        if (bpb.State == BpbState.Invalid)
            throw new InvalidDataException(bpb.Error ?? "Floppy BIOS Parameter Block is inconsistent with the image.");

        if (bpb.State == BpbState.Valid)
        {
            var parsed = bpb.Info!;
            return new MediaGeometryInfo(
                geometry.Name,
                parsed.BytesPerSector,
                parsed.SectorsPerTrack,
                parsed.Heads,
                parsed.Tracks,
                parsed.TotalSectors,
                file.Length,
                BootParameterBlockDetected: true,
                parsed.OemName,
                parsed.FileSystemHint,
                parsed.VolumeLabel,
                parsed.MediaDescriptor);
        }

        return new MediaGeometryInfo(
            geometry.Name,
            geometry.BytesPerSector,
            geometry.SectorsPerTrack,
            geometry.Heads,
            geometry.Tracks,
            checked(geometry.SectorsPerTrack * geometry.Heads * geometry.Tracks),
            file.Length,
            BootParameterBlockDetected: false,
            string.Empty,
            "Unknown / unformatted",
            string.Empty,
            null);
    }

    private static BpbParseResult TryParseBpb(ReadOnlySpan<byte> boot, long fileLength)
    {
        if (boot.Length < BootSectorReadSize)
            return BpbParseResult.None;

        var bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(11, 2));
        var sectorsPerCluster = boot[13];
        var reservedSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(14, 2));
        var fatCount = boot[16];
        var rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(17, 2));
        var total16 = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(19, 2));
        var mediaDescriptor = boot[21];
        var sectorsPerFat = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(22, 2));
        var sectorsPerTrack = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(24, 2));
        var heads = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(26, 2));
        var total32 = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(32, 4));

        var hasBpbShape = bytesPerSector != 0
            || sectorsPerCluster != 0
            || reservedSectors != 0
            || fatCount != 0
            || rootEntries != 0
            || total16 != 0
            || total32 != 0
            || sectorsPerFat != 0
            || sectorsPerTrack != 0
            || heads != 0;

        if (!hasBpbShape)
            return BpbParseResult.None;

        if (bytesPerSector is not (128 or 256 or 512 or 1024 or 2048 or 4096))
            return BpbParseResult.Invalid("Floppy BPB declares an unsupported bytes-per-sector value.");
        if (sectorsPerCluster == 0 || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0)
            return BpbParseResult.Invalid("Floppy BPB sectors-per-cluster value is invalid.");
        if (reservedSectors == 0 || fatCount == 0 || fatCount > 4)
            return BpbParseResult.Invalid("Floppy BPB reserved-sector/FAT-count values are invalid.");
        if (rootEntries == 0 || sectorsPerFat == 0 || sectorsPerTrack == 0 || heads == 0)
            return BpbParseResult.Invalid("Floppy BPB is missing required FAT12/16 geometry fields.");

        var totalSectors = total16 != 0 ? total16 : checked((int)total32);
        if (totalSectors <= 0)
            return BpbParseResult.Invalid("Floppy BPB total-sector count is invalid.");

        var declaredBytes = checked((long)totalSectors * bytesPerSector);
        if (declaredBytes != fileLength)
            return BpbParseResult.Invalid("Floppy BPB capacity does not match the image length.");

        var sectorsPerCylinder = checked(sectorsPerTrack * heads);
        if (sectorsPerCylinder == 0 || totalSectors % sectorsPerCylinder != 0)
            return BpbParseResult.Invalid("Floppy BPB geometry does not form a whole number of tracks.");

        var tracks = totalSectors / sectorsPerCylinder;
        if (tracks <= 0 || tracks > 255)
            return BpbParseResult.Invalid("Floppy BPB track count is outside the supported range.");

        var rootDirectorySectors = ((rootEntries * 32) + (bytesPerSector - 1)) / bytesPerSector;
        var dataSectors = totalSectors
            - reservedSectors
            - checked(fatCount * sectorsPerFat)
            - rootDirectorySectors;
        if (dataSectors <= 0)
            return BpbParseResult.Invalid("Floppy BPB leaves no data sectors.");

        var clusterCount = dataSectors / sectorsPerCluster;
        var fileSystemHint = clusterCount < 4_085
            ? "FAT12"
            : clusterCount < 65_525 ? "FAT16" : "FAT32-like";

        var oem = ReadAscii(boot.Slice(3, 8));
        var volumeLabel = boot[38] is 0x28 or 0x29
            ? ReadAscii(boot.Slice(43, 11))
            : string.Empty;
        var declaredFs = boot[38] is 0x28 or 0x29
            ? ReadAscii(boot.Slice(54, 8))
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(declaredFs))
            fileSystemHint = declaredFs;

        return BpbParseResult.Valid(new ParsedBpb(
            bytesPerSector,
            sectorsPerTrack,
            heads,
            tracks,
            totalSectors,
            oem,
            fileSystemHint,
            volumeLabel,
            mediaDescriptor));
    }

    private static string ReadAscii(ReadOnlySpan<byte> value)
        => Encoding.ASCII.GetString(value).Trim('\0', ' ');

    private sealed record FloppyGeometry(
        string Name,
        int BytesPerSector,
        int SectorsPerTrack,
        int Heads,
        int Tracks);

    private sealed record ParsedBpb(
        int BytesPerSector,
        int SectorsPerTrack,
        int Heads,
        int Tracks,
        int TotalSectors,
        string OemName,
        string FileSystemHint,
        string VolumeLabel,
        byte MediaDescriptor);

    private enum BpbState
    {
        None,
        Valid,
        Invalid
    }

    private sealed record BpbParseResult(BpbState State, ParsedBpb? Info, string? Error)
    {
        public static BpbParseResult None { get; } = new(BpbState.None, null, null);
        public static BpbParseResult Valid(ParsedBpb info) => new(BpbState.Valid, info, null);
        public static BpbParseResult Invalid(string error) => new(BpbState.Invalid, null, error);
    }
}
