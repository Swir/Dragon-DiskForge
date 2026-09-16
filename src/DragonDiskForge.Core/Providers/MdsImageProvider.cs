using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class MdsImageProvider : ITrackLayoutProvider
{
    private const int HeaderSize = 92;
    private const int SessionSize = 24;
    private const int TrackBlockSize = 80;
    private const int FooterSize = 16;
    private const int MaxSessions = 99;
    private const int MaxTracks = 99;
    private const int MaxFilenameBytes = 2048;
    private const long MaxDescriptorBytes = 64L * 1024 * 1024;

    private static readonly byte[] Signature = Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR");
    private static readonly string[] MdsExtensions = [".mds", ".mdf"];
    private static readonly HashSet<int> SupportedSectorSizes = [2048, 2336, 2352, 2448];

    public string Id => "mdf-mds";
    public string DisplayName => "MDF / MDS track layout";
    public IReadOnlyCollection<string> Extensions => MdsExtensions;

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!MdsExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
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
        var layout = await ReadTrackLayoutAsync(path, cancellationToken);
        var file = new FileInfo(path);

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            "MDF/MDS",
            file.Length,
            $"MDS track layout ({layout.TrackCount} track(s): {layout.DataTrackCount} data, {layout.AudioTrackCount} audio; {layout.DataFiles.Count} MDF file(s))",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<OpticalTrackLayoutInfo> ReadTrackLayoutAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("MDF/MDS image was not found.", fullPath);

        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".mdf", StringComparison.OrdinalIgnoreCase))
        {
            var companion = Path.ChangeExtension(fullPath, ".mds");
            if (!File.Exists(companion))
                throw new InvalidDataException("An MDF payload is supported only when a same-name companion MDS descriptor exists.");

            var layout = await ParseMdsAsync(companion, cancellationToken);
            if (!layout.DataFiles.Any(path => PathsEqual(path, fullPath)))
                throw new InvalidDataException("The companion MDS descriptor does not reference this MDF payload.");

            return layout;
        }

        if (!extension.Equals(".mds", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The MDF/MDS provider accepts only .mds descriptors and companion .mdf payloads.");

        return await ParseMdsAsync(fullPath, cancellationToken);
    }

    private static async ValueTask<OpticalTrackLayoutInfo> ParseMdsAsync(
        string mdsPath,
        CancellationToken cancellationToken)
    {
        var descriptor = new FileInfo(mdsPath);
        if (!descriptor.Exists)
            throw new FileNotFoundException("MDS descriptor was not found.", mdsPath);
        if (descriptor.Length < HeaderSize || descriptor.Length > MaxDescriptorBytes)
            throw new InvalidDataException("MDS descriptor size is outside the supported safety bounds.");

        await using var stream = new FileStream(
            descriptor.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var header = await ReadRangeAsync(stream, 0, HeaderSize, cancellationToken);
        if (!header.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            throw new InvalidDataException("MDS signature is invalid.");

        var majorVersion = header[16];
        var minorVersion = header[17];
        if (majorVersion > 1)
            throw new InvalidDataException($"MDS version {majorVersion}.{minorVersion} is not supported by this provider slice.");

        var sessionCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(20, 2));
        if (sessionCount is 0 or > MaxSessions)
            throw new InvalidDataException("MDS session count is outside the supported range.");

        var sessionsOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80, 4));
        EnsureRange(stream.Length, sessionsOffset, checked(sessionCount * SessionSize), "MDS session table");

        var tracks = new List<RawTrack>();
        var seenTrackNumbers = new HashSet<int>();

        for (var sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sessionOffset = checked((long)sessionsOffset + (sessionIndex * SessionSize));
            var session = await ReadRangeAsync(stream, sessionOffset, SessionSize, cancellationToken);

            var sessionNumber = BinaryPrimitives.ReadUInt16LittleEndian(session.AsSpan(8, 2));
            var totalBlocks = session[10];
            var firstTrack = BinaryPrimitives.ReadUInt16LittleEndian(session.AsSpan(12, 2));
            var lastTrack = BinaryPrimitives.ReadUInt16LittleEndian(session.AsSpan(14, 2));
            var trackBlocksOffset = BinaryPrimitives.ReadUInt32LittleEndian(session.AsSpan(20, 4));

            if (sessionNumber == 0 || totalBlocks == 0)
                throw new InvalidDataException("MDS session metadata is incomplete.");
            if (firstTrack is > MaxTracks || lastTrack is > MaxTracks || (firstTrack != 0 && lastTrack != 0 && firstTrack > lastTrack))
                throw new InvalidDataException("MDS session track range is invalid.");

            EnsureRange(stream.Length, trackBlocksOffset, checked(totalBlocks * TrackBlockSize), "MDS track table");

            for (var blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trackOffset = checked((long)trackBlocksOffset + (blockIndex * TrackBlockSize));
                var block = await ReadRangeAsync(stream, trackOffset, TrackBlockSize, cancellationToken);

                var trackNumber = block[4];
                var extraOffset = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(12, 4));

                // A0/A1/A2 and other lead-in blocks are TOC metadata, not user tracks.
                if (trackNumber is 0 or >= 0xA0 || extraOffset == 0)
                    continue;

                if (trackNumber > MaxTracks)
                    throw new InvalidDataException("MDS track number is outside the supported range.");
                if (firstTrack != 0 && trackNumber < firstTrack || lastTrack != 0 && trackNumber > lastTrack)
                    throw new InvalidDataException("MDS track lies outside its session track range.");
                if (!seenTrackNumbers.Add(trackNumber))
                    throw new InvalidDataException("MDS contains duplicate user track numbers.");
                if (tracks.Count >= MaxTracks)
                    throw new InvalidDataException("MDS exceeds the supported user-track limit.");

                var mode = block[0];
                var addressControl = block[2];
                var sectorSize = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(16, 2));
                var startSector = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(36, 4));
                var startOffset = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(40, 8));
                var footerOffset = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(52, 4));

                if (!SupportedSectorSizes.Contains(sectorSize))
                    throw new InvalidDataException($"MDS track {trackNumber:00} declares unsupported sector size {sectorSize}.");
                if (startSector > int.MaxValue)
                    throw new InvalidDataException("MDS track start sector exceeds the supported range.");

                var payloadPath = footerOffset == 0
                    ? Path.ChangeExtension(descriptor.FullName, ".mdf")
                    : await ResolvePayloadPathAsync(stream, descriptor.FullName, footerOffset, cancellationToken);

                var payload = new FileInfo(payloadPath);
                if (!payload.Exists)
                    throw new InvalidDataException($"Referenced MDF payload was not found: {Path.GetFileName(payloadPath)}");
                if (payload.Length <= 0)
                    throw new InvalidDataException($"Referenced MDF payload is empty: {payload.Name}");
                if (startOffset >= (ulong)payload.Length)
                    throw new InvalidDataException($"MDS track {trackNumber:00} starts outside its MDF payload.");

                var isAudio = (addressControl & 0x04) == 0;
                tracks.Add(new RawTrack(
                    trackNumber,
                    mode,
                    payload.Name,
                    payload.FullName,
                    sectorSize,
                    checked((long)startSector),
                    checked((long)startOffset),
                    isAudio));
            }
        }

        if (tracks.Count == 0)
            throw new InvalidDataException("MDS descriptor contains no supported user tracks.");

        tracks.Sort((left, right) => left.Number.CompareTo(right.Number));
        var resolved = ResolveTrackRanges(tracks);
        var dataFiles = resolved
            .Select(track => track.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OpticalTrackLayoutInfo(descriptor.FullName, resolved, dataFiles);
    }

    private static IReadOnlyList<OpticalTrackInfo> ResolveTrackRanges(IReadOnlyList<RawTrack> tracks)
    {
        var result = new List<OpticalTrackInfo>(tracks.Count);

        foreach (var fileGroup in tracks.GroupBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = fileGroup.OrderBy(track => track.StartByte).ToArray();
            var payloadLength = new FileInfo(fileGroup.Key).Length;

            for (var index = 0; index < ordered.Length; index++)
            {
                var track = ordered[index];
                var endByte = index + 1 < ordered.Length
                    ? ordered[index + 1].StartByte
                    : payloadLength;

                if (endByte <= track.StartByte || endByte > payloadLength)
                    throw new InvalidDataException($"MDS track {track.Number:00} has an invalid byte range.");

                var lengthBytes = checked(endByte - track.StartByte);
                if (lengthBytes % track.SectorSize != 0)
                    throw new InvalidDataException($"MDS track {track.Number:00} byte range is not aligned to its sector size.");

                var sectorCount = lengthBytes / track.SectorSize;
                if (sectorCount <= 0)
                    throw new InvalidDataException($"MDS track {track.Number:00} contains no sectors.");

                result.Add(new OpticalTrackInfo(
                    track.Number,
                    $"MDS 0x{track.Mode:X2}/{track.SectorSize}",
                    track.FileName,
                    track.FilePath,
                    track.SectorSize,
                    track.StartSector,
                    sectorCount,
                    track.StartByte,
                    lengthBytes,
                    Index00Frames: null,
                    Index01Frames: checked((int)track.StartSector),
                    track.IsAudio));
            }
        }

        result.Sort((left, right) => left.Number.CompareTo(right.Number));
        return result;
    }

    private static async ValueTask<string> ResolvePayloadPathAsync(
        FileStream stream,
        string mdsPath,
        uint footerOffset,
        CancellationToken cancellationToken)
    {
        EnsureRange(stream.Length, footerOffset, FooterSize, "MDS footer");
        var footer = await ReadRangeAsync(stream, footerOffset, FooterSize, cancellationToken);
        var filenameOffset = BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(0, 4));
        var isWide = BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(4, 4)) != 0;

        if (filenameOffset == 0 || filenameOffset >= stream.Length)
            throw new InvalidDataException("MDS footer filename offset is invalid.");

        var filename = await ReadNullTerminatedStringAsync(stream, filenameOffset, isWide, cancellationToken);
        if (string.IsNullOrWhiteSpace(filename))
            throw new InvalidDataException("MDS footer contains an empty payload filename.");

        if (filename.StartsWith("*.", StringComparison.Ordinal))
            filename = Path.GetFileNameWithoutExtension(mdsPath) + filename[1..];

        if (Path.IsPathRooted(filename))
            throw new InvalidDataException("MDS payload filename must be relative to the descriptor directory.");

        var directory = Path.GetDirectoryName(mdsPath)
            ?? throw new InvalidDataException("MDS descriptor directory could not be resolved.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(directory, filename));

        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MDS payload filename escapes the descriptor directory.");
        if (!Path.GetExtension(resolved).Equals(".mdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MDF/MDS provider currently accepts only .mdf payload files.");

        return resolved;
    }

    private static async ValueTask<string> ReadNullTerminatedStringAsync(
        FileStream stream,
        long offset,
        bool wide,
        CancellationToken cancellationToken)
    {
        var remaining = checked((int)Math.Min(MaxFilenameBytes, stream.Length - offset));
        if (remaining <= 0)
            throw new InvalidDataException("MDS filename lies outside the descriptor.");

        var bytes = await ReadRangeAsync(stream, offset, remaining, cancellationToken);
        if (wide)
        {
            var end = -1;
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                if (bytes[i] == 0 && bytes[i + 1] == 0)
                {
                    end = i;
                    break;
                }
            }

            if (end < 0)
                throw new InvalidDataException("MDS UTF-16 payload filename is not terminated within the safety bound.");

            return Encoding.Unicode.GetString(bytes, 0, end).Trim();
        }

        var terminator = Array.IndexOf(bytes, (byte)0);
        if (terminator < 0)
            throw new InvalidDataException("MDS payload filename is not terminated within the safety bound.");

        return Encoding.ASCII.GetString(bytes, 0, terminator).Trim();
    }

    private static async ValueTask<byte[]> ReadRangeAsync(
        FileStream stream,
        long offset,
        int count,
        CancellationToken cancellationToken)
    {
        EnsureRange(stream.Length, offset, count, "MDS data");
        var buffer = new byte[count];
        stream.Position = offset;
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return buffer;
    }

    private static void EnsureRange(long fileLength, long offset, long count, string label)
    {
        if (offset < 0 || count < 0 || offset > fileLength || count > fileLength - offset)
            throw new InvalidDataException($"{label} points outside the MDS descriptor.");
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private sealed record RawTrack(
        int Number,
        byte Mode,
        string FileName,
        string FilePath,
        int SectorSize,
        long StartSector,
        long StartByte,
        bool IsAudio);
}
