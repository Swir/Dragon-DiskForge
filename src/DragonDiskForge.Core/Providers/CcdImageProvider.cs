using System.Globalization;
using System.Text.RegularExpressions;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class CcdImageProvider : ITrackLayoutProvider
{
    private const long MaxDescriptorBytes = 2L * 1024 * 1024;
    private const int MaxLines = 20_000;
    private const int MaxLineLength = 4096;
    private const int MaxTracks = 99;
    private const int ImgSectorSize = 2352;
    private const int SubchannelBytesPerSector = 96;

    private static readonly string[] CcdExtensions = [".ccd", ".img", ".sub"];
    private static readonly Regex IndexedSectionRegex = new(
        @"^(?<name>Session|Entry|TRACK)\s+(?<index>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex IndexKeyRegex = new(
        @"^INDEX\s+(?<index>\d{1,2})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string Id => "ccd-img-sub";
    public string DisplayName => "CCD / IMG / SUB track layout";
    public IReadOnlyCollection<string> Extensions => CcdExtensions;

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        if (!CcdExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return false;

        try
        {
            _ = await ParseInputAsync(path, cancellationToken);
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
        var parsed = await ParseInputAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var layout = parsed.Layout;
        var subText = parsed.SubPath is null ? "no SUB sidecar" : "validated SUB sidecar";

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            "CCD/IMG/SUB",
            file.Length,
            $"CloneCD track layout ({layout.TrackCount} track(s): {layout.DataTrackCount} data, {layout.AudioTrackCount} audio; {subText})",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<OpticalTrackLayoutInfo> ReadTrackLayoutAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
        => (await ParseInputAsync(imagePath, cancellationToken)).Layout;

    private static async ValueTask<CcdParseResult> ParseInputAsync(
        string imagePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("CCD/IMG/SUB image was not found.", fullPath);

        var extension = Path.GetExtension(fullPath);
        if (!CcdExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("The CCD/IMG/SUB provider accepts only .ccd descriptors and same-name .img/.sub companions.");

        var ccdPath = extension.Equals(".ccd", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.ChangeExtension(fullPath, ".ccd");

        if (!File.Exists(ccdPath))
            throw new InvalidDataException("An IMG/SUB companion is supported only when a same-name CCD descriptor exists.");

        var parsed = await ParseCcdAsync(ccdPath, cancellationToken);
        if (extension.Equals(".img", StringComparison.OrdinalIgnoreCase)
            && !PathsEqual(parsed.ImgPath, fullPath))
        {
            throw new InvalidDataException("The companion CCD descriptor does not resolve to this IMG payload.");
        }

        if (extension.Equals(".sub", StringComparison.OrdinalIgnoreCase))
        {
            if (parsed.SubPath is null || !PathsEqual(parsed.SubPath, fullPath))
                throw new InvalidDataException("The SUB file is not a validated same-name sidecar for this CCD image.");
        }

        return parsed;
    }

    private static async ValueTask<CcdParseResult> ParseCcdAsync(
        string ccdPath,
        CancellationToken cancellationToken)
    {
        var descriptor = new FileInfo(ccdPath);
        if (!descriptor.Exists)
            throw new FileNotFoundException("CCD descriptor was not found.", ccdPath);
        if (descriptor.Length <= 0 || descriptor.Length > MaxDescriptorBytes)
            throw new InvalidDataException("CCD descriptor size is outside the supported safety bounds.");

        var imgPath = Path.ChangeExtension(descriptor.FullName, ".img");
        var image = new FileInfo(imgPath);
        if (!image.Exists)
            throw new InvalidDataException("CCD descriptor requires a same-name IMG payload.");
        if (image.Length <= 0 || image.Length % ImgSectorSize != 0)
            throw new InvalidDataException("CloneCD IMG payload must be non-empty and aligned to 2352-byte raw sectors.");

        var totalSectors = image.Length / ImgSectorSize;
        if (totalSectors <= 0 || totalSectors > int.MaxValue)
            throw new InvalidDataException("CloneCD IMG sector count exceeds the supported range.");

        var subPath = Path.ChangeExtension(descriptor.FullName, ".sub");
        string? validatedSubPath = null;
        if (File.Exists(subPath))
        {
            var sub = new FileInfo(subPath);
            var expectedSubBytes = checked(totalSectors * SubchannelBytesPerSector);
            if (sub.Length != expectedSubBytes)
            {
                throw new InvalidDataException(
                    $"CloneCD SUB sidecar length must be exactly {SubchannelBytesPerSector} bytes per IMG sector.");
            }
            validatedSubPath = sub.FullName;
        }

        int? cloneCdVersion = null;
        var sawCloneCd = false;
        var sawDisc = false;
        var trackBuilders = new List<TrackBuilder>();
        TrackBuilder? currentTrack = null;
        SectionKind currentSection = SectionKind.None;
        var lastTrackNumber = 0;
        var lineCount = 0;

        using var reader = new StreamReader(descriptor.FullName, detectEncodingFromByteOrderMarks: true);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await reader.ReadLineAsync(cancellationToken);
            if (raw is null)
                break;

            if (++lineCount > MaxLines)
                throw new InvalidDataException("CCD descriptor exceeds the supported line-count limit.");
            if (raw.Length > MaxLineLength)
                throw new InvalidDataException("CCD descriptor contains a line longer than the supported safety bound.");

            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var sectionText = line[1..^1].Trim();
                if (sectionText.Equals("CloneCD", StringComparison.OrdinalIgnoreCase))
                {
                    if (sawCloneCd)
                        throw new InvalidDataException("CCD descriptor contains duplicate [CloneCD] sections.");
                    sawCloneCd = true;
                    currentSection = SectionKind.CloneCd;
                    currentTrack = null;
                    continue;
                }

                if (sectionText.Equals("Disc", StringComparison.OrdinalIgnoreCase))
                {
                    if (sawDisc)
                        throw new InvalidDataException("CCD descriptor contains duplicate [Disc] sections.");
                    sawDisc = true;
                    currentSection = SectionKind.Disc;
                    currentTrack = null;
                    continue;
                }

                if (sectionText.Equals("CDText", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = SectionKind.CdText;
                    currentTrack = null;
                    continue;
                }

                var indexed = IndexedSectionRegex.Match(sectionText);
                if (!indexed.Success)
                    throw new InvalidDataException($"Unsupported CCD section '[{sectionText}]'.");

                var sectionIndex = ParseNonNegativeInteger(indexed.Groups["index"].Value, "CCD section index");
                var sectionName = indexed.Groups["name"].Value;
                if (sectionName.Equals("TRACK", StringComparison.OrdinalIgnoreCase))
                {
                    if (sectionIndex is < 1 or > MaxTracks || sectionIndex != lastTrackNumber + 1)
                        throw new InvalidDataException("CCD TRACK sections must be sequential from 1 through 99.");

                    currentTrack = new TrackBuilder(sectionIndex);
                    trackBuilders.Add(currentTrack);
                    lastTrackNumber = sectionIndex;
                    currentSection = SectionKind.Track;
                }
                else
                {
                    if (sectionName.Equals("Session", StringComparison.OrdinalIgnoreCase) && sectionIndex < 1)
                        throw new InvalidDataException("CCD Session indexes must be positive.");
                    currentSection = sectionName.Equals("Session", StringComparison.OrdinalIgnoreCase)
                        ? SectionKind.Session
                        : SectionKind.Entry;
                    currentTrack = null;
                }

                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
                throw new InvalidDataException($"CCD descriptor line is not a key/value property: '{line}'.");

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Length == 0)
                throw new InvalidDataException("CCD descriptor contains an empty property name.");

            if (currentSection == SectionKind.CloneCd && key.Equals("Version", StringComparison.OrdinalIgnoreCase))
            {
                if (cloneCdVersion is not null)
                    throw new InvalidDataException("CCD descriptor contains duplicate CloneCD Version properties.");
                cloneCdVersion = ParseNonNegativeInteger(value, "CloneCD Version");
                if (cloneCdVersion is < 1 or > 3)
                    throw new InvalidDataException($"CloneCD descriptor version {cloneCdVersion} is not supported by this provider slice.");
                continue;
            }

            if (currentSection == SectionKind.Track)
            {
                if (currentTrack is null)
                    throw new InvalidDataException("CCD TRACK property has no active track section.");

                if (key.Equals("MODE", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentTrack.Mode is not null)
                        throw new InvalidDataException($"CCD track {currentTrack.Number:00} contains duplicate MODE properties.");
                    currentTrack.Mode = ParseNonNegativeInteger(value, $"CCD track {currentTrack.Number:00} MODE");
                    continue;
                }

                var indexMatch = IndexKeyRegex.Match(key);
                if (indexMatch.Success)
                {
                    var indexNumber = ParseNonNegativeInteger(indexMatch.Groups["index"].Value, "CCD INDEX number");
                    var frameOffset = ParseNonNegativeInteger(value, $"CCD track {currentTrack.Number:00} INDEX {indexNumber}");
                    if (!currentTrack.Indexes.TryAdd(indexNumber, frameOffset))
                        throw new InvalidDataException($"CCD track {currentTrack.Number:00} contains duplicate INDEX {indexNumber}.");
                    continue;
                }

                if (key.Equals("ISRC", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("FLAGS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                throw new InvalidDataException($"Unsupported CCD TRACK property '{key}'.");
            }

            // CloneCD, Disc, CDText, Session and Entry metadata is retained as
            // descriptor validation context but does not change raw IMG offsets.
            if (currentSection == SectionKind.None)
                throw new InvalidDataException("CCD property appears before any section declaration.");
        }

        if (!sawCloneCd || cloneCdVersion is null)
            throw new InvalidDataException("CCD descriptor must contain [CloneCD] and a supported Version property.");
        if (!sawDisc)
            throw new InvalidDataException("CCD descriptor must contain a [Disc] section.");
        if (trackBuilders.Count == 0)
            throw new InvalidDataException("CCD descriptor contains no TRACK sections.");

        var resolved = new List<OpticalTrackInfo>(trackBuilders.Count);
        for (var i = 0; i < trackBuilders.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = trackBuilders[i];
            if (track.Mode is null)
                throw new InvalidDataException($"CCD track {track.Number:00} is missing MODE.");
            if (!track.Indexes.TryGetValue(1, out var index1))
                throw new InvalidDataException($"CCD track {track.Number:00} is missing INDEX 1.");

            var mode = DecodeMode(track.Mode.Value);
            if (index1 < 0 || index1 >= totalSectors)
                throw new InvalidDataException($"CCD track {track.Number:00} INDEX 1 lies outside the IMG payload.");

            int? index0 = null;
            if (track.Indexes.TryGetValue(0, out var parsedIndex0))
            {
                if (parsedIndex0 > index1)
                    throw new InvalidDataException($"CCD track {track.Number:00} INDEX 0 occurs after INDEX 1.");
                index0 = parsedIndex0;
            }

            foreach (var pair in track.Indexes.OrderBy(pair => pair.Key))
            {
                if (pair.Value < 0 || pair.Value >= totalSectors)
                    throw new InvalidDataException($"CCD track {track.Number:00} INDEX {pair.Key} lies outside the IMG payload.");
            }

            if (i > 0 && trackBuilders[i - 1].Indexes.TryGetValue(1, out var previousStart) && previousStart >= index1)
                throw new InvalidDataException("CCD INDEX 1 positions must increase across tracks.");

            var end = i + 1 < trackBuilders.Count
                ? trackBuilders[i + 1].Indexes.GetValueOrDefault(1, -1)
                : checked((int)totalSectors);
            if (end <= index1 || end > totalSectors)
                throw new InvalidDataException($"CCD track {track.Number:00} has an invalid sector range.");

            var sectorCount = checked((long)end - index1);
            var startByte = checked((long)index1 * ImgSectorSize);
            var lengthBytes = checked(sectorCount * ImgSectorSize);
            resolved.Add(new OpticalTrackInfo(
                track.Number,
                mode.Label,
                image.Name,
                image.FullName,
                ImgSectorSize,
                index1,
                sectorCount,
                startByte,
                lengthBytes,
                index0,
                index1,
                mode.IsAudio));
        }

        return new CcdParseResult(
            cloneCdVersion.Value,
            image.FullName,
            validatedSubPath,
            new OpticalTrackLayoutInfo(descriptor.FullName, resolved, [image.FullName]));
    }

    private static TrackMode DecodeMode(int mode)
        => mode switch
        {
            0 => new TrackMode("AUDIO", true),
            1 => new TrackMode("MODE1/2352", false),
            2 => new TrackMode("MODE2/2352", false),
            _ => throw new InvalidDataException($"CCD track MODE={mode} is not supported by this provider slice.")
        };

    private static int ParseNonNegativeInteger(string value, string label)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            throw new InvalidDataException($"{label} must be a non-negative decimal integer.");
        return parsed;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private enum SectionKind
    {
        None,
        CloneCd,
        Disc,
        CdText,
        Session,
        Entry,
        Track
    }

    private sealed class TrackBuilder(int number)
    {
        public int Number { get; } = number;
        public int? Mode { get; set; }
        public Dictionary<int, int> Indexes { get; } = new();
    }

    private sealed record TrackMode(string Label, bool IsAudio);
    private sealed record CcdParseResult(
        int Version,
        string ImgPath,
        string? SubPath,
        OpticalTrackLayoutInfo Layout);
}
