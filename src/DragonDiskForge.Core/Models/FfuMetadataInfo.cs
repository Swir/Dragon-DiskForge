namespace DragonDiskForge.Core.Models;

public sealed record FfuSecurityMetadata(
    uint HeaderSize,
    uint ChunkSizeInKb,
    uint AlgorithmId,
    uint CatalogSize,
    uint HashTableSize,
    long AlignedRegionLength);

public sealed record FfuImageMetadata(
    uint HeaderSize,
    uint ManifestSize,
    uint ChunkSizeInKb,
    long PhysicalOffset,
    long AlignedRegionLength);

public sealed record FfuStoreMetadata(
    string PlatformId,
    uint BlockSize,
    int WriteDescriptorCount,
    uint WriteDescriptorLength,
    int ValidateDescriptorCount,
    uint ValidateDescriptorLength,
    long PhysicalOffset,
    long AlignedRegionLength);

public sealed record FfuMetadataInfo(
    string ImagePath,
    FfuSecurityMetadata Security,
    FfuImageMetadata Image,
    FfuStoreMetadata Store,
    long PhysicalFileLength);
