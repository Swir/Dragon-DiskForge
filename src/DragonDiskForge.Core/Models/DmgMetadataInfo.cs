namespace DragonDiskForge.Core.Models;

public sealed record DmgMetadataInfo(
    string ImagePath,
    uint Version,
    uint HeaderSize,
    uint Flags,
    ulong RunningDataForkOffset,
    ulong DataForkOffset,
    ulong DataForkLength,
    ulong ResourceForkOffset,
    ulong ResourceForkLength,
    uint SegmentNumber,
    uint SegmentCount,
    string SegmentId,
    uint DataChecksumType,
    uint DataChecksumSize,
    ulong XmlOffset,
    ulong XmlLength,
    uint MasterChecksumType,
    uint MasterChecksumSize,
    uint ImageVariant,
    ulong SectorCount,
    int BlkxEntryCount)
{
    public const int LogicalSectorSize = 512;
    public ulong VirtualSizeBytes => checked(SectorCount * (ulong)LogicalSectorSize);
    public bool HasXmlPlist => XmlOffset != 0 && XmlLength != 0;
    public bool HasResourceFork => ResourceForkOffset != 0 && ResourceForkLength != 0;
}
