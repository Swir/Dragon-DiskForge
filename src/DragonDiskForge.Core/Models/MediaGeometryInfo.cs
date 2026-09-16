namespace DragonDiskForge.Core.Models;

public sealed record MediaGeometryInfo(
    string GeometryName,
    int BytesPerSector,
    int SectorsPerTrack,
    int Heads,
    int Tracks,
    int TotalSectors,
    long CapacityBytes,
    bool BootParameterBlockDetected,
    string OemName,
    string FileSystemHint,
    string VolumeLabel,
    byte? MediaDescriptor);
