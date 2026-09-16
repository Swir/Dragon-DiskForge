namespace DragonDiskForge.Core.Models;

public sealed record OutputCommitInfo(
    string DestinationPath,
    long SizeBytes,
    bool ReplacedExisting);
