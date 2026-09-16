namespace DragonDiskForge.Core.Models;

public enum FileSystemKind
{
    Iso9660,
    Udf,
    Fat12,
    Fat16,
    Fat32,
    ExFat,
    Ntfs,
    Ext2,
    Ext3,
    Ext4
}

public sealed record FileSystemDetectionInfo(
    int? PartitionIndex,
    long PhysicalOffsetBytes,
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

public sealed record FileSystemRecognitionInfo(
    string ProviderId,
    string ProviderDisplayName,
    long PhysicalImageSizeBytes,
    IReadOnlyList<FileSystemDetectionInfo> Detections)
{
    public bool HasDetections => Detections.Count > 0;
}
