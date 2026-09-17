namespace DragonDiskForge.Core.Models;

public enum PhysicalMediaWriteExecutionStatus
{
    RefusedBeforeWrite,
    CancelledBeforeWrite,
    FailedBeforeWrite,
    CancelledAfterWriteStarted,
    FailedAfterWriteStarted,
    Completed,
}

public sealed record PhysicalMediaWriteProgress(
    long BytesWritten,
    long TotalBytes)
{
    public double Fraction => TotalBytes <= 0
        ? 0d
        : Math.Clamp((double)BytesWritten / TotalBytes, 0d, 1d);
}

public sealed record PhysicalMediaWriteExecutionResult(
    PhysicalMediaWriteExecutionStatus Status,
    long BytesWritten,
    long TotalBytes,
    bool DestinationMayBeModified,
    bool RequiresRecovery,
    string? WrittenSha256Hex,
    string? ErrorMessage)
{
    public bool IsCompleted => Status == PhysicalMediaWriteExecutionStatus.Completed;
}
