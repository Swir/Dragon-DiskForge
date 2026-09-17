namespace DragonDiskForge.Core.Models;

public sealed record PhysicalMediaWritePlan(
    string SourcePath,
    long SourceLengthBytes,
    PhysicalDiskInfo Destination,
    bool IsAllowed,
    bool RequiresExplicitConfirmation,
    string? ConfirmationToken,
    IReadOnlyList<string> RefusalReasons,
    IReadOnlyList<string> Warnings)
{
    public bool IsRefused => !IsAllowed;
}
