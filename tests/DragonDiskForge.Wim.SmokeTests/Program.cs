using System.Buffers.Binary;
using DragonDiskForge.Core.Providers;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-WIM-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new WimEsdImageProvider();

    var wim = Path.Combine(root, "valid.wim");
    CreateWim(wim, version: 68864, flags: 0x00040002, chunkSize: 32768);
    Expect(await provider.CanHandleAsync(wim), "Valid standalone WIM should be recognized.");
    var wimMetadata = await provider.ReadWimMetadataAsync(wim);
    Expect(wimMetadata.HeaderSize == 208 && wimMetadata.Version == 68864, "Standard WIM header/version should be parsed.");
    Expect(wimMetadata.PartNumber == 1 && wimMetadata.TotalParts == 1 && wimMetadata.ImageCount == 2,
        "Standalone part/image metadata should be preserved.");
    Expect(wimMetadata.BootIndex == 1, "Boot index should be preserved.");
    Expect(wimMetadata.LookupTable.IsPresent && wimMetadata.XmlData.IsPresent && wimMetadata.IntegrityTable.IsPresent,
        "Header resource descriptors should be exposed.");
    Expect(!wimMetadata.IsSolidVersion && wimMetadata.ContainerFlavor == "WIM", "Standard version should be classified as WIM.");

    var esd = Path.Combine(root, "valid.esd");
    CreateWim(esd, version: 3584, flags: 0x00080002, chunkSize: 131072);
    Expect(await provider.CanHandleAsync(esd), "Valid standalone solid-version ESD header should be recognized.");
    var esdMetadata = await provider.ReadWimMetadataAsync(esd);
    Expect(esdMetadata.IsSolidVersion && esdMetadata.ContainerFlavor == "ESD / solid WIM", "Version 3584 should be classified as ESD/solid WIM.");

    var inspect = await provider.InspectAsync(esd);
    Expect(inspect.Format == "ESD / solid WIM", "Inspect should identify the solid WIM/ESD flavor.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify,
        "WIM/ESD provider must remain metadata-only in this slice.");

    var badMagic = Path.Combine(root, "bad-magic.wim");
    File.Copy(wim, badMagic);
    PatchByte(badMagic, 0, (byte)'X');
    Expect(!await provider.CanHandleAsync(badMagic), "Bad WIM magic must be rejected.");

    var badHeader = Path.Combine(root, "bad-header.wim");
    File.Copy(wim, badHeader);
    PatchU32(badHeader, 8, 212);
    Expect(!await provider.CanHandleAsync(badHeader), "Header size other than 208 must be rejected.");

    var badVersion = Path.Combine(root, "bad-version.wim");
    File.Copy(wim, badVersion);
    PatchU32(badVersion, 12, 1234);
    Expect(!await provider.CanHandleAsync(badVersion), "Unknown WIM versions must be rejected.");

    var split = Path.Combine(root, "split.wim");
    File.Copy(wim, split);
    PatchU16(split, 42, 2);
    Expect(!await provider.CanHandleAsync(split), "Split WIM must be rejected until companion-part support exists.");

    var spanned = Path.Combine(root, "spanned.wim");
    File.Copy(wim, spanned);
    PatchU32(spanned, 16, 0x0004000A);
    Expect(!await provider.CanHandleAsync(spanned), "Spanned WIM header flag must be rejected.");

    var writing = Path.Combine(root, "writing.wim");
    File.Copy(wim, writing);
    PatchU32(writing, 16, 0x00040042);
    Expect(!await provider.CanHandleAsync(writing), "WRITE_IN_PROGRESS WIM must be rejected.");

    var badBoot = Path.Combine(root, "bad-boot.wim");
    File.Copy(wim, badBoot);
    PatchU32(badBoot, 120, 3);
    Expect(!await provider.CanHandleAsync(badBoot), "Boot index beyond image count must be rejected.");

    var lookupOob = Path.Combine(root, "lookup-oob.wim");
    File.Copy(wim, lookupOob);
    PatchU64(lookupOob, 56, 100_000);
    Expect(!await provider.CanHandleAsync(lookupOob), "Lookup-table resource outside the file must be rejected.");

    var unknownResFlag = Path.Combine(root, "unknown-resource-flag.wim");
    File.Copy(wim, unknownResFlag);
    PatchU64(unknownResFlag, 48, (0x80UL << 56) | 64UL);
    Expect(!await provider.CanHandleAsync(unknownResFlag), "Unknown WIM resource flags must be rejected.");

    var badChunk = Path.Combine(root, "bad-chunk.wim");
    File.Copy(wim, badChunk);
    PatchU32(badChunk, 20, 30000);
    Expect(!await provider.CanHandleAsync(badChunk), "Non-power-of-two compressed chunk size must be rejected.");

    var foreign = Path.Combine(root, "foreign.bin");
    File.Copy(wim, foreign);
    Expect(!await provider.CanHandleAsync(foreign), "WIM provider must not claim foreign extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadWimMetadataAsync(wim, cts.Token).AsTask(),
        "Pre-cancelled WIM parsing should propagate cancellation.");

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
        new ProviderRegistration(new DmgUdifImageProvider(), Priority: 20),
        new ProviderRegistration(provider, Priority: 10)
    ]);

    var resolution = await registry.ResolveAsync(wim);
    Expect(resolution.Provider is WimEsdImageProvider, "Provider registry should resolve valid WIM metadata.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.ContainerMetadata) == true,
        "WIM should advertise ContainerMetadata capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.VirtualDiskMetadata) == false,
        "WIM must not be mislabeled as VirtualDiskMetadata.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false,
        "WIM must not advertise DirectBrowse before resource decompression/file-tree support exists.");

    Console.WriteLine("Dragon DiskForge WIM / ESD metadata smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateWim(string path, uint version, uint flags, uint chunkSize)
{
    var bytes = new byte[1024];
    var h = bytes.AsSpan(0, 208);
    new byte[] { (byte)'M', (byte)'S', (byte)'W', (byte)'I', (byte)'M', 0, 0, 0 }.CopyTo(h);
    WriteU32(h, 8, 208);
    WriteU32(h, 12, version);
    WriteU32(h, 16, flags);
    WriteU32(h, 20, chunkSize);
    var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff").ToByteArray();
    guid.CopyTo(h.Slice(24, 16));
    WriteU16(h, 40, 1);
    WriteU16(h, 42, 1);
    WriteU32(h, 44, 2);
    WriteResource(h, 48, flags: 0x02, storedSize: 64, offset: 512, originalSize: 64);
    WriteResource(h, 72, flags: 0x00, storedSize: 128, offset: 576, originalSize: 128);
    WriteResource(h, 96, flags: 0x02, storedSize: 64, offset: 704, originalSize: 64);
    WriteU32(h, 120, 1);
    WriteResource(h, 124, flags: 0x00, storedSize: 64, offset: 768, originalSize: 64);
    File.WriteAllBytes(path, bytes);
}

static void WriteResource(Span<byte> header, int offset, byte flags, ulong storedSize, ulong physicalOffset, ulong originalSize)
{
    var packed = ((ulong)flags << 56) | storedSize;
    WriteU64(header, offset, packed);
    WriteU64(header, offset + 8, physicalOffset);
    WriteU64(header, offset + 16, originalSize);
}

static void PatchByte(string path, long offset, byte value)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.WriteByte(value);
}

static void PatchU16(string path, long offset, ushort value)
{
    Span<byte> raw = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(raw, value);
    Patch(path, offset, raw);
}

static void PatchU32(string path, long offset, uint value)
{
    Span<byte> raw = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(raw, value);
    Patch(path, offset, raw);
}

static void PatchU64(string path, long offset, ulong value)
{
    Span<byte> raw = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(raw, value);
    Patch(path, offset, raw);
}

static void Patch(string path, long offset, ReadOnlySpan<byte> raw)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.Write(raw);
}

static void WriteU16(Span<byte> data, int offset, ushort value)
    => BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), value);

static void WriteU32(Span<byte> data, int offset, uint value)
    => BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), value);

static void WriteU64(Span<byte> data, int offset, ulong value)
    => BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, 8), value);

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
