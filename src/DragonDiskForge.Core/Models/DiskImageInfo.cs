namespace DragonDiskForge.Core.Models;

public sealed record DiskImageInfo(
    string Path,
    string FileName,
    string Format,
    long SizeBytes,
    string DetectionMethod,
    bool CanExplore,
    bool CanMount,
    bool CanConvert,
    bool CanVerify)
{
    public string SizeDisplay => SizeBytes switch
    {
        >= 1L << 40 => $"{SizeBytes / (double)(1L << 40):0.##} TB",
        >= 1L << 30 => $"{SizeBytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{SizeBytes / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{SizeBytes / (double)(1L << 10):0.##} KB",
        _ => $"{SizeBytes} B"
    };
}
