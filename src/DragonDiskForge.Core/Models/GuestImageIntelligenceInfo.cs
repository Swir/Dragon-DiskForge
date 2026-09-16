namespace DragonDiskForge.Core.Models;

public sealed record GuestFileSystemDetectionInfo(
    int? PartitionIndex,
    long GuestOffsetBytes,
    long RegionSizeBytes,
    FileSystemKind Kind,
    string Variant,
    string Label,
    string Identifier,
    int? LogicalBlockSize,
    int? AllocationUnitSize,
    string Evidence)
{
    public string DisplayName => Kind switch
    {
        FileSystemKind.Iso9660 => "ISO9660",
        FileSystemKind.Udf => "UDF",
        FileSystemKind.Fat12 => "FAT12",
        FileSystemKind.Fat16 => "FAT16",
        FileSystemKind.Fat32 => "FAT32",
        FileSystemKind.ExFat => "exFAT",
        FileSystemKind.Ntfs => "NTFS",
        FileSystemKind.Ext2 => "ext2",
        FileSystemKind.Ext3 => "ext3",
        FileSystemKind.Ext4 => "ext4",
        _ => Kind.ToString()
    };
}

public sealed record GuestFileSystemRecognitionInfo(
    string ProviderId,
    string ProviderDisplayName,
    long GuestSizeBytes,
    IReadOnlyList<GuestFileSystemDetectionInfo> Detections)
{
    public bool HasDetections => Detections.Count > 0;
}

public sealed record GuestImageIntelligenceInfo(
    string ReaderKind,
    long GuestSizeBytes,
    PartitionIntelligenceInfo? PartitionLayout,
    GuestFileSystemRecognitionInfo FileSystems);
