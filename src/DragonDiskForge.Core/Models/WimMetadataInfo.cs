namespace DragonDiskForge.Core.Models;

public sealed record WimResourceInfo(
    string Name,
    byte Flags,
    ulong StoredSize,
    ulong Offset,
    ulong OriginalSize)
{
    public bool IsPresent => StoredSize != 0;
    public bool IsCompressed => (Flags & 0x04) != 0;
    public bool IsMetadata => (Flags & 0x02) != 0;
}

public sealed record WimMetadataInfo(
    string ImagePath,
    uint HeaderSize,
    uint Version,
    uint Flags,
    uint ChunkSize,
    string Guid,
    ushort PartNumber,
    ushort TotalParts,
    uint ImageCount,
    WimResourceInfo LookupTable,
    WimResourceInfo XmlData,
    WimResourceInfo BootMetadata,
    uint BootIndex,
    WimResourceInfo IntegrityTable)
{
    public const uint StandardVersion = 68864;
    public const uint SolidVersion = 3584;

    public bool IsSolidVersion => Version == SolidVersion;
    public bool IsStandalone => PartNumber == 1 && TotalParts == 1;
    public string ContainerFlavor => IsSolidVersion ? "ESD / solid WIM" : "WIM";
}
