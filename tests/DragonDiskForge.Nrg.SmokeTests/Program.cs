using System.Buffers.Binary;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-NRG-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new NrgImageProvider();

    var v2Path = Path.Combine(root, "mixed-v2.nrg");
    CreateNrg(v2Path, version: 2,
        new TrackSpec(1, 0x01, 0x07, 2352, 10),
        new TrackSpec(2, 0x41, 0x00, 2048, 20));

    Expect(await provider.CanHandleAsync(v2Path), "Valid NER5/DAOX NRG should be recognized.");
    var v2 = await provider.ReadTrackLayoutAsync(v2Path);
    Expect(v2.TrackCount == 2, "NRG v2 should expose both tracks.");
    Expect(v2.AudioTrackCount == 1 && v2.DataTrackCount == 1, "NRG v2 cue/DAO mode classification should preserve audio/data.");
    Expect(v2.Tracks[0].SectorSize == 2352 && v2.Tracks[0].SectorCount == 10, "NRG v2 audio track range should be parsed.");
    Expect(v2.Tracks[1].SectorSize == 2048 && v2.Tracks[1].SectorCount == 20, "NRG v2 data track range should be parsed.");
    Expect(v2.Tracks[0].StartSector == 0 && v2.Tracks[1].StartSector == 10, "NRG v2 StartSector must come from CUEX LBA metadata.");
    Expect(v2.Tracks[1].StartByte == 2352L * 10, "NRG v2 mixed-sector byte offsets should come from DAOX metadata.");

    var inspect = await provider.InspectAsync(v2Path);
    Expect(inspect.Format == "NRG v2", "Inspect should identify NER5 as NRG v2.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify,
        "NRG provider must remain inspection-only in this slice.");

    var v1Path = Path.Combine(root, "data-v1.nrg");
    CreateNrg(v1Path, version: 1,
        new TrackSpec(1, 0x41, 0x00, 2048, 4),
        new TrackSpec(2, 0x41, 0x00, 2048, 6));
    Expect(await provider.CanHandleAsync(v1Path), "Valid NERO/DAOI NRG should be recognized.");
    var v1 = await provider.ReadTrackLayoutAsync(v1Path);
    Expect(v1.TrackCount == 2 && v1.DataTrackCount == 2, "NRG v1 should expose both data tracks.");
    Expect(v1.Tracks[0].StartSector == 0 && v1.Tracks[0].SectorCount == 4, "NRG v1 first CUES/DAOI track should be bounded.");
    Expect(v1.Tracks[1].StartSector == 4 && v1.Tracks[1].SectorCount == 6, "NRG v1 CUES BCD MSF metadata must convert to the real LBA.");
    var inspectV1 = await provider.InspectAsync(v1Path);
    Expect(inspectV1.Format == "NRG v1", "Inspect should identify NERO as NRG v1.");

    var badFooter = Path.Combine(root, "bad-footer.nrg");
    File.Copy(v2Path, badFooter);
    using (var stream = new FileStream(badFooter, FileMode.Open, FileAccess.Write, FileShare.None))
    {
        stream.Position = stream.Length - 12;
        stream.Write("NOPE"u8);
    }
    Expect(!await provider.CanHandleAsync(badFooter), "NRG without NERO/NER5 footer magic must be rejected.");

    var badOffset = Path.Combine(root, "bad-offset.nrg");
    File.Copy(v2Path, badOffset);
    using (var stream = new FileStream(badOffset, FileMode.Open, FileAccess.Write, FileShare.None))
    {
        stream.Position = stream.Length - 8;
        Span<byte> raw = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(raw, checked((ulong)(stream.Length + 4096)));
        stream.Write(raw);
    }
    Expect(!await provider.CanHandleAsync(badOffset), "NRG chunk-table offset outside the file must be rejected.");

    var missingEnd = Path.Combine(root, "missing-end.nrg");
    CreateNrg(missingEnd, version: 2,
        new[] { new TrackSpec(1, 0x41, 0x00, 2048, 8) },
        includeEndChunk: false);
    Expect(!await provider.CanHandleAsync(missingEnd), "NRG metadata without END! must be rejected.");

    var missingCue = Path.Combine(root, "missing-cue.nrg");
    CreateNrg(missingCue, version: 2,
        new[] {
            new TrackSpec(1, 0x01, 0x07, 2352, 4),
            new TrackSpec(2, 0x41, 0x00, 2048, 4)
        },
        omittedCueTrack: 2);
    Expect(!await provider.CanHandleAsync(missingCue), "NRG DAO track without an unambiguous cue index 1 LBA must be rejected.");

    var unknownMode = Path.Combine(root, "unknown-mode.nrg");
    CreateNrg(unknownMode, version: 2,
        new TrackSpec(1, 0x41, 0x55, 2048, 4));
    Expect(!await provider.CanHandleAsync(unknownMode), "Unknown NRG DAO track mode must be rejected instead of guessed.");

    var cueMismatch = Path.Combine(root, "cue-mismatch.nrg");
    CreateNrg(cueMismatch, version: 2,
        new TrackSpec(1, 0x01, 0x00, 2048, 4));
    Expect(!await provider.CanHandleAsync(cueMismatch), "Cue audio/data control must agree with DAO track mode.");

    var metadataOverlap = Path.Combine(root, "metadata-overlap.nrg");
    CreateNrg(metadataOverlap, version: 2,
        new[] { new TrackSpec(1, 0x41, 0x00, 2048, 4) },
        extendLastTrackIntoMetadata: true);
    Expect(!await provider.CanHandleAsync(metadataOverlap), "NRG track byte range must not overlap the metadata chunk table.");

    var foreign = Path.Combine(root, "foreign.iso");
    File.Copy(v2Path, foreign);
    Expect(!await provider.CanHandleAsync(foreign), "NRG provider must not claim foreign extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadTrackLayoutAsync(v2Path, cts.Token).AsTask(),
        "Pre-cancelled NRG parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
        new ProviderRegistration(new CueSheetImageProvider(), Priority: 70),
        new ProviderRegistration(new MdsImageProvider(), Priority: 60),
        new ProviderRegistration(provider, Priority: 50)
    ]);

    var resolution = await registry.ResolveAsync(v2Path);
    Expect(resolution.Provider is NrgImageProvider, "Provider registry should resolve NRG through the NRG provider.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.TrackLayout) == true,
        "NRG provider should advertise TrackLayout capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false,
        "NRG provider must not advertise DirectBrowse before a real filesystem/content path exists.");

    Console.WriteLine("Dragon DiskForge NRG provider smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateNrg(string path, int version, params TrackSpec[] tracks)
    => CreateNrg(path, version, tracks, includeEndChunk: true);

static void CreateNrg(
    string path,
    int version,
    TrackSpec[] tracks,
    bool includeEndChunk = true,
    int? omittedCueTrack = null,
    bool extendLastTrackIntoMetadata = false)
{
    if (version is not (1 or 2))
        throw new ArgumentOutOfRangeException(nameof(version));
    if (tracks.Length == 0)
        throw new ArgumentException("At least one track is required.", nameof(tracks));

    using var stream = new MemoryStream();
    var starts = new List<long>(tracks.Length);

    foreach (var track in tracks)
    {
        starts.Add(stream.Position);
        stream.SetLength(checked(stream.Length + ((long)track.SectorSize * track.SectorCount)));
        stream.Position = stream.Length;
    }

    var chunkOffset = stream.Position;

    using var chunks = new MemoryStream();
    Span<byte> sinf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(sinf, checked((uint)tracks.Length));
    WriteChunk(chunks, "SINF", sinf);

    using (var cue = new MemoryStream())
    {
        long lba = 0;
        foreach (var track in tracks)
        {
            if (omittedCueTrack != track.Number)
            {
                if (version == 2)
                {
                    WriteCueXEntry(cue, track.CueType, ToBcd(track.Number), 0, checked((int)lba));
                    WriteCueXEntry(cue, track.CueType, ToBcd(track.Number), 1, checked((int)lba));
                }
                else
                {
                    WriteCueSEntry(cue, track.CueType, ToBcd(track.Number), 0, checked((int)lba));
                    WriteCueSEntry(cue, track.CueType, ToBcd(track.Number), 1, checked((int)lba));
                }
            }

            lba = checked(lba + track.SectorCount);
        }

        if (version == 2)
            WriteCueXEntry(cue, 0x41, 0xAA, 1, checked((int)lba));
        else
            WriteCueSEntry(cue, 0x41, 0xAA, 1, checked((int)lba));

        WriteChunk(chunks, version == 2 ? "CUEX" : "CUES", cue.ToArray());
    }

    using (var dao = new MemoryStream())
    {
        var common = new byte[22];
        common[20] = checked((byte)tracks.Min(track => track.Number));
        common[21] = checked((byte)tracks.Max(track => track.Number));
        dao.Write(common);

        for (var index = 0; index < tracks.Length; index++)
        {
            var track = tracks[index];
            var entrySize = version == 2 ? 42 : 30;
            var entry = new byte[entrySize];
            BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(12, 2), checked((ushort)track.SectorSize));
            BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(14, 2), checked((ushort)(track.ModeCode << 8)));
            BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(16, 2), 1);

            var start = starts[index];
            var end = index + 1 < starts.Count ? starts[index + 1] : chunkOffset;
            if (extendLastTrackIntoMetadata && index == tracks.Length - 1)
                end = checked(chunkOffset + track.SectorSize);

            if (version == 2)
            {
                BinaryPrimitives.WriteUInt64BigEndian(entry.AsSpan(18, 8), checked((ulong)start));
                BinaryPrimitives.WriteUInt64BigEndian(entry.AsSpan(26, 8), checked((ulong)start));
                BinaryPrimitives.WriteUInt64BigEndian(entry.AsSpan(34, 8), checked((ulong)end));
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(18, 4), checked((uint)start));
                BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(22, 4), checked((uint)start));
                BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(26, 4), checked((uint)end));
            }

            dao.Write(entry);
        }

        WriteChunk(chunks, version == 2 ? "DAOX" : "DAOI", dao.ToArray());
    }

    if (includeEndChunk)
        WriteChunk(chunks, "END!", ReadOnlySpan<byte>.Empty);

    chunks.Position = 0;
    chunks.CopyTo(stream);

    if (version == 2)
    {
        stream.Write("NER5"u8);
        Span<byte> offset = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(offset, checked((ulong)chunkOffset));
        stream.Write(offset);
    }
    else
    {
        stream.Write("NERO"u8);
        Span<byte> offset = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(offset, checked((uint)chunkOffset));
        stream.Write(offset);
    }

    File.WriteAllBytes(path, stream.ToArray());
}

