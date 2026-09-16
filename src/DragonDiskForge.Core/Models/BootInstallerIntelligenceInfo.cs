namespace DragonDiskForge.Core.Models;

public enum BootFirmwareKind
{
    Bios,
    Uefi,
    Other
}

public enum BootMediaType
{
    NoEmulation,
    Floppy120Mb,
    Floppy144Mb,
    Floppy288Mb,
    HardDisk,
    Unknown
}

public enum InstallerFamily
{
    Windows,
    Linux
}

public sealed record BootCatalogEntryInfo(
    BootFirmwareKind Firmware,
    byte PlatformId,
    string PlatformName,
    bool Bootable,
    BootMediaType MediaType,
    ushort LoadSegment,
    byte SystemType,
    ushort LoadSectorCount,
    uint ImageLba,
    string SectionId);

public sealed record InstallerDetectionInfo(
    InstallerFamily Family,
    string Variant,
    string ArchitectureHint,
    IReadOnlyList<string> EvidencePaths);

public sealed record BootInstallerIntelligenceInfo(
    string ProviderId,
    string ProviderDisplayName,
    long PhysicalImageSizeBytes,
    bool HasElToritoCatalog,
    uint? BootCatalogLba,
    IReadOnlyList<BootCatalogEntryInfo> BootEntries,
    IReadOnlyList<InstallerDetectionInfo> Installers,
    IReadOnlyList<string> ArchitectureHints)
{
    public bool IsBootable => BootEntries.Any(x => x.Bootable);
    public bool SupportsBiosBoot => BootEntries.Any(x => x.Bootable && x.Firmware == BootFirmwareKind.Bios);
    public bool SupportsUefiBoot => BootEntries.Any(x => x.Bootable && x.Firmware == BootFirmwareKind.Uefi);
}
