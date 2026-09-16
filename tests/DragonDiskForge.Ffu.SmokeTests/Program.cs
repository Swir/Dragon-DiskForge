using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Providers;

const int ChunkSize = 4096;
const int ImageOffset = ChunkSize;
const int StoreOffset = ChunkSize * 2;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-FFU-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var provider = new FfuImageProvider();

    var valid = Path.Combine(root, "valid.ffu");
    CreateFfu(valid);
    Expect(await provider.CanHandleAsync(valid), "Valid common-layout FFU should be recognized.");

    var metadata = await provider.ReadFfuMetadataAsync(valid);
    Expect(metadata.Security.HeaderSize == 32 && metadata.Security.ChunkSizeInKb == 4,
        "FFU security header and chunk size should be parsed.");
    Expect(metadata.Security.AlgorithmId == 0x800C,
        "FFU SHA-256 algorithm id should be preserved.");
    Expect(metadata.Security.AlignedRegionLength == ChunkSize,
        "Security/catalog/hash region should align to one chunk.");
    Expect(metadata.Image.HeaderSize == 24 && metadata.Image.ManifestSize == 16,
        "FFU image header and manifest length should be parsed.");
    Expect(metadata.Image.PhysicalOffset == ImageOffset && metadata.Image.AlignedRegionLength == ChunkSize,
        "FFU image region should follow the aligned security region.");
    Expect(metadata.Store.PhysicalOffset == StoreOffset && metadata.Store.PlatformId == "DragonBoard",
        "FFU common store metadata and PlatformID should be parsed.");
    Expect(metadata.Store.BlockSize == 4096 && metadata.Store.WriteDescriptorCount == 2,
        "FFU block size and write-descriptor count should be preserved.");
    Expect(metadata.Store.WriteDescriptorLength == 32 && metadata.Store.ValidateDescriptorCount == 1
        && metadata.Store.ValidateDescriptorLength == 16,
        "FFU descriptor metadata lengths/counts should be preserved.");

    var inspect = await provider.InspectAsync(valid);
    Expect(inspect.Format == "FFU", "Inspect should identify FFU.");
    Expect(!inspect.CanExplore && !inspect.CanMount && !inspect.CanConvert && inspect.CanVerify,
        "FFU provider must remain metadata-only in this slice.");

    var badSignature = Path.Combine(root, "bad-signature.ffu");
    File.Copy(valid, badSignature);
    PatchByte(badSignature, 4, (byte)'X');
    Expect(!await provider.CanHandleAsync(badSignature), "Bad SignedImage signature must be rejected.");

    var badSecuritySize = Path.Combine(root, "bad-security-size.ffu");
    File.Copy(valid, badSecuritySize);
    PatchU32(badSecuritySize, 0, 36);
    Expect(!await provider.CanHandleAsync(badSecuritySize), "Unsupported security-header size must be rejected.");

    var badChunk = Path.Combine(root, "bad-chunk.ffu");
    File.Copy(valid, badChunk);
    PatchU32(badChunk, 16, 3);
    Expect(!await provider.CanHandleAsync(badChunk), "Non-power-of-two FFU chunk size must be rejected.");

    var badAlgorithm = Path.Combine(root, "bad-algorithm.ffu");
    File.Copy(valid, badAlgorithm);
    PatchU32(badAlgorithm, 20, 0x12345678);
    Expect(!await provider.CanHandleAsync(badAlgorithm), "Unknown FFU security algorithm must be rejected.");

    var securityOob = Path.Combine(root, "security-oob.ffu");
    File.Copy(valid, securityOob);
    PatchU32(securityOob, 24, 100_000);
    Expect(!await provider.CanHandleAsync(securityOob), "Security/catalog/hash range outside the file must be rejected.");

    var badImageSignature = Path.Combine(root, "bad-image-signature.ffu");
    File.Copy(valid, badImageSignature);
    PatchByte(badImageSignature, ImageOffset + 4, (byte)'X');
    Expect(!await provider.CanHandleAsync(badImageSignature), "Bad ImageFlash signature must be rejected.");

    var badImageSize = Path.Combine(root, "bad-image-size.ffu");
    File.Copy(valid, badImageSize);
    PatchU32(badImageSize, ImageOffset, 28);
    Expect(!await provider.CanHandleAsync(badImageSize), "Unsupported FFU image-header size must be rejected.");

    var chunkMismatch = Path.Combine(root, "chunk-mismatch.ffu");
    File.Copy(valid, chunkMismatch);
    PatchU32(chunkMismatch, ImageOffset + 20, 8);
    Expect(!await provider.CanHandleAsync(chunkMismatch), "Security/image chunk-size mismatch must be rejected.");

    var manifestOob = Path.Combine(root, "manifest-oob.ffu");
    File.Copy(valid, manifestOob);
    PatchU32(manifestOob, ImageOffset + 16, 100_000);
    Expect(!await provider.CanHandleAsync(manifestOob), "Manifest region outside the file must be rejected.");

    var badPlatform = Path.Combine(root, "bad-platform.ffu");
    File.Copy(valid, badPlatform);
    PatchByte(badPlatform, StoreOffset + 12, 0x01);
    Expect(!await provider.CanHandleAsync(badPlatform), "Non-ASCII FFU PlatformID metadata must be rejected.");

    var badBlockSize = Path.Combine(root, "bad-block-size.ffu");
    File.Copy(valid, badBlockSize);
    PatchU32(badBlockSize, StoreOffset + 204, 123);
    Expect(!await provider.CanHandleAsync(badBlockSize), "Malformed FFU store block size must be rejected.");

    var negativeCount = Path.Combine(root, "negative-write-count.ffu");
    File.Copy(valid, negativeCount);
    PatchI32(negativeCount, StoreOffset + 208, -1);
    Expect(!await provider.CanHandleAsync(negativeCount), "Negative FFU descriptor count must be rejected.");

    var inconsistentDescriptor = Path.Combine(root, "descriptor-inconsistent.ffu");
    File.Copy(valid, inconsistentDescriptor);
    PatchI32(inconsistentDescriptor, StoreOffset + 208, 0);
    Expect(!await provider.CanHandleAsync(inconsistentDescriptor), "Non-zero descriptor length with zero count must be rejected.");

    var descriptorOob = Path.Combine(root, "descriptor-oob.ffu");
    File.Copy(valid, descriptorOob);
    PatchU32(descriptorOob, StoreOffset + 212, 1_000_000);
    Expect(!await provider.CanHandleAsync(descriptorOob), "Descriptor metadata extending outside the file must be rejected.");

    var foreign = Path.Combine(root, "foreign.bin");
    File.Copy(valid, foreign);
    Expect(!await provider.CanHandleAsync(foreign), "FFU provider must not claim foreign extensions.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => provider.ReadFfuMetadataAsync(valid, cts.Token).AsTask(),
        "Pre-cancelled FFU parsing should propagate cancellation.");

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
        new ProviderRegistration(new WimEsdImageProvider(), Priority: 10),
        new ProviderRegistration(provider, Priority: 5)
    ]);

    var resolution = await registry.ResolveAsync(valid);
    Expect(resolution.Provider is FfuImageProvider, "Provider registry should resolve valid FFU metadata.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.ContainerMetadata) == true,
        "FFU should advertise ContainerMetadata capability.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.VirtualDiskMetadata) == false,
        "FFU must not be mislabeled as VirtualDiskMetadata.");
    Expect(resolution.Descriptor?.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse) == false,
        "FFU must not advertise DirectBrowse in the metadata-only slice.");

    Console.WriteLine("Dragon DiskForge FFU metadata smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateFfu(string path)
{
    var bytes = new byte[ChunkSize * 4];

    var security = bytes.AsSpan(0, 32);
    WriteU32(security, 0, 32);
    Encoding.ASCII.GetBytes("SignedImage ").CopyTo(security.Slice(4, 12));
    WriteU32(security, 16, 4);
    WriteU32(security, 20, 0x800C);
    WriteU32(security, 24, 16);
    WriteU32(security, 28, 16);

    var image = bytes.AsSpan(ImageOffset, 24);
    WriteU32(image, 0, 24);
    Encoding.ASCII.GetBytes("ImageFlash ").CopyTo(image.Slice(4, 12));
    WriteU32(image, 16, 16);
    WriteU32(image, 20, 4);

    var store = bytes.AsSpan(StoreOffset, 248);
    Encoding.ASCII.GetBytes("DragonBoard").CopyTo(store.Slice(12, "DragonBoard".Length));
    WriteU32(store, 204, 4096);
    WriteI32(store, 208, 2);
    WriteU32(store, 212, 32);
    WriteI32(store, 216, 1);
    WriteU32(store, 220, 16);

    File.WriteAllBytes(path, bytes);
}

static void PatchByte(string path, long offset, byte value)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.WriteByte(value);
}

static void PatchI32(string path, long offset, int value)
{
    Span<byte> raw = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(raw, value);
    Patch(path, offset, raw);
}

static void PatchU32(string path, long offset, uint value)
{
    Span<byte> raw = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(raw, value);
    Patch(path, offset, raw);
}

static void Patch(string path, long offset, ReadOnlySpan<byte> raw)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.Write(raw);
}

static void WriteI32(Span<byte> data, int offset, int value)
    => BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, 4), value);

static void WriteU32(Span<byte> data, int offset, uint value)
    => BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), value);

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
