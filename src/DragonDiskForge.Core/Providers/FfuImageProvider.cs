using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class FfuImageProvider : IFfuMetadataProvider
{
    private const int SecurityHeaderSize = 32;
    private const int ImageHeaderSize = 24;
    private const int StoreHeaderSize = 248;
    private const uint Sha256AlgorithmId = 0x0000800C;
    private const long MinChunkSizeBytes = 4 * 1024;
    private const long MaxChunkSizeBytes = 1024L * 1024 * 1024;
    private const long MaxDescriptorMetadataBytes = 1024L * 1024 * 1024;
    private const int MaxDescriptorCount = 10_000_000;
    private static readonly byte[] SecuritySignature = Encoding.ASCII.GetBytes("SignedImage ");
    private static readonly byte[] ImageSignature = Encoding.ASCII.GetBytes("ImageFlash ");
    private static readonly string[] FfuExtensions = [".ffu"];

    public string Id => "ffu";
    public string DisplayName => "FFU container metadata";
    public IReadOnlyCollection<string> Extensions => FfuExtensions;

    public async ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;
        if (!Path.GetExtension(path).Equals(".ffu", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            _ = await ReadFfuMetadataAsync(path, cancellationToken);
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

    public async ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var metadata = await ReadFfuMetadataAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var platform = string.IsNullOrWhiteSpace(metadata.Store.PlatformId) ? "unspecified platform" : metadata.Store.PlatformId;

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            "FFU",
            file.Length,
            $"FFU metadata ({metadata.Security.ChunkSizeInKb} KiB chunks; {platform}; {metadata.Store.WriteDescriptorCount} write descriptor(s))",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<FfuMetadataInfo> ReadFfuMetadataAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("FFU image was not found.", fullPath);
        if (!file.Extension.Equals(".ffu", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The FFU provider accepts only .ffu files.");
        if (file.Length < SecurityHeaderSize)
            throw new InvalidDataException("FFU file is too small to contain a security header.");

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var securityBytes = new byte[SecurityHeaderSize];
        await stream.ReadExactlyAsync(securityBytes, cancellationToken);
        var securitySpan = securityBytes.AsSpan();

        var declaredSecuritySize = ReadU32(securitySpan, 0);
        if (!securitySpan.Slice(4, SecuritySignature.Length).SequenceEqual(SecuritySignature))
            throw new InvalidDataException("FFU security signature does not match SignedImage.");

        var chunkSizeInKb = ReadU32(securitySpan, 16);
        var algorithmId = ReadU32(securitySpan, 20);
        var catalogSize = ReadU32(securitySpan, 24);
        var hashTableSize = ReadU32(securitySpan, 28);
        var chunkSizeBytes = ValidateSecurityHeader(declaredSecuritySize, chunkSizeInKb, algorithmId);

        var securityRawLength = checked((long)declaredSecuritySize + catalogSize + hashTableSize);
        var securityRegionLength = AlignUp(securityRawLength, chunkSizeBytes);
        EnsureRegion(file.Length, 0, securityRegionLength, "FFU security/catalog/hash region");

        var imageOffset = securityRegionLength;
        EnsureRegion(file.Length, imageOffset, ImageHeaderSize, "FFU image header");
        stream.Position = imageOffset;
        var imageBytes = new byte[ImageHeaderSize];
        await stream.ReadExactlyAsync(imageBytes, cancellationToken);
        var imageSpan = imageBytes.AsSpan();

        var declaredImageSize = ReadU32(imageSpan, 0);
        if (!imageSpan.Slice(4, ImageSignature.Length).SequenceEqual(ImageSignature)
            || imageSpan[4 + ImageSignature.Length] != 0)
        {
            throw new InvalidDataException("FFU image signature does not match the 12-byte ImageFlash field.");
        }

        var manifestSize = ReadU32(imageSpan, 16);
        var imageChunkSizeInKb = ReadU32(imageSpan, 20);

        if (declaredImageSize != ImageHeaderSize)
            throw new InvalidDataException("FFU image header size must be exactly 24 bytes in the proven slice.");
        if (imageChunkSizeInKb != chunkSizeInKb)
            throw new InvalidDataException("FFU security and image headers disagree on chunk size.");

        var imageRawLength = checked((long)declaredImageSize + manifestSize);
        var imageRegionLength = AlignUp(imageRawLength, chunkSizeBytes);
        EnsureRegion(file.Length, imageOffset, imageRegionLength, "FFU image/manifest region");

        var storeOffset = checked(imageOffset + imageRegionLength);
        EnsureRegion(file.Length, storeOffset, StoreHeaderSize, "FFU common store header");
        stream.Position = storeOffset;
        var storeBytes = new byte[StoreHeaderSize];
        await stream.ReadExactlyAsync(storeBytes, cancellationToken);
        var storeSpan = storeBytes.AsSpan();

        var platformId = ReadPlatformId(storeSpan.Slice(12, 192));
        var blockSize = ReadU32(storeSpan, 204);
        var writeDescriptorCount = ReadI32(storeSpan, 208);
        var writeDescriptorLength = ReadU32(storeSpan, 212);
        var validateDescriptorCount = ReadI32(storeSpan, 216);
        var validateDescriptorLength = ReadU32(storeSpan, 220);

        ValidateStoreHeader(
            blockSize,
            writeDescriptorCount,
            writeDescriptorLength,
            validateDescriptorCount,
            validateDescriptorLength);

        var storeRawLength = checked((long)StoreHeaderSize + writeDescriptorLength + validateDescriptorLength);
        var storeRegionLength = AlignUp(storeRawLength, chunkSizeBytes);
        EnsureRegion(file.Length, storeOffset, storeRegionLength, "FFU store metadata region");

        var security = new FfuSecurityMetadata(
            declaredSecuritySize,
            chunkSizeInKb,
            algorithmId,
            catalogSize,
            hashTableSize,
            securityRegionLength);

        var image = new FfuImageMetadata(
            declaredImageSize,
            manifestSize,
            imageChunkSizeInKb,
            imageOffset,
            imageRegionLength);

        var store = new FfuStoreMetadata(
            platformId,
            blockSize,
            writeDescriptorCount,
            writeDescriptorLength,
            validateDescriptorCount,
            validateDescriptorLength,
            storeOffset,
            storeRegionLength);

        return new FfuMetadataInfo(file.FullName, security, image, store, file.Length);
    }

    private static long ValidateSecurityHeader(uint headerSize, uint chunkSizeInKb, uint algorithmId)
    {
        if (headerSize != SecurityHeaderSize)
            throw new InvalidDataException("FFU security header size must be exactly 32 bytes in the proven slice.");
        if (algorithmId != Sha256AlgorithmId)
            throw new InvalidDataException($"FFU security algorithm 0x{algorithmId:X8} is outside the proven SHA-256 slice.");
        if (chunkSizeInKb == 0)
            throw new InvalidDataException("FFU chunk size cannot be zero.");

        var chunkSizeBytes = checked((long)chunkSizeInKb * 1024L);
        if (chunkSizeBytes < MinChunkSizeBytes || chunkSizeBytes > MaxChunkSizeBytes || !IsPowerOfTwo(chunkSizeBytes))
            throw new InvalidDataException("FFU chunk size is outside supported structural bounds.");
        return chunkSizeBytes;
    }

    private static void ValidateStoreHeader(
        uint blockSize,
        int writeDescriptorCount,
        uint writeDescriptorLength,
        int validateDescriptorCount,
        uint validateDescriptorLength)
    {
        if (blockSize == 0 || blockSize % 512 != 0 || blockSize > MaxChunkSizeBytes)
            throw new InvalidDataException("FFU store block size is outside supported structural bounds.");
        if (writeDescriptorCount < 0 || writeDescriptorCount > MaxDescriptorCount)
            throw new InvalidDataException("FFU write-descriptor count is outside the provider safety bound.");
        if (validateDescriptorCount < 0 || validateDescriptorCount > MaxDescriptorCount)
            throw new InvalidDataException("FFU validation-descriptor count is outside the provider safety bound.");
        if (writeDescriptorLength > MaxDescriptorMetadataBytes || validateDescriptorLength > MaxDescriptorMetadataBytes)
            throw new InvalidDataException("FFU descriptor metadata length exceeds the provider safety bound.");
        if (writeDescriptorCount == 0 && writeDescriptorLength != 0)
            throw new InvalidDataException("FFU write-descriptor length is non-zero while the count is zero.");
        if (writeDescriptorCount > 0 && writeDescriptorLength == 0)
            throw new InvalidDataException("FFU write-descriptor count is non-zero while the length is zero.");
        if (validateDescriptorCount == 0 && validateDescriptorLength != 0)
            throw new InvalidDataException("FFU validation-descriptor length is non-zero while the count is zero.");
        if (validateDescriptorCount > 0 && validateDescriptorLength == 0)
            throw new InvalidDataException("FFU validation-descriptor count is non-zero while the length is zero.");
    }

    private static string ReadPlatformId(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value == 0 || value == (byte)' ')
                continue;
            if (value < 0x21 || value > 0x7E)
                throw new InvalidDataException("FFU PlatformID contains non-ASCII metadata.");
        }

        return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    }

    private static long AlignUp(long value, long alignment)
    {
        if (value < 0 || alignment <= 0)
            throw new InvalidDataException("FFU alignment metadata is invalid.");
        return checked(((value + alignment - 1) / alignment) * alignment);
    }

    private static void EnsureRegion(long fileLength, long offset, long length, string name)
    {
        if (offset < 0 || length < 0 || offset > fileLength || length > fileLength - offset)
            throw new InvalidDataException($"{name} lies outside the physical FFU file.");
    }

    private static bool IsPowerOfTwo(long value)
        => value > 0 && (value & (value - 1)) == 0;

    private static int ReadI32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
}
