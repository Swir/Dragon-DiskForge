using System.Buffers.Binary;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-CCD-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new CcdImageProvider();

    var valid = CreateCloneCdSet(root, "mixed", withSub: true,
        new TrackSpec(1, 0, null, 0),
        new TrackSpec(2, 1, 8, 10));

    Expect(await provider.CanHandleAsync(valid.Ccd), "Valid CCD descriptor should be recognized.");
    Expect(await provider.CanHandleAsync(valid.Img), "Same-name IMG with valid CCD companion should be recognized.");
    Expect(await provider.CanHandleAsync(valid.Sub!), "Validated same-name SUB sidecar should be recognized.");

    var layout = await provider.ReadTrackLayoutAsync(valid.Ccd);
    Expect(layout.TrackCount == 2, "CCD should expose two tracks.");
    Expect(layout.AudioTrackCount == 1 && layout.DataTrackCount == 1, "CCD MODE values should classify audio/data correctly.");
    Expect(layout.Tracks[0].Mode == "AUDIO" && layout.Tracks[0].SectorSize == 2352, "CCD MODE=0 should map to AUDIO/2352.");
    Expect(layout.Tracks[0].StartSector == 0 && layout.Tracks[0].SectorCount == 10, "First CCD track range should use INDEX 1.");
    Expect(layout.Tracks[1].Mode == "MODE1/2352" && layout.Tracks[1].StartSector == 10, "CCD MODE=1 and INDEX 1 should be preserved.");
    Expect(layout.Tracks[1].Index00Frames == 8 && layout.Tracks[1].Index01Frames == 10, "CCD INDEX 0/1 metadata should be preserved.");
    Expect(layout.Tracks[1].SectorCount == 20 && layout.Tracks[1].StartByte == 10L * 2352, "CCD IMG byte ranges should use 2352-byte raw sectors.");
    Expect(layout.DataFiles.Count == 1 && PathsEqual(layout.DataFiles[0], valid.Img), "CCD layout should expose the IMG payload as its data file.");

    var inspect = await provider.InspectAsync(valid.Ccd);
    Expect(inspect.Format == "CCD/IMG/SUB", "Inspect should report CCD/IMG/SUB.");
    Expect(inspect.Description.Contains("validated SUB sidecar", StringComparison.Ordinal), "Inspect should report a validated SUB sidecar.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify,
        "CCD provider must remain inspection-only in this slice.");

    var noSub = CreateCloneCdSet(root, "no-sub", withSub: false,
        new TrackSpec(1, 2, null, 0));
    Expect(await provider.CanHandleAsync(noSub.Ccd), "CCD/IMG should remain valid when optional SUB is absent.");
    var noSubInspect = await provider.InspectAsync(noSub.Ccd);
    Expect(noSubInspect.Description.Contains("no SUB sidecar", StringComparison.Ordinal), "Inspect should distinguish absent optional SUB.");

    var badSub = CreateCloneCdSet(root, "bad-sub", withSub: true,
        new TrackSpec(1, 1, null, 0));
    using (var stream = new FileStream(badSub.Sub!, FileMode.Open, FileAccess.Write, FileShare.None))
        stream.SetLength(stream.Length - 1);
    Expect(!await provider.CanHandleAsync(badSub.Ccd), "SUB length not equal to 96 bytes per IMG sector must be rejected.");
    Expect(!await provider.CanHandleAsync(badSub.Sub!), "Invalid SUB sidecar must not be claimed directly.");

    var missingImgCcd = Path.Combine(root, "missing-img.ccd");
    WriteCcd(missingImgCcd, version: 3,
        new TrackSpec(1, 1, null, 0));
    Expect(!await provider.CanHandleAsync(missingImgCcd), "CCD with missing same-name IMG must be rejected.");

    var badAlignment = CreateCloneCdSet(root, "bad-alignment", withSub: false,
        new TrackSpec(1, 1, null, 0));
    using (var stream = new FileStream(badAlignment.Img, FileMode.Open, FileAccess.Write, FileShare.None))
        stream.SetLength(stream.Length - 1);
    Expect(!await provider.CanHandleAsync(badAlignment.Ccd), "IMG payload not aligned to 2352-byte sectors must be rejected.");

    var unknownMode = CreateCloneCdSet(root, "unknown-mode", withSub: false,
        new TrackSpec(1, 9, null, 0));
    Expect(!await provider.CanHandleAsync(unknownMode.Ccd), "Unknown CCD MODE must be rejected instead of guessed.");

    var missingIndex = CreateCloneCdSet(root, "missing-index", withSub: false,
        new TrackSpec(1, 1, null, null));
    Expect(!await provider.CanHandleAsync(missingIndex.Ccd), "CCD track without INDEX 1 must be rejected.");

    var duplicateIndex = CreateCloneCdSet(root, "duplicate-index", withSub: false,
        new TrackSpec(1, 1, null, 0));
    File.AppendAllText(duplicateIndex.Ccd, "INDEX 1=1\n");
    Expect(!await provider.CanHandleAsync(duplicateIndex.Ccd), "Duplicate CCD INDEX 1 must be rejected.");

    var nonIncreasing = CreateCloneCdSet(root, "non-increasing", withSub: false,
        new TrackSpec(1, 1, null, 5),
        new TrackSpec(2, 1, null, 5));
    Expect(!await provider.CanHandleAsync(nonIncreasing.Ccd), "CCD INDEX 1 positions must increase across tracks.");

    var badIndexOrder = CreateCloneCdSet(root, "bad-index-order", withSub: false,
        new TrackSpec(1, 1, 6, 5));
    Expect(!await provider.CanHandleAsync(badIndexOrder.Ccd), "CCD INDEX 0 after INDEX 1 must be rejected.");

    var futureVersion = CreateCloneCdSet(root, "future-version", withSub: false, version: 4,
        new TrackSpec(1, 1, null, 0));
    Expect(!await provider.CanHandleAsync(futureVersion.Ccd), "Unvalidated future CloneCD descriptor versions must be rejected.");

    var foreign = Path.Combine(root, "foreign.iso");
    File.Copy(valid.Ccd, foreign);
    Expect(!await provider.CanHandleAsync(foreign), "CCD provider must not claim foreign extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadTrackLayoutAsync(valid.Ccd, cts.Token).AsTask(),
        "Pre-cancelled CCD parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(provider, Priority: 95),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
        new ProviderRegistration(new CueSheetImageProvider(), Priority: 70),
        new ProviderRegistration(new MdsImageProvider(), Priority: 60),
        new ProviderRegistration(new NrgImageProvider(), Priority: 50)
    ]);

    var ccdResolution = await registry.ResolveAsync(valid.Img);
    Expect(ccdResolution.Provider is CcdImageProvider, "Valid same-name CCD companion should take precedence for ambiguous .img inputs.");
    Expect(ccdResolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.TrackLayout) == true,
        "CCD provider should advertise TrackLayout capability.");
    Expect(ccdResolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false,
        "CCD provider must not advertise DirectBrowse before a real content path exists.");

    var rawImg = Path.Combine(root, "raw-without-ccd.img");
    CreateRawMbr(rawImg);
    var rawResolution = await registry.ResolveAsync(rawImg);
    Expect(rawResolution.Provider is RawPartitionImageProvider,
        "An .img without a valid same-name CCD descriptor must fall through to the RAW provider when its partition table is valid.");

    Console.WriteLine("Dragon DiskForge CCD/IMG/SUB provider smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static CloneCdSet CreateCloneCdSet(
    string root,
    string name,
    bool withSub,
    params TrackSpec[] tracks)
    => CreateCloneCdSet(root, name, withSub, version: 3, tracks);

