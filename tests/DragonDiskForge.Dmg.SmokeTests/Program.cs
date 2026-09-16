using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-DMG-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new DmgUdifImageProvider();
    var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
              "<plist version=\"1.0\"><dict><key>resource-fork</key><dict><key>blkx</key><array>" +
              "<dict><key>Name</key><string>partition 1</string></dict>" +
              "<dict><key>Name</key><string>partition 2</string></dict>" +
              "</array></dict></dict></plist>";

    var valid = Path.Combine(root, "valid.dmg");
    CreateDmg(valid, xml);
    Expect(await provider.CanHandleAsync(valid), "Valid UDIF DMG should be recognized.");
    var metadata = await provider.ReadDmgMetadataAsync(valid);
    Expect(metadata.Version == 4 && metadata.HeaderSize == 512, "UDIF version/header size should be parsed big-endian.");
    Expect(metadata.DataForkOffset == 0 && metadata.DataForkLength == 1024, "UDIF data-fork metadata should be preserved.");
    Expect(metadata.HasXmlPlist && metadata.XmlOffset == 1024 && metadata.XmlLength == (ulong)Encoding.UTF8.GetByteCount(xml),
        "UDIF XML location should be preserved.");
    Expect(metadata.SectorCount == 2048 && metadata.VirtualSizeBytes == 2048UL * 512,
        "UDIF sector count should determine virtual size.");
    Expect(metadata.BlkxEntryCount == 2, "UDIF plist blkx entries should be counted without decoding block maps.");
    Expect(metadata.SegmentCount == 1 && metadata.SegmentNumber == 1, "Single-segment metadata should be preserved.");

    var inspect = await provider.InspectAsync(valid);
    Expect(inspect.Format == "DMG / UDIF", "Inspect should identify DMG / UDIF.");
    Expect(inspect.DetectionMethod.Contains("2 blkx entries", StringComparison.Ordinal), "Inspect should expose bounded blkx metadata.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify,
        "DMG provider must remain inspection-only in this slice.");

    var stub = Path.Combine(root, "stub.dmg");
    CreateDmg(stub, xml: null);
    Expect(await provider.CanHandleAsync(stub), "UDIF stub without XML should remain inspectable.");
    var stubMetadata = await provider.ReadDmgMetadataAsync(stub);
    Expect(!stubMetadata.HasXmlPlist && stubMetadata.BlkxEntryCount == 0, "UDIF stub must not invent XML/blkx metadata.");

    var badMagic = Path.Combine(root, "bad-magic.dmg");
    File.Copy(valid, badMagic);
    PatchTrailerU32(badMagic, 0, 0x12345678);
    Expect(!await provider.CanHandleAsync(badMagic), "Bad koly signature must be rejected.");

    var badVersion = Path.Combine(root, "bad-version.dmg");
    File.Copy(valid, badVersion);
    PatchTrailerU32(badVersion, 4, 5);
    Expect(!await provider.CanHandleAsync(badVersion), "Unproven UDIF versions must be rejected.");

    var badHeader = Path.Combine(root, "bad-header-size.dmg");
    File.Copy(valid, badHeader);
    PatchTrailerU32(badHeader, 8, 256);
    Expect(!await provider.CanHandleAsync(badHeader), "UDIF trailer size other than 512 must be rejected.");

    var dataOutOfBounds = Path.Combine(root, "data-oob.dmg");
    File.Copy(valid, dataOutOfBounds);
    PatchTrailerU64(dataOutOfBounds, 24, 100_000);
    Expect(!await provider.CanHandleAsync(dataOutOfBounds), "Data-fork range outside payload must be rejected.");

    var xmlOutOfBounds = Path.Combine(root, "xml-oob.dmg");
    File.Copy(valid, xmlOutOfBounds);
    PatchTrailerU64(xmlOutOfBounds, 216, 100_000);
    Expect(!await provider.CanHandleAsync(xmlOutOfBounds), "XML range outside payload must be rejected.");

    var segmented = Path.Combine(root, "segmented.dmg");
    File.Copy(valid, segmented);
    PatchTrailerU32(segmented, 60, 2);
    Expect(!await provider.CanHandleAsync(segmented), "Multi-segment DMG must be rejected until companion-segment support is implemented.");

    var badChecksum = Path.Combine(root, "bad-checksum-size.dmg");
    File.Copy(valid, badChecksum);
    PatchTrailerU32(badChecksum, 84, 1025);
    Expect(!await provider.CanHandleAsync(badChecksum), "Checksum metadata larger than its trailer field must be rejected.");

    var malformedXml = Path.Combine(root, "malformed-xml.dmg");
    CreateDmg(malformedXml, "<plist><dict><key>blkx</key><array>");
    Expect(!await provider.CanHandleAsync(malformedXml), "Malformed UDIF XML plist must be rejected safely.");

    var foreign = Path.Combine(root, "foreign.img");
    File.Copy(valid, foreign);
    Expect(!await provider.CanHandleAsync(foreign), "DMG provider must not claim foreign extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadDmgMetadataAsync(valid, cts.Token).AsTask(),
        "Pre-cancelled DMG parsing should propagate cancellation.");

    var registry = new ProviderRegistry([
        new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
        new ProviderRegistration(new CcdImageProvider(), Priority: 95),
        new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
        new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
        new ProviderRegistration(new CueSheetImageProvider(), Priority: 70),
        new ProviderRegistration(new MdsImageProvider(), Priority: 60),
        new ProviderRegistration(new NrgImageProvider(), Priority: 50),
        new ProviderRegistration(new VmdkSparseImageProvider(), Priority: 40),
        new ProviderRegistration(new QcowImageProvider(), Priority: 30),
        new ProviderRegistration(provider, Priority: 20)
    ]);

    var resolution = await registry.ResolveAsync(valid);
    Expect(resolution.Provider is DmgUdifImageProvider, "Provider registry should resolve a valid UDIF DMG.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.VirtualDiskMetadata) == true,
        "DMG should advertise VirtualDiskMetadata capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false,
        "DMG must not advertise DirectBrowse before blkx/data translation and filesystem support exist.");

    Console.WriteLine("Dragon DiskForge DMG / UDIF metadata smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateDmg(string path, string? xml)
{
    const int payloadSize = 4096;
    const int trailerSize = 512;
    var bytes = new byte[payloadSize + trailerSize];
    var xmlBytes = xml is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(xml);
    if (xmlBytes.Length > payloadSize - 1024)
        throw new InvalidOperationException("Synthetic plist exceeded fixture payload allocation.");
    if (xmlBytes.Length > 0)
        xmlBytes.CopyTo(bytes, 1024);

    var trailer = bytes.AsSpan(payloadSize, trailerSize);
    WriteU32(trailer, 0, 0x6B6F6C79);
    WriteU32(trailer, 4, 4);
    WriteU32(trailer, 8, 512);
    WriteU32(trailer, 12, 1);
    WriteU64(trailer, 16, 0);
    WriteU64(trailer, 24, 0);
    WriteU64(trailer, 32, 1024);
    WriteU64(trailer, 40, 0);
    WriteU64(trailer, 48, 0);
    WriteU32(trailer, 56, 1);
    WriteU32(trailer, 60, 1);
    for (var index = 0; index < 16; index++)
        trailer[64 + index] = (byte)(index + 1);
    WriteU32(trailer, 80, 2);
    WriteU32(trailer, 84, 32);
    WriteU64(trailer, 216, xmlBytes.Length == 0 ? 0UL : 1024UL);
    WriteU64(trailer, 224, (ulong)xmlBytes.Length);
    WriteU32(trailer, 352, 2);
    WriteU32(trailer, 356, 32);
    WriteU32(trailer, 488, 1);
    WriteU64(trailer, 492, 2048);

    File.WriteAllBytes(path, bytes);
}

static void PatchTrailerU32(string path, int trailerOffset, uint value)
{
    Span<byte> raw = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(raw, value);
    PatchTrailer(path, trailerOffset, raw);
}

static void PatchTrailerU64(string path, int trailerOffset, ulong value)
{
    Span<byte> raw = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(raw, value);
    PatchTrailer(path, trailerOffset, raw);
}

static void PatchTrailer(string path, int trailerOffset, ReadOnlySpan<byte> raw)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = stream.Length - 512 + trailerOffset;
    stream.Write(raw);
}

static void WriteU32(Span<byte> data, int offset, uint value)
    => BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);

static void WriteU64(Span<byte> data, int offset, ulong value)
    => BinaryPrimitives.WriteUInt64BigEndian(data.Slice(offset, 8), value);

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
