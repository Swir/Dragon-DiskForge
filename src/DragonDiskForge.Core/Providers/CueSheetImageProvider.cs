using System.Text.RegularExpressions;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class CueSheetImageProvider : ITrackLayoutProvider
{
    private const long MaxCueBytes = 1024 * 1024;
    private const int MaxCueLines = 10_000;
    private const int MaxTracks = 99;
    private static readonly string[] CueExtensions = [".cue", ".bin"];

    private static readonly Regex FileRegex = new(
        @"^FILE\s+(?:""(?<quoted>[^""]+)""|(?<plain>\S+))\s+(?<type>\S+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TrackRegex = new(
        @"^TRACK\s+(?<number>\d{1,2})\s+(?<mode>\S+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IndexRegex = new(
        @"^INDEX\s+(?<index>\d{2})\s+(?<minutes>\d{1,3}):(?<seconds>\d{2}):(?<frames>\d{2})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string Id => "bin-cue";
    public string DisplayName => "BIN / CUE track layout";
    public IReadOnlyCollection<string> Extensions => CueExtensions;

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!CueExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
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
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException or UnauthorizedAccessException)
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
        var audio = layout.AudioTrackCount;
        var data = layout.DataTrackCount;

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            "BIN/CUE",
            file.Length,
            $"CUE track layout ({layout.TrackCount} track(s): {data} data, {audio} audio; {layout.DataFiles.Count} BIN file(s))",
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
            throw new FileNotFoundException("BIN/CUE image was not found.", fullPath);

        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
            return await ParseCueAsync(fullPath, cancellationToken);

        if (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            var cuePath = Path.ChangeExtension(fullPath, ".cue");
            if (!File.Exists(cuePath))
                throw new InvalidDataException("A BIN file is supported only when a same-name companion CUE file exists.");

            var layout = await ParseCueAsync(cuePath, cancellationToken);
            if (!layout.DataFiles.Any(path => PathsEqual(path, fullPath)))
                throw new InvalidDataException("The companion CUE file does not reference this BIN file.");

            return layout;
        }

        throw new InvalidDataException("The BIN/CUE provider accepts only .cue and companion .bin files.");
    }

    private static async ValueTask<OpticalTrackLayoutInfo> ParseCueAsync(
        string cuePath,
        CancellationToken cancellationToken)
    {
        var cueFile = new FileInfo(cuePath);
        if (!cueFile.Exists)
            throw new FileNotFoundException("CUE sheet was not found.", cuePath);
        if (cueFile.Length <= 0 || cueFile.Length > MaxCueBytes)
            throw new InvalidDataException("CUE sheet size is outside the supported safety bounds.");

        var cueDirectory = Path.GetDirectoryName(cueFile.FullName)
            ?? throw new InvalidDataException("CUE sheet directory could not be resolved.");
        var cueRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cueDirectory)) + Path.DirectorySeparatorChar;

        var builders = new List<TrackBuilder>();
        string? currentFileName = null;
        string? currentFilePath = null;
        TrackBuilder? currentTrack = null;
        var lastTrackNumber = 0;
        var lineCount = 0;

        using var reader = new StreamReader(cueFile.FullName, detectEncodingFromByteOrderMarks: true);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await reader.ReadLineAsync(cancellationToken);
            if (raw is null)
                break;

            lineCount++;
            if (lineCount > MaxCueLines)
                throw new InvalidDataException("CUE sheet exceeds the supported line-count safety limit.");

            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileMatch = FileRegex.Match(line);
            if (fileMatch.Success)
            {
                var fileType = fileMatch.Groups["type"].Value;
                if (!fileType.Equals("BINARY", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsupported CUE FILE type '{fileType}'. Only BINARY is supported in this provider slice.");

                var referenced = fileMatch.Groups["quoted"].Success
                    ? fileMatch.Groups["quoted"].Value
                    : fileMatch.Groups["plain"].Value;

                if (string.IsNullOrWhiteSpace(referenced) || Path.IsPathRooted(referenced))
                    throw new InvalidDataException("CUE FILE must use a non-empty relative path.");

                var resolved = Path.GetFullPath(Path.Combine(cueDirectory, referenced));
                if (!resolved.StartsWith(cueRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("CUE FILE escapes the CUE sheet directory.");
                if (!Path.GetExtension(resolved).Equals(".bin", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("BIN/CUE provider currently accepts only .bin BINARY payload files.");

                currentFileName = referenced;
                currentFilePath = resolved;
                currentTrack = null;
                continue;
            }

            var trackMatch = TrackRegex.Match(line);
            if (trackMatch.Success)
            {
                if (currentFilePath is null || currentFileName is null)
                    throw new InvalidDataException("CUE TRACK appears before a FILE declaration.");
                if (builders.Count >= MaxTracks)
                    throw new InvalidDataException("CUE sheet exceeds the supported track-count limit.");

                var number = int.Parse(trackMatch.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (number <= lastTrackNumber || number is < 1 or > 99)
                    throw new InvalidDataException("CUE track numbers must be strictly increasing from 1 through 99.");

                var mode = trackMatch.Groups["mode"].Value.ToUpperInvariant();
                var (sectorSize, isAudio) = DecodeTrackMode(mode);
                currentTrack = new TrackBuilder(number, mode, currentFileName, currentFilePath, sectorSize, isAudio);
                builders.Add(currentTrack);
                lastTrackNumber = number;
                continue;
            }

            var indexMatch = IndexRegex.Match(line);
            if (indexMatch.Success)
            {
                if (currentTrack is null)
                    throw new InvalidDataException("CUE INDEX appears before a TRACK declaration.");

                var index = int.Parse(indexMatch.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture);
                var frames = ParseFrames(indexMatch);
                if (index == 0)
                {
                    if (currentTrack.Index00Frames is not null)
                        throw new InvalidDataException($"Track {currentTrack.Number:00} contains duplicate INDEX 00 entries.");
                    currentTrack.Index00Frames = frames;
                }
                else if (index == 1)
                {
                    if (currentTrack.Index01Frames is not null)
                        throw new InvalidDataException($"Track {currentTrack.Number:00} contains duplicate INDEX 01 entries.");
                    currentTrack.Index01Frames = frames;
                }

                continue;
            }

            if (IsSupportedMetadataOrGapDirective(line))
                continue;

            throw new InvalidDataException($"Unsupported CUE directive: '{line}'.");
        }

        if (builders.Count == 0)
            throw new InvalidDataException("CUE sheet contains no tracks.");
        if (builders.Any(track => track.Index01Frames is null))
            throw new InvalidDataException("Every CUE track must contain INDEX 01.");

        var resolvedTracks = new List<OpticalTrackInfo>(builders.Count);
        var dataFiles = new List<string>();

        foreach (var fileGroup in builders.GroupBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tracks = fileGroup.ToArray();
            var sectorSizes = tracks.Select(track => track.SectorSize).Distinct().ToArray();
            if (sectorSizes.Length != 1)
            {
                throw new InvalidDataException(
                    "Tracks sharing one BIN file must use one sector size in this provider slice; mixed sector-size offsets are not guessed.");
            }

            var payload = new FileInfo(fileGroup.Key);
            if (!payload.Exists)
                throw new InvalidDataException($"Referenced BIN file was not found: {tracks[0].FileName}");
            if (payload.Length <= 0)
                throw new InvalidDataException($"Referenced BIN file is empty: {tracks[0].FileName}");

            var sectorSize = sectorSizes[0];
            if (payload.Length % sectorSize != 0)
                throw new InvalidDataException($"Referenced BIN file length is not aligned to {sectorSize}-byte sectors: {tracks[0].FileName}");

            var totalSectors = payload.Length / sectorSize;
            dataFiles.Add(payload.FullName);

            for (var i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                var start = track.Index01Frames!.Value;
                if (track.Index00Frames is { } index00 && index00 > start)
                    throw new InvalidDataException($"Track {track.Number:00} INDEX 00 occurs after INDEX 01.");
                if (start < 0 || start >= totalSectors)
                    throw new InvalidDataException($"Track {track.Number:00} INDEX 01 lies outside its BIN file.");

                if (i > 0 && tracks[i - 1].Index01Frames!.Value >= start)
                    throw new InvalidDataException("CUE INDEX 01 positions must increase within each BIN file.");

                var end = i + 1 < tracks.Length
                    ? tracks[i + 1].Index01Frames!.Value
                    : totalSectors;
                if (end <= start || end > totalSectors)
                    throw new InvalidDataException($"Track {track.Number:00} has an invalid sector range.");

                var sectorCount = checked(end - start);
                var startByte = checked(start * (long)sectorSize);
                var lengthBytes = checked(sectorCount * (long)sectorSize);

                resolvedTracks.Add(new OpticalTrackInfo(
                    track.Number,
                    track.Mode,
                    track.FileName,
                    payload.FullName,
                    sectorSize,
                    start,
                    sectorCount,
                    startByte,
                    lengthBytes,
                    track.Index00Frames,
                    start,
                    track.IsAudio));
            }
        }

        resolvedTracks.Sort((left, right) => left.Number.CompareTo(right.Number));
        return new OpticalTrackLayoutInfo(
            cueFile.FullName,
            resolvedTracks,
            dataFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static (int SectorSize, bool IsAudio) DecodeTrackMode(string mode)
        => mode switch
        {
            "AUDIO" => (2352, true),
            "MODE1/2048" => (2048, false),
            "MODE1/2352" => (2352, false),
            "MODE2/2336" => (2336, false),
            "MODE2/2352" => (2352, false),
            _ => throw new InvalidDataException($"Unsupported CUE track mode '{mode}'.")
        };

    private static int ParseFrames(Match match)
    {
        var minutes = int.Parse(match.Groups["minutes"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups["seconds"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var frames = int.Parse(match.Groups["frames"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (seconds >= 60 || frames >= 75)
            throw new InvalidDataException("CUE time must use MM:SS:FF with SS < 60 and FF < 75.");

        return checked(((minutes * 60) + seconds) * 75 + frames);
    }

    private static bool IsSupportedMetadataOrGapDirective(string line)
    {
        var keyword = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return keyword is not null && keyword.ToUpperInvariant() is
            "TITLE" or "PERFORMER" or "SONGWRITER" or "CATALOG" or "ISRC" or "FLAGS" or "PREGAP" or "POSTGAP" or "CDTEXTFILE";
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private sealed class TrackBuilder(
        int number,
        string mode,
        string fileName,
        string filePath,
        int sectorSize,
        bool isAudio)
    {
        public int Number { get; } = number;
        public string Mode { get; } = mode;
        public string FileName { get; } = fileName;
        public string FilePath { get; } = filePath;
        public int SectorSize { get; } = sectorSize;
        public bool IsAudio { get; } = isAudio;
        public int? Index00Frames { get; set; }
        public int? Index01Frames { get; set; }
    }
}
