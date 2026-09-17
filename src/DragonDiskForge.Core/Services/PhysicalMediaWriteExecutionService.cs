using System.Buffers;
using System.Security.Cryptography;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Coordinates a bounded image-to-physical-media write against an injected sink.
/// This type does not open physical devices itself. A platform writer must be supplied separately
/// and must expose the exact destination identity used for a final pre-write safety revalidation.
/// </summary>
public sealed class PhysicalMediaWriteExecutionService
{
    public const int DefaultBufferSizeBytes = 1024 * 1024;
    public const int MinimumBufferSizeBytes = 4 * 1024;
    public const int MaximumBufferSizeBytes = 8 * 1024 * 1024;

    private readonly PhysicalMediaSafetyService _safety;

    public PhysicalMediaWriteExecutionService(PhysicalMediaSafetyService? safety = null)
    {
        _safety = safety ?? new PhysicalMediaSafetyService();
    }

    public async Task<PhysicalMediaWriteExecutionResult> ExecuteAsync(
        PhysicalMediaWritePlan plan,
        string? suppliedConfirmationToken,
        IPhysicalMediaWriteSink sink,
        IProgress<PhysicalMediaWriteProgress>? progress = null,
        int bufferSizeBytes = DefaultBufferSizeBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);

        if (bufferSizeBytes is < MinimumBufferSizeBytes or > MaximumBufferSizeBytes)
            throw new ArgumentOutOfRangeException(
                nameof(bufferSizeBytes),
                $"Buffer size must be between {MinimumBufferSizeBytes} and {MaximumBufferSizeBytes} bytes.");

        var preflightFailure = ValidatePreflight(plan, suppliedConfirmationToken, sink.Destination);
        if (preflightFailure is not null)
        {
            return Result(
                PhysicalMediaWriteExecutionStatus.RefusedBeforeWrite,
                bytesWritten: 0,
                plan.SourceLengthBytes,
                hash: null,
                error: preflightFailure);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result(
                PhysicalMediaWriteExecutionStatus.CancelledBeforeWrite,
                bytesWritten: 0,
                plan.SourceLengthBytes,
                hash: null,
                error: "Operation was cancelled before any destination write.");
        }

        FileStream? source = null;
        byte[]? rentedBuffer = null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytesWritten = 0;

        try
        {
            if (!File.Exists(plan.SourcePath))
                throw new FileNotFoundException("Source image no longer exists.", plan.SourcePath);

            source = new FileStream(
                plan.SourcePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    BufferSize = Math.Min(bufferSizeBytes, 128 * 1024),
                });

            if (source.Length != plan.SourceLengthBytes)
            {
                throw new IOException(
                    $"Source image length changed after planning: planned {plan.SourceLengthBytes} bytes, current {source.Length} bytes.");
            }

            ReportProgress(progress, 0, plan.SourceLengthBytes);
            rentedBuffer = ArrayPool<byte>.Shared.Rent(bufferSizeBytes);

            while (bytesWritten < plan.SourceLengthBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remaining = plan.SourceLengthBytes - bytesWritten;
                var requested = (int)Math.Min(bufferSizeBytes, remaining);
                var read = await source.ReadAsync(
                    rentedBuffer.AsMemory(0, requested),
                    cancellationToken).ConfigureAwait(false);

                if (read == 0)
                    throw new EndOfStreamException("Source image ended before the planned byte count was read.");

                cancellationToken.ThrowIfCancellationRequested();

                await sink.WriteAsync(
                    bytesWritten,
                    rentedBuffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);

                hash.AppendData(rentedBuffer, 0, read);
                bytesWritten += read;
                ReportProgress(progress, bytesWritten, plan.SourceLengthBytes);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await sink.FlushAsync(cancellationToken).ConfigureAwait(false);

            return Result(
                PhysicalMediaWriteExecutionStatus.Completed,
                bytesWritten,
                plan.SourceLengthBytes,
                hash: CurrentHashHex(hash),
                error: null);
        }
        catch (OperationCanceledException)
        {
            var started = bytesWritten > 0;
            return Result(
                started
                    ? PhysicalMediaWriteExecutionStatus.CancelledAfterWriteStarted
                    : PhysicalMediaWriteExecutionStatus.CancelledBeforeWrite,
                bytesWritten,
                plan.SourceLengthBytes,
                hash: started ? CurrentHashHex(hash) : null,
                error: started
                    ? "Operation was cancelled after destination modification began; the destination may be incomplete and requires recovery/rewrite."
                    : "Operation was cancelled before any destination write.");
        }
        catch (Exception ex)
        {
            var started = bytesWritten > 0;
            return Result(
                started
                    ? PhysicalMediaWriteExecutionStatus.FailedAfterWriteStarted
                    : PhysicalMediaWriteExecutionStatus.FailedBeforeWrite,
                bytesWritten,
                plan.SourceLengthBytes,
                hash: started ? CurrentHashHex(hash) : null,
                error: ex.Message);
        }
        finally
        {
            if (rentedBuffer is not null)
                ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: false);