static CloneCdSet CreateCloneCdSet(
    string root,
    string name,
    bool withSub,
    int version,
    params TrackSpec[] tracks)
{
    var ccd = Path.Combine(root, name + ".ccd");
    var img = Path.Combine(root, name + ".img");
    var sub = withSub ? Path.Combine(root, name + ".sub") : null;

    var lastIndex = tracks.Where(track => track.Index1 is not null).Select(track => track.Index1!.Value).DefaultIfEmpty(0).Max();
    var totalSectors = Math.Max(30, lastIndex + 20);
    using (var stream = new FileStream(img, FileMode.Create, FileAccess.Write, FileShare.None))
        stream.SetLength(checked((long)totalSectors * 2352));

    if (sub is not null)
    {
        using var stream = new FileStream(sub, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.SetLength(checked((long)totalSectors * 96));
    }

    WriteCcd(ccd, version, tracks);
    return new CloneCdSet(ccd, img, sub);
}

static void WriteCcd(string path, int version, params TrackSpec[] tracks)
{
    using var writer = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(false));
    writer.WriteLine("[CloneCD]");
    writer.WriteLine($"Version={version}");
    writer.WriteLine();
    writer.WriteLine("[Disc]");
    writer.WriteLine($"TocEntries={tracks.Length + 3}");
    writer.WriteLine("Sessions=1");
    writer.WriteLine("DataTracksScrambled=0");
    writer.WriteLine("CDTextLength=0");
    writer.WriteLine();
    writer.WriteLine("[Session 1]");
    writer.WriteLine("PreGapMode=0");
    writer.WriteLine("PreGapSubC=0");
    writer.WriteLine();
    writer.WriteLine("[Entry 0]");
    writer.WriteLine("Session=1");
    writer.WriteLine("Point=0xa0");
    writer.WriteLine("ADR=0x01");
    writer.WriteLine("Control=0x00");
    writer.WriteLine("TrackNo=0");
    writer.WriteLine("AMin=0");
    writer.WriteLine("ASec=0");
    writer.WriteLine("AFrame=0");
    writer.WriteLine("ALBA=-150");
    writer.WriteLine("Zero=0");
    writer.WriteLine("PMin=1");
    writer.WriteLine("PSec=0");
    writer.WriteLine("PFrame=0");
    writer.WriteLine("PLBA=4350");
    writer.WriteLine();

    foreach (var track in tracks)
    {
        writer.WriteLine($"[TRACK {track.Number}]");
        writer.WriteLine($"MODE={track.Mode}");
        if (track.Index0 is not null)
            writer.WriteLine($"INDEX 0={track.Index0.Value}");
        if (track.Index1 is not null)
            writer.WriteLine($"INDEX 1={track.Index1.Value}");
        writer.WriteLine();
    }
}

static void CreateRawMbr(string path)
{
    var bytes = new byte[1024 * 1024];
    var entry = bytes.AsSpan(446, 16);
    entry[4] = 0x0C;
    BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12, 4), 10);
    bytes[510] = 0x55;
    bytes[511] = 0xAA;
    File.WriteAllBytes(path, bytes);
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

static bool PathsEqual(string left, string right)
    => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

readonly record struct TrackSpec(int Number, int Mode, int? Index0, int? Index1);
readonly record struct CloneCdSet(string Ccd, string Img, string? Sub);
