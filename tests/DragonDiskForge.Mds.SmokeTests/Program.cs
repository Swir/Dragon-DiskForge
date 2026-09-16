using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-MDS-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new MdsImageProvider();

    var validMds = Path.Combine(root, "mixed.mds");
    var validMdf = Path.Combine(root, "mixed.mdf");
    CreatePayload(validMdf, (2352L * 10) + (2048L * 20));
    CreateMds(validMds,
        new TrackSpec(1, 0xA9, 0x00, 2352, 0, 0),
        new TrackSpec(2, 0xAA, 0x04, 2048, 10, 2352L * 10));

    Expect(await provider.CanHandleAsync(validMds), "Valid MDS should be recognized.");
    Expect(await provider.CanHandleAsync(validMdf), "Same-name MDF with valid MDS companion should be recognized.");

    var layout = await provider.ReadTrackLayoutAsync(validMds);
    Expect(layout.TrackCount == 2, "Two MDS tracks should be parsed.");
    Expect(layout.AudioTrackCount == 1 && layout.DataTrackCount == 1, "Audio/data control metadata should be preserved.");
    Expect(layout.DataFiles.Count == 1, "Both tracks should reference one MDF payload.");
    Expect(layout.Tracks[0].SectorSize == 2352 && layout.Tracks[0].SectorCount == 10, "First track byte range should use explicit start offsets.");
    Expect(layout.Tracks[1].SectorSize == 2048 && layout.Tracks[1].SectorCount == 20, "Second mixed-sector track should resolve without guessed offsets.");
    Expect(layout.Tracks[1].StartByte == 2352L * 10, "Second track byte offset should match MDS metadata.");

    var inspect = await provider.InspectAsync(validMds);
    Expect(inspect.Format == "MDF/MDS", "Inspect should report MDF/MDS.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify,
        "MDF/MDS provider must remain inspection-only in this slice.");

    var badSignature = Path.Combine(root, "bad-signature.mds");
    File.Copy(validMds, badSignature);
    using (var stream = new FileStream(badSignature, FileMode.Open, FileAccess.Write, FileShare.None))
    {
        stream.Position = 0;
        stream.Write(Encoding.ASCII.GetBytes("NOT A DESCRIPTOR"));
    }
    Expect(!await provider.CanHandleAsync(badSignature), "Invalid MDS signature must be rejected.");

    var badOffset = Path.Combine(root, "bad-offset.mds");
    File.Copy(validMds, badOffset);
    using (var stream = new FileStream(badOffset, FileMode.Open, FileAccess.Write, FileShare.None))
    {
        stream.Position = 80;
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(value, 0x7FFF_FFF0);
        stream.Write(value);
    }
    Expect(!await provider.CanHandleAsync(badOffset), "Out-of-range MDS session table must be rejected.");

    var missingMds = Path.Combine(root, "missing.mds");
    CreateMds(missingMds, new TrackSpec(1, 0xAA, 0x04, 2048, 0, 0));
    Expect(!await provider.CanHandleAsync(missingMds), "MDS with missing companion MDF must be rejected.");

    var unsupportedSectorMds = Path.Combine(root, "unsupported-sector.mds");
    var unsupportedSectorMdf = Path.Combine(root, "unsupported-sector.mdf");
    CreatePayload(unsupportedSectorMdf, 4096L * 4);
    CreateMds(unsupportedSectorMds, new TrackSpec(1, 0xAA, 0x04, 4096, 0, 0));
    Expect(!await provider.CanHandleAsync(unsupportedSectorMds), "Unsupported MDS sector sizes must be rejected.");

    var traversalMds = Path.Combine(root, "traversal.mds");
    var traversalPayload = Path.Combine(root, "traversal.mdf");
    CreatePayload(traversalPayload, 2352L * 4);
    CreateMdsWithFooter(traversalMds, "../outside.mdf", false,
        new TrackSpec(1, 0xA9, 0x00, 2352, 0, 0));
    Expect(!await provider.CanHandleAsync(traversalMds), "MDS footer path traversal must be rejected.");

    var wildcardMds = Path.Combine(root, "wildcard.mds");
    var wildcardMdf = Path.Combine(root, "wildcard.mdf");
    CreatePayload(wildcardMdf, 2352L * 5);
    CreateMdsWithFooter(wildcardMds, "*.mdf", false,
        new TrackSpec(1, 0xA9, 0x00, 2352, 0, 0));
    Expect(await provider.CanHandleAsync(wildcardMds), "MDS wildcard footer should resolve the same-name MDF payload.");

    var wideMds = Path.Combine(root, "wide.mds");
    var wideMdf = Path.Combine(root, "wide.mdf");
    CreatePayload(wideMdf, 2352L * 3);
    CreateMdsWithFooter(wideMds, "*.mdf", true,
        new TrackSpec(1, 0xA9, 0x00, 2352, 0, 0));
    Expect(await provider.CanHandleAsync(wideMds), "UTF-16 MDS wildcard footer should resolve safely.");

    var foreign = Path.Combine(root, "foreign.iso");
    File.Copy(validMds, foreign);
    Expect(!await provider.CanHandleAsync(foreign), "MDS provider must not claim foreign extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadTrackLayoutAsync(validMds, cts.Token).AsTask(),
        "Pre-cancelled MDS parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
        new ProviderRegistration(new CueSheetImageProvider(), Priority: 70),
        new ProviderRegistration(provider, Priority: 60)
    ]);

    var resolution = await registry.ResolveAsync(validMds);
    Expect(resolution.Provider is MdsImageProvider, "Provider registry should resolve MDS through the MDF/MDS provider.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.TrackLayout) == true,
        "MDF/MDS provider should advertise TrackLayout capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false,
        "MDF/MDS provider must not advertise DirectBrowse before filesystem support exists.");

    Console.WriteLine("Dragon DiskForge MDF/MDS provider smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreatePayload(string path, long length)
{
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    stream.SetLength(length);
}

static void CreateMds(string path, params TrackSpec[] tracks)
    => CreateMdsCore(path, tracks, footerName: null, wideFooter: false);

static void CreateMdsWithFooter(string path, string footerName, bool wide, params TrackSpec[] tracks)
    => CreateMdsCore(path, tracks, footerName, wide);

static void CreateMdsCore(string path, TrackSpec[] tracks, string? footerName, bool wideFooter)
{
    const int headerSize = 92;
    const int sessionSize = 24;
    const int trackSize = 80;
    const int footerSize = 16;

    if (tracks.Length == 0)
        throw new ArgumentException("At least one track is required.", nameof(tracks));

    var sessionOffset = headerSize;
    var trackOffset = sessionOffset + sessionSize;
    var footerOffset = footerName is null ? 0 : trackOffset + (trackSize * tracks.Length);
    var filenameOffset = footerName is null ? 0 : footerOffset + footerSize;
    var filenameBytes = footerName is null
        ? Array.Empty<byte>()
        : wideFooter
            ? Encoding.Unicode.GetBytes(footerName + "\0")
            : Encoding.ASCII.GetBytes(footerName + "\0");

    var totalLength = footerName is null
        ? trackOffset + (trackSize * tracks.Length)
        : filenameOffset + filenameBytes.Length;

    var bytes = new byte[totalLength];
    Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR").CopyTo(bytes, 0);
    bytes[16] = 1;
    bytes[17] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18, 2), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20, 2), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80, 4), (uint)sessionOffset);

    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(sessionOffset + 0, 4), 0);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(sessionOffset + 4, 4), 1000);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(sessionOffset + 8, 2), 1);
    bytes[sessionOffset + 10] = checked((byte)tracks.Length);
    bytes[sessionOffset + 11] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(sessionOffset + 12, 2), (ushort)tracks.Min(track => track.Number));
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(sessionOffset + 14, 2), (ushort)tracks.Max(track => track.Number));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(sessionOffset + 20, 4), (uint)trackOffset);

    for (var i = 0; i < tracks.Length; i++)
    {
        var track = tracks[i];
        var offset = trackOffset + (i * trackSize);
        bytes[offset + 0] = track.Mode;
        bytes[offset + 2] = track.AddressControl;
        bytes[offset + 4] = checked((byte)track.Number);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 12, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 16, 2), checked((ushort)track.SectorSize));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 36, 4), checked((uint)track.StartSector));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset + 40, 8), checked((ulong)track.StartByte));
        bytes[offset + 48] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 52, 4), checked((uint)footerOffset));
    }

    if (footerName is not null)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(footerOffset + 0, 4), checked((uint)filenameOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(footerOffset + 4, 4), wideFooter ? 1u : 0u);
        filenameBytes.CopyTo(bytes, filenameOffset);
    }

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

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

readonly record struct TrackSpec(
    int Number,
    byte Mode,
    byte AddressControl,
    int SectorSize,
    int StartSector,
    long StartByte);
