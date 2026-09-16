namespace DragonDiskForge.Core.Models;

public enum PartitionTableScheme
{
    Mbr,
    Gpt
}

public sealed record PartitionInfo(
    int Index,
    ulong FirstLba,
    ulong SectorCount,
    long OffsetBytes,
    long SizeBytes,
    string TypeId,
    string TypeName,
    string Name,
    bool IsBootable);

public sealed record PartitionTableInfo(
    PartitionTableScheme Scheme,
    int SectorSize,
    IReadOnlyList<PartitionInfo> Partitions)
{
    public string SchemeDisplay => Scheme == PartitionTableScheme.Gpt ? "GPT" : "MBR";
}
