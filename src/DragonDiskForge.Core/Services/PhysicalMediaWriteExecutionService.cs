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
                destinationMayBeModified: false,
                hash: null,
                error: preflightFailure);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Result(
                PhysicalMediaWriteExecutionStatus.CancelledBeforeWrite,
                bytesWritten: 0,
                plan.SourceLengthBytes,
                destinationMayBeModified: false,
                hash: null,
                error: "Operation was cancelled before any destination write.");
        }

        FileStream? source = null;
        byte[]? rentedBuffer = null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytesWritten = 0;
        var writeAttempted = false;

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

                // From this point onward a sink failure/cancellation may have happened after a
                // partial device transfer even if the sink could not report a completed chunk.
                // Fail closed and require recovery for every exception after a write was attempted.
                writeAttempted = true;
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
                destinationMayBeModified: true,
                hash: CurrentHashHex(hash),
                error: null);
        }
        catch (OperationCanceledException)
        {
            return Result(
                writeAttempted
                    ? PhysicalMediaWriteExecutionStatus.CancelledAfterWriteStarted
                    : PhysicalMediaWriteExecutionStatus.CancelledBeforeWrite,
                bytesWritten,
                plan.SourceLengthBytes,
                destinationMayBeModified: writeAttempted,
                hash: bytesWritten > 0 ? CurrentHashHex(hash) : null,
                error: writeAttempted
                    ? "Operation was cancelled after a destination write was attempted; the destination may be incomplete and requires recovery/rewrite."
                    : "Operation was cancelled before any destination write.");
        }
        catch (Exception ex)
        {
            return Result(
                writeAttempted
                    ? PhysicalMediaWriteExecutionStatus.FailedAfterWriteStarted
                    : PhysicalMediaWriteExecutionStatus.FailedBeforeWrite,
                bytesWritten,
                plan.SourceLengthBytes,
                destinationMayBeModified: writeAttempted,
                hash: bytesWritten > 0 ? CurrentHashHex(hash) : null,
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
        bool destinationMayBeModified,
        string? hash,
        string? error)
    {
        var recoveryRequired = destinationMayBeModified
            && status != PhysicalMediaWriteExecutionStatus.Completed;

        return new PhysicalMediaWriteExecutionResult(
            status,
            bytesWritten,
            totalBytes,
            DestinationMayBeModified: destinationMayBeModified,
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
