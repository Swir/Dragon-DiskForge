using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class NrgImageProvider : ITrackLayoutProvider
{
    private const int MaxTracks = 99;
    private const int MaxChunks = 4096;
    private const int MaxDaoChunkBytes = 256 * 1024;
    private const int MaxCueChunkBytes = 64 * 1024;
    private const int CdPregapFrames = 150;

    private static readonly string[] NrgExtensions = [".nrg"];
    private static readonly HashSet<int> SupportedSectorSizes = [2048, 2336, 2352, 2448];

    public string Id => "nrg";
    public string DisplayName => "NRG track layout";
    public IReadOnlyCollection<string> Extensions => NrgExtensions;

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!Path.GetExtension(path).Equals(".nrg", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            _ = await ReadTrackLayoutAsync(path, cancellationToken);
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

    public async ValueTask<DiskImageInfo> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var parsed = await ParseAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var layout = parsed.Layout;

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            $"NRG v{parsed.Version}",
            file.Length,
            $"Nero NRG v{parsed.Version} DAO track layout ({layout.TrackCount} track(s): {layout.DataTrackCount} data, {layout.AudioTrackCount} audio)",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<OpticalTrackLayoutInfo> ReadTrackLayoutAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
        => (await ParseAsync(imagePath, cancellationToken)).Layout;

    private static async ValueTask<NrgParseResult> ParseAsync(
        string imagePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("NRG image was not found.", fullPath);
        if (!file.Extension.Equals(".nrg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The NRG provider accepts only .nrg files.");
        if (file.Length < 8)
            throw new InvalidDataException("NRG image is too small to contain a valid footer.");

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var footer = await ReadFooterAsync(stream, cancellationToken);
        if (footer.ChunkOffset < 0 || footer.ChunkOffset > footer.FooterOffset - 8)
            throw new InvalidDataException("NRG chunk-table offset lies outside the metadata area.");

        var cuePoints = new List<CuePoint>();
        var daoGroups = new List<DaoTrackGroup>();
        var position = footer.ChunkOffset;
        var chunkCount = 0;
        var endSeen = false;

        while (position < footer.FooterOffset)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++chunkCount > MaxChunks)
                throw new InvalidDataException("NRG metadata exceeds the supported chunk-count limit.");

            EnsureRange(footer.FooterOffset, position, 8, "NRG chunk header");
            var header = await ReadRangeAsync(stream, position, 8, cancellationToken);
            var id = Encoding.ASCII.GetString(header, 0, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));
            var dataOffset = checked(position + 8);
            EnsureRange(footer.FooterOffset, dataOffset, chunkSize, $"NRG {id} chunk");

            switch (id)
            {
                case "CUEX":
                case "CUES":
                    if (chunkSize > MaxCueChunkBytes)
                        throw new InvalidDataException($"NRG {id} chunk exceeds the supported safety bound.");
                    cuePoints.AddRange(ParseCuePoints(
                        await ReadRangeAsync(stream, dataOffset, checked((int)chunkSize), cancellationToken),
                        id));
                    break;

                case "DAOX":
                    if (footer.Version != 2)
                        throw new InvalidDataException("NRG v1 footer cannot use a DAOX track table.");
                    if (chunkSize > MaxDaoChunkBytes)
                        throw new InvalidDataException("NRG DAOX chunk exceeds the supported safety bound.");
                    daoGroups.Add(ParseDao(
                        await ReadRangeAsync(stream, dataOffset, checked((int)chunkSize), cancellationToken),
                        is64Bit: true));
                    break;

                case "DAOI":
                    if (footer.Version != 1)
                        throw new InvalidDataException("NRG v2 footer cannot use a DAOI track table in this provider slice.");
                    if (chunkSize > MaxDaoChunkBytes)
                        throw new InvalidDataException("NRG DAOI chunk exceeds the supported safety bound.");
                    daoGroups.Add(ParseDao(
                        await ReadRangeAsync(stream, dataOffset, checked((int)chunkSize), cancellationToken),
                        is64Bit: false));
                    break;

                case "END!":
                    if (chunkSize != 0)
                        throw new InvalidDataException("NRG END! chunk must be empty.");
                    endSeen = true;
                    position = dataOffset;
                    if (position != footer.FooterOffset)
                        throw new InvalidDataException("NRG contains trailing metadata after the END! chunk.");
                    goto ChunksComplete;
            }

            position = checked(dataOffset + chunkSize);
        }

    ChunksComplete:
        if (!endSeen)
            throw new InvalidDataException("NRG metadata does not contain a terminating END! chunk.");
        if (daoGroups.Count == 0)
            throw new InvalidDataException("NRG image contains no supported DAOI/DAOX track-layout chunk.");

        var rawTracks = daoGroups.SelectMany(group => group.Tracks).ToArray();
        if (rawTracks.Length is 0 or > MaxTracks)
            throw new InvalidDataException("NRG user-track count is outside the supported range.");

        var duplicateTrack = rawTracks
            .GroupBy(track => track.Number)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTrack is not null)
            throw new InvalidDataException($"NRG contains duplicate track number {duplicateTrack.Key:00}.");

        var resolved = new List<OpticalTrackInfo>(rawTracks.Length);
        foreach (var track in rawTracks.OrderBy(track => track.Number))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (track.Index0 < 0 || track.Index1 < track.Index0 || track.End <= track.Index1)
                throw new InvalidDataException($"NRG track {track.Number:00} has an invalid byte range.");
            if (track.End > footer.ChunkOffset)
                throw new InvalidDataException($"NRG track {track.Number:00} overlaps the metadata chunk table.");
            if (!SupportedSectorSizes.Contains(track.SectorSize))
                throw new InvalidDataException($"NRG track {track.Number:00} declares unsupported sector size {track.SectorSize}.");

            var lengthBytes = checked(track.End - track.Index1);
            if (lengthBytes % track.SectorSize != 0)
                throw new InvalidDataException($"NRG track {track.Number:00} byte range is not sector-aligned.");

            var mode = DecodeMode(track.ModeCode, track.SectorSize);
            var index1 = ResolveCuePoint(cuePoints, track.Number, 1)
                ?? throw new InvalidDataException($"NRG track {track.Number:00} has no unambiguous CUE index 1 LBA.");
            var index0 = ResolveCuePoint(cuePoints, track.Number, 0);

            var cueIsAudio = ClassifyCueType(index1.Type);
            if (cueIsAudio is null)
                throw new InvalidDataException($"NRG track {track.Number:00} has an unsupported CUE control/type value 0x{index1.Type:X2}.");
            if (cueIsAudio.Value != mode.IsAudio)
                throw new InvalidDataException($"NRG track {track.Number:00} CUE control disagrees with the DAO track mode.");

            var sectorCount = lengthBytes / track.SectorSize;
            resolved.Add(new OpticalTrackInfo(
                track.Number,
                mode.Label,
                file.Name,
                file.FullName,
                track.SectorSize,
                index1.Lba,
                sectorCount,
                track.Index1,
                lengthBytes,
                index0?.Lba,
                index1.Lba,
                mode.IsAudio));
        }

        return new NrgParseResult(
            footer.Version,
            new OpticalTrackLayoutInfo(file.FullName, resolved, [file.FullName]));
    }

    private static async ValueTask<NrgFooter> ReadFooterAsync(
        FileStream stream,
        CancellationToken cancellationToken)
    {
        if (stream.Length >= 12)
        {
            var v2Footer = await ReadRangeAsync(stream, stream.Length - 12, 12, cancellationToken);
            if (v2Footer.AsSpan(0, 4).SequenceEqual("NER5"u8))
            {
                var rawOffset = BinaryPrimitives.ReadUInt64BigEndian(v2Footer.AsSpan(4, 8));
                if (rawOffset > long.MaxValue)
                    throw new InvalidDataException("NRG v2 chunk-table offset exceeds the supported file range.");

                return new NrgFooter(2, checked((long)rawOffset), stream.Length - 12);
            }
        }

        var v1Footer = await ReadRangeAsync(stream, stream.Length - 8, 8, cancellationToken);
        if (v1Footer.AsSpan(0, 4).SequenceEqual("NERO"u8))
        {
            var rawOffset = BinaryPrimitives.ReadUInt32BigEndian(v1Footer.AsSpan(4, 4));
            return new NrgFooter(1, rawOffset, stream.Length - 8);
        }

        throw new InvalidDataException("NRG footer does not contain the NERO or NER5 signature.");
    }

    private static IReadOnlyList<CuePoint> ParseCuePoints(byte[] data, string chunkId)
    {
        if (data.Length == 0 || data.Length % 8 != 0)
            throw new InvalidDataException($"NRG {chunkId} chunk size is not aligned to 8-byte cue entries.");

        var points = new List<CuePoint>(data.Length / 8);
        for (var offset = 0; offset < data.Length; offset += 8)
        {
            var type = data[offset];
            var rawTrack = data[offset + 1];
            var index = DecodeBcd(data[offset + 2], "NRG cue index");
            if (data[offset + 3] != 0)
                throw new InvalidDataException($"NRG {chunkId} cue entry reserved byte is not zero.");

            var lba = chunkId == "CUEX"
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 4, 4))
                : DecodeCuesLba(data.AsSpan(offset + 4, 4));

            points.Add(new CuePoint(type, rawTrack, index, lba));
        }

        return points;
    }

    private static int DecodeCuesLba(ReadOnlySpan<byte> position)
    {
        if (position.Length != 4 || position[0] != 0)
            throw new InvalidDataException("NRG CUES position must use the 00:MM:SS:FF layout.");

        var minute = DecodeBcd(position[1], "NRG CUES minute");
        var second = DecodeBcd(position[2], "NRG CUES second");
        var frame = DecodeBcd(position[3], "NRG CUES frame");
        if (second >= 60 || frame >= 75)
            throw new InvalidDataException("NRG CUES MSF position is outside the valid CD range.");

        return checked((((minute * 60) + second) * 75) + frame - CdPregapFrames);
    }

    private static int DecodeBcd(byte value, string label)
    {
        var high = value >> 4;
        var low = value & 0x0F;
        if (high > 9 || low > 9)
            throw new InvalidDataException($"{label} is not valid BCD.");
        return (high * 10) + low;
    }

    private static DaoTrackGroup ParseDao(byte[] data, bool is64Bit)
    {
        const int commonSize = 22;
        var entrySize = is64Bit ? 42 : 30;
        if (data.Length < commonSize || (data.Length - commonSize) % entrySize != 0)
            throw new InvalidDataException($"NRG {(is64Bit ? "DAOX" : "DAOI")} chunk has an invalid size.");

        var firstTrack = data[20];
        var lastTrack = data[21];
        if (firstTrack is 0 or > MaxTracks || lastTrack < firstTrack || lastTrack > MaxTracks)
            throw new InvalidDataException("NRG DAO track-number range is invalid.");

        var entryCount = (data.Length - commonSize) / entrySize;
        var expectedCount = lastTrack - firstTrack + 1;
        if (entryCount != expectedCount)
            throw new InvalidDataException("NRG DAO track count does not match the declared first/last track range.");

        var tracks = new List<RawTrack>(entryCount);
        for (var index = 0; index < entryCount; index++)
        {
            var offset = commonSize + (index * entrySize);
            var sectorSize = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 12, 2));
            var modeCode = data[offset + 14];

            long index0;
            long index1;
            long end;
            if (is64Bit)
            {
                index0 = CheckedUInt64ToInt64(BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 18, 8)));
                index1 = CheckedUInt64ToInt64(BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 26, 8)));
                end = CheckedUInt64ToInt64(BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 34, 8)));
            }
            else
            {
                index0 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 18, 4));
                index1 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 22, 4));
                end = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 26, 4));
            }

            tracks.Add(new RawTrack(
                firstTrack + index,
                sectorSize,
                modeCode,
                index0,
                index1,
                end));
        }

        return new DaoTrackGroup(firstTrack, lastTrack, tracks);
    }

    private static TrackMode DecodeMode(byte modeCode, int sectorSize)
    {
        var mode = modeCode switch
        {
            0x07 => new TrackMode("AUDIO", true),
            0x10 => new TrackMode("AUDIO+SUB", true),
            0x00 => new TrackMode("MODE1", false),
            0x02 => new TrackMode("MODE2-FORM1", false),
            0x03 => new TrackMode("MODE2", false),
            0x05 => new TrackMode("MODE1-RAW", false),
            0x06 => new TrackMode("MODE2-RAW", false),
            0x0F => new TrackMode("MODE1-RAW+SUB", false),
            0x11 => new TrackMode("MODE2-RAW+SUB", false),
            0x20 => new TrackMode("MODE2-RAW", false),
            _ => throw new InvalidDataException($"NRG DAO track mode 0x{modeCode:X2} is not supported by this provider slice.")
        };

        if (mode.IsAudio && sectorSize is not (2352 or 2448))
            throw new InvalidDataException($"NRG audio track mode 0x{modeCode:X2} uses an invalid sector size {sectorSize}.");

        return mode with { Label = $"{mode.Label}/{sectorSize}" };
    }

    private static CuePoint? ResolveCuePoint(IReadOnlyList<CuePoint> points, int trackNumber, int index)
    {
        var matches = points
            .Where(point => point.Index == index && ResolveCueTrackNumber(point.RawTrack) == trackNumber)
            .ToArray();

        if (matches.Length == 0)
            return null;
        if (matches.Length > 1)
        {
            var distinct = matches.Select(point => (point.Type, point.Lba)).Distinct().ToArray();
            if (distinct.Length != 1)
                return null;
        }

        return matches[0];
    }

    private static int ResolveCueTrackNumber(byte rawTrack)
    {
        if (rawTrack == 0xAA)
            return 0xAA;

        var high = rawTrack >> 4;
        var low = rawTrack & 0x0F;
        if (high <= 9 && low <= 9)
        {
            var bcd = (high * 10) + low;
            if (bcd is >= 1 and <= MaxTracks)
                return bcd;
        }

        return rawTrack;
    }

    private static bool? ClassifyCueType(byte type)
        => type switch
        {
            0x01 or 0x21 => true,
            0x41 => false,
            _ => null
        };

    private static long CheckedUInt64ToInt64(ulong value)
    {
        if (value > long.MaxValue)
            throw new InvalidDataException("NRG 64-bit track offset exceeds the supported file range.");
        return checked((long)value);
    }

    private static async ValueTask<byte[]> ReadRangeAsync(
        FileStream stream,
        long offset,
        int count,
        CancellationToken cancellationToken)
    {
        EnsureRange(stream.Length, offset, count, "NRG data");
        var buffer = new byte[count];
        stream.Position = offset;
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return buffer;
    }

    private static void EnsureRange(long boundary, long offset, long count, string label)
    {
        if (offset < 0 || count < 0 || offset > boundary || count > boundary - offset)
            throw new InvalidDataException($"{label} points outside the permitted NRG range.");
    }

    private sealed record NrgFooter(int Version, long ChunkOffset, long FooterOffset);
    private sealed record NrgParseResult(int Version, OpticalTrackLayoutInfo Layout);
    private sealed record DaoTrackGroup(int FirstTrack, int LastTrack, IReadOnlyList<RawTrack> Tracks);
    private sealed record RawTrack(
        int Number,
        int SectorSize,
        byte ModeCode,
        long Index0,
        long Index1,
        long End);
    private sealed record CuePoint(byte Type, byte RawTrack, int Index, int Lba);
    private sealed record TrackMode(string Label, bool IsAudio);
}
