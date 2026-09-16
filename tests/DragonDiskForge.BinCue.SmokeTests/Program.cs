using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-BinCue-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new CueSheetImageProvider();

    var binPath = Path.Combine(root, "disc.bin");
    CreateSizedFile(binPath, 300L * 2352);
    var cuePath = Path.Combine(root, "disc.cue");
    await File.WriteAllTextAsync(cuePath, """
        FILE "disc.bin" BINARY
          TRACK 01 MODE1/2352
            INDEX 01 00:00:00
          TRACK 02 AUDIO
            INDEX 00 00:02:00
            INDEX 01 00:02:10
        """);

    Expect(await provider.CanHandleAsync(cuePath), "Valid mixed-mode CUE should be recognized.");
    Expect(await provider.CanHandleAsync(binPath), "Companion BIN should resolve through its same-name CUE.");

    var layout = await provider.ReadTrackLayoutAsync(cuePath);
    Expect(layout.TrackCount == 2, "CUE should expose two tracks.");
    Expect(layout.DataTrackCount == 1 && layout.AudioTrackCount == 1, "Data/audio track counts should be reported.");
    Expect(layout.DataFiles.Count == 1, "Single BIN CUE should expose one data file.");
    Expect(layout.Tracks[0].Mode == "MODE1/2352", "Track 1 mode should be preserved.");
    Expect(layout.Tracks[0].SectorSize == 2352, "MODE1/2352 sector size should be decoded.");
    Expect(layout.Tracks[0].SectorCount == 160, "Track 1 sector count should end at track 2 INDEX 01.");
    Expect(layout.Tracks[1].IsAudio, "Track 2 should be audio.");
    Expect(layout.Tracks[1].Index00Frames == 150 && layout.Tracks[1].Index01Frames == 160, "INDEX 00/01 should be preserved.");
    Expect(layout.Tracks[1].SectorCount == 140, "Final track should extend to the end of the BIN.");

    var inspect = await provider.InspectAsync(cuePath);
    Expect(inspect.Format == "BIN/CUE", "Inspect should report BIN/CUE.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify, "BIN/CUE provider must not fake browse/mount/convert.");

    var dataBin = Path.Combine(root, "data.bin");
    var audioBin = Path.Combine(root, "audio.bin");
    CreateSizedFile(dataBin, 100L * 2048);
    CreateSizedFile(audioBin, 120L * 2352);
    var multiCue = Path.Combine(root, "multi.cue");
    await File.WriteAllTextAsync(multiCue, """
        FILE "data.bin" BINARY
          TRACK 01 MODE1/2048
            INDEX 01 00:00:00
        FILE "audio.bin" BINARY
          TRACK 02 AUDIO
            INDEX 01 00:00:00
        """);

    var multi = await provider.ReadTrackLayoutAsync(multiCue);
    Expect(multi.IsMultiFile && multi.DataFiles.Count == 2, "Multi-file CUE should expose both BIN payloads.");
    Expect(multi.Tracks[0].SectorSize == 2048 && multi.Tracks[0].SectorCount == 100, "Cooked data track bounds should be verified.");
    Expect(multi.Tracks[1].SectorSize == 2352 && multi.Tracks[1].SectorCount == 120, "Audio track bounds should be verified.");

    var missingCue = Path.Combine(root, "missing.cue");
    await File.WriteAllTextAsync(missingCue, "FILE \"missing.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");
    Expect(!await provider.CanHandleAsync(missingCue), "CUE with a missing BIN must be rejected.");

    var outsideDir = Path.Combine(Path.GetTempPath(), "DragonDiskForge-BinCue-Outside-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outsideDir);
    try
    {
        var outsideBin = Path.Combine(outsideDir, "outside.bin");
        CreateSizedFile(outsideBin, 20L * 2352);
        var traversalCue = Path.Combine(root, "traversal.cue");
        var relative = Path.GetRelativePath(root, outsideBin).Replace('\\', '/');
        await File.WriteAllTextAsync(traversalCue, $"FILE \"{relative}\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");
        Expect(!await provider.CanHandleAsync(traversalCue), "CUE FILE traversal outside the cue directory must be rejected.");
    }
    finally
    {
        try { Directory.Delete(outsideDir, recursive: true); } catch { }
    }

    var mixedBin = Path.Combine(root, "mixed.bin");
    CreateSizedFile(mixedBin, 400L * 2352);
    var mixedCue = Path.Combine(root, "mixed.cue");
    await File.WriteAllTextAsync(mixedCue, """
        FILE "mixed.bin" BINARY
          TRACK 01 MODE1/2048
            INDEX 01 00:00:00
          TRACK 02 AUDIO
            INDEX 01 00:02:00
        """);
    Expect(!await provider.CanHandleAsync(mixedCue), "Mixed sector sizes in one BIN must be rejected instead of guessing byte offsets.");

    var badIndexCue = Path.Combine(root, "badindex.cue");
    await File.WriteAllTextAsync(badIndexCue, """
        FILE "disc.bin" BINARY
          TRACK 01 MODE1/2352
            INDEX 01 00:02:00
          TRACK 02 AUDIO
            INDEX 01 00:01:00
        """);
    Expect(!await provider.CanHandleAsync(badIndexCue), "Track INDEX 01 positions must increase within a shared BIN.");

    var unsupportedTypeCue = Path.Combine(root, "wave.cue");
    await File.WriteAllTextAsync(unsupportedTypeCue, "FILE \"disc.bin\" WAVE\n  TRACK 01 AUDIO\n    INDEX 01 00:00:00\n");
    Expect(!await provider.CanHandleAsync(unsupportedTypeCue), "Unsupported FILE type must be rejected.");

    var foreignPath = Path.Combine(root, "disc.dat");
    File.Copy(cuePath, foreignPath);
    Expect(!await provider.CanHandleAsync(foreignPath), "BIN/CUE provider must not claim foreign extensions.");

    var orphanBin = Path.Combine(root, "orphan.bin");
    CreateSizedFile(orphanBin, 10L * 2352);
    Expect(!await provider.CanHandleAsync(orphanBin), "A standalone BIN without same-name CUE must not be claimed.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadTrackLayoutAsync(cuePath, cts.Token).AsTask(),
        "Pre-cancelled CUE parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
        new ProviderRegistration(provider, Priority: 70)
    ]);

    var cueResolution = await registry.ResolveAsync(cuePath);
    Expect(cueResolution.Provider is CueSheetImageProvider, "Registry should resolve CUE through the BIN/CUE provider.");
    Expect(cueResolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.TrackLayout) == true, "BIN/CUE provider should report TrackLayout capability.");
    Expect(cueResolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false, "BIN/CUE provider must not advertise DirectBrowse.");

    var binResolution = await registry.ResolveAsync(binPath);
    Expect(binResolution.Provider is CueSheetImageProvider, "Registry should resolve companion BIN through its CUE.");

    Console.WriteLine("Dragon DiskForge BIN/CUE provider smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateSizedFile(string path, long length)
{
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    stream.SetLength(length);
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
