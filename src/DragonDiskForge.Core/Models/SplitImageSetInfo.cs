namespace DragonDiskForge.Core.Models;

public sealed record SplitImageSetInfo(
    string DirectoryPath,
    ulong SourceSizeBytes,
    long PartSizeBytes,
    int PartCount);
