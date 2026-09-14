namespace DragonDiskForge.Core.Models;

public enum MountHistoryAction
{
    Mounted,
    Unmounted
}

public sealed record MountHistoryEntry(
    string ImagePath,
    MountHistoryAction Action,
    DateTimeOffset TimestampUtc,
    string? TargetDisplay = null);