static void WriteChunk(Stream stream, string id, ReadOnlySpan<byte> payload)
{
    if (id.Length != 4)
        throw new ArgumentException("NRG chunk ID must have four characters.", nameof(id));

    stream.Write(System.Text.Encoding.ASCII.GetBytes(id));
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)payload.Length));
    stream.Write(size);
    stream.Write(payload);
}

static void WriteCueXEntry(Stream stream, byte type, byte track, byte index, int lba)
{
    Span<byte> entry = stackalloc byte[8];
    entry[0] = type;
    entry[1] = track;
    entry[2] = ToBcd(index);
    entry[3] = 0;
    BinaryPrimitives.WriteInt32BigEndian(entry.Slice(4, 4), lba);
    stream.Write(entry);
}

static void WriteCueSEntry(Stream stream, byte type, byte track, byte index, int lba)
{
    var absoluteFrames = checked(lba + 150);
    if (absoluteFrames < 0)
        throw new ArgumentOutOfRangeException(nameof(lba));

    var minute = absoluteFrames / (60 * 75);
    var remainder = absoluteFrames % (60 * 75);
    var second = remainder / 75;
    var frame = remainder % 75;
    if (minute > 99)
        throw new ArgumentOutOfRangeException(nameof(lba), "Synthetic CUES fixture exceeds two-digit BCD minutes.");

    Span<byte> entry = stackalloc byte[8];
    entry[0] = type;
    entry[1] = track;
    entry[2] = ToBcd(index);
    entry[3] = 0;
    entry[4] = 0;
    entry[5] = ToBcd(minute);
    entry[6] = ToBcd(second);
    entry[7] = ToBcd(frame);
    stream.Write(entry);
}

static byte ToBcd(int value)
{
    if (value is < 0 or > 99)
        throw new ArgumentOutOfRangeException(nameof(value));
    return checked((byte)(((value / 10) << 4) | (value % 10)));
}

static async Task ExpectCanceledAsync(Func<Task> action, string message)
{
    try
    {
        await action();
        throw new InvalidOperationException(message);
    }
    catch (OperationCanceledException)
    {
    }
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

readonly record struct TrackSpec(
    int Number,
    byte CueType,
    byte ModeCode,
    int SectorSize,
    int SectorCount);
