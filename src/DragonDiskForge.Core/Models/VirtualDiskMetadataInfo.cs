namespace DragonDiskForge.Core.Models;

public sealed record VirtualDiskMetadataInfo(
    string ImagePath,
    string ContainerType,
    int SectorSize,
    ulong CapacitySectors,
    ulong GrainSizeSectors,
    uint Flags,
    ulong DescriptorOffsetSectors,
    ulong DescriptorSizeSectors,
    uint GrainTableEntries,
    ulong RedundantGrainDirectoryOffsetSectors,
    ulong GrainDirectoryOffsetSectors,
    ulong OverheadSectors,
    ushort CompressionAlgorithm,
    bool UncleanShutdown,
    string? DescriptorVersion,
    string? CreateType,
    string? Cid,
    string? ParentCid,
    int DescriptorExtentCount)
{
    public ulong CapacityBytes => checked(CapacitySectors * (ulong)SectorSize);
    public ulong GrainSizeBytes => checked(GrainSizeSectors * (ulong)SectorSize);
    public bool HasEmbeddedDescriptor => DescriptorOffsetSectors != 0 && DescriptorSizeSectors != 0;
}
