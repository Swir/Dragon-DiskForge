namespace DragonDiskForge.Core.Models;

public sealed record MountHistoryEntry(
    string Path,
    DateTimeOffset LastSeenMountedUtc,
    string? LastTargetDisplay)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}
