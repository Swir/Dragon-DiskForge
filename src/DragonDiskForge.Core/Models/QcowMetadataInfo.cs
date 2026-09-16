namespace DragonDiskForge.Core.Models;

public sealed record QcowMetadataInfo(
    string ImagePath,
    string Format,
    uint Version,
    ulong VirtualSizeBytes,
    int ClusterBits,
    ulong BackingFileOffset,
    uint BackingFileSize,
    string? BackingFileName,
    uint EncryptionMethod,
    int? L2Bits,
    ulong L1Size,
    ulong L1TableOffset,
    ulong? RefcountTableOffset,
    uint? RefcountTableClusters,
    uint? SnapshotCount,
    ulong? SnapshotsOffset,
    ulong IncompatibleFeatures,
    ulong CompatibleFeatures,
    ulong AutoclearFeatures,
    uint? RefcountOrder,
    uint HeaderLength,
    byte? CompressionType,
    uint? ModificationTime)
{
    public ulong ClusterSizeBytes => 1UL << ClusterBits;
    public bool HasBackingFile => BackingFileOffset != 0 && BackingFileSize != 0;
    public bool IsEncrypted => EncryptionMethod != 0;
    public bool IsQcow2 => Version is 2 or 3;
}