            if (source is not null)
                await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string? ValidatePreflight(
        PhysicalMediaWritePlan plan,
        string? suppliedConfirmationToken,
        PhysicalDiskInfo currentDestination)
    {
        if (!plan.IsAllowed)
        {
            return plan.RefusalReasons.Count > 0
                ? string.Join(" ", plan.RefusalReasons)
                : "The physical-media write plan is refused.";
        }

        if (!_safety.ConfirmationMatches(plan, suppliedConfirmationToken))
            return "The destructive confirmation token does not exactly match the approved plan.";

        if (!SameDestinationIdentity(plan.Destination, currentDestination))
            return "Destination identity changed after planning; write is refused before the first byte.";

        var currentPlan = _safety.PreviewImageToDiskWrite(
            plan.SourcePath,
            plan.SourceLengthBytes,
            currentDestination);

        if (!currentPlan.IsAllowed)
        {
            return currentPlan.RefusalReasons.Count > 0
                ? string.Join(" ", currentPlan.RefusalReasons)
                : "Destination no longer satisfies the physical-media safety contract.";
        }

        if (!string.Equals(
                currentPlan.ConfirmationToken,
                plan.ConfirmationToken,
                StringComparison.Ordinal)
            || !_safety.ConfirmationMatches(currentPlan, suppliedConfirmationToken))
        {
            return "Destination confirmation binding changed after planning; write is refused before the first byte.";
        }

        return null;
    }

    private static bool SameDestinationIdentity(PhysicalDiskInfo planned, PhysicalDiskInfo current)
    {
        return planned.DiskNumber == current.DiskNumber
            && string.Equals(planned.DevicePath, current.DevicePath, StringComparison.OrdinalIgnoreCase)
            && planned.HasStableIdentity
            && current.HasStableIdentity
            && !string.IsNullOrWhiteSpace(planned.StableId)
            && string.Equals(planned.StableId, current.StableId, StringComparison.Ordinal);
    }

    private static PhysicalMediaWriteExecutionResult Result(
        PhysicalMediaWriteExecutionStatus status,
        long bytesWritten,
        long totalBytes,
        string? hash,
        string? error)
    {
        var destinationModified = bytesWritten > 0;
        var recoveryRequired = destinationModified
            && status != PhysicalMediaWriteExecutionStatus.Completed;

        return new PhysicalMediaWriteExecutionResult(
            status,
            bytesWritten,
            totalBytes,
            DestinationMayBeModified: destinationModified,
            RequiresRecovery: recoveryRequired,
            WrittenSha256Hex: hash,
            ErrorMessage: error);
    }

    private static string CurrentHashHex(IncrementalHash hash)
        => Convert.ToHexString(hash.GetCurrentHash());

    private static void ReportProgress(
        IProgress<PhysicalMediaWriteProgress>? progress,
        long bytesWritten,
        long totalBytes)
    {
        if (progress is null)
            return;

        try
        {
            // Progress is observational only. A UI/reporting callback must not abort a write
            // after destructive I/O has begun.
            progress.Report(new PhysicalMediaWriteProgress(bytesWritten, totalBytes));
        }
        catch
        {
            // Intentionally isolated from the write pipeline.
        }
    }
}
