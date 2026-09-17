using System.Security.Cryptography;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

var failures = 0;
var safety = new PhysicalMediaSafetyService();
var execution = new PhysicalMediaWriteExecutionService(safety);
var imagePath = Path.Combine(Path.GetTempPath(), $"dragon-physical-media-safety-{Guid.NewGuid():N}.img");
var stableId = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

var safeDisk = MakeDisk(
    diskNumber: 7,
    capacityBytes: 8L * 1024 * 1024,
    busType: "USB",
    isRemovable: true,
    isSystemDisk: false,
    stableId: stableId,
    hasStableIdentity: true);

var safePlan = safety.PreviewImageToDiskWrite(imagePath, 4L * 1024 * 1024, safeDisk);
Check(safePlan.IsAllowed, "stable non-system disk can reach the confirmation gate");
Check(safePlan.RequiresExplicitConfirmation, "allowed destructive plan always requires explicit confirmation");
Check(safePlan.ConfirmationToken == "ERASE PHYSICALDRIVE7 AABBCCDDEEFF", "confirmation token is bound to disk number and stable identity");
Check(safety.ConfirmationMatches(safePlan, safePlan.ConfirmationToken), "exact confirmation token is accepted");
Check(!safety.ConfirmationMatches(safePlan, safePlan.ConfirmationToken?.ToLowerInvariant()), "confirmation token is case-sensitive");
Check(!safety.ConfirmationMatches(safePlan, "ERASE PHYSICALDRIVE8 DEADBEEF"), "confirmation for another disk is rejected");
Check(safePlan.Warnings.Any(x => x.Contains("smaller", StringComparison.OrdinalIgnoreCase)), "smaller source warns about trailing destination capacity");
Check(safePlan.Warnings.Any(x => x.Contains("removable", StringComparison.OrdinalIgnoreCase)), "removable-media evidence is surfaced as a warning");

var systemPlan = safety.PreviewImageToDiskWrite(
    imagePath,
    1024,
    safeDisk with { IsSystemDisk = true });
Check(systemPlan.IsRefused, "system disk is always refused");
Check(systemPlan.RefusalReasons.Any(x => x.Contains("system disk", StringComparison.OrdinalIgnoreCase)), "system-disk refusal is explicit");
Check(systemPlan.ConfirmationToken is null, "refused plan never emits a destructive confirmation token");
Check(!safety.ConfirmationMatches(systemPlan, "anything"), "refused plan cannot be confirmed");

var ambiguousPlan = safety.PreviewImageToDiskWrite(
    imagePath,
    1024,
    safeDisk with { HasStableIdentity = false });
Check(ambiguousPlan.IsRefused, "ambiguous destination identity is refused");
Check(ambiguousPlan.RefusalReasons.Any(x => x.Contains("stable hardware identity", StringComparison.OrdinalIgnoreCase)), "identity refusal explains the blocker");

var missingStableMaterialPlan = safety.PreviewImageToDiskWrite(
    imagePath,
    1024,
    safeDisk with { StableId = string.Empty, HasStableIdentity = true });
Check(missingStableMaterialPlan.IsRefused, "stable-identity claim without stable identity material is refused");
Check(missingStableMaterialPlan.ConfirmationToken is null, "missing stable identity material cannot produce a confirmation token");

var unknownCapacityPlan = safety.PreviewImageToDiskWrite(
    imagePath,
    1024,
    safeDisk with { CapacityBytes = null });
Check(unknownCapacityPlan.IsRefused, "unknown destination capacity is refused");

var tooSmallPlan = safety.PreviewImageToDiskWrite(
    imagePath,
    9L * 1024 * 1024,
    safeDisk);
Check(tooSmallPlan.IsRefused, "image larger than destination is refused");
Check(tooSmallPlan.RefusalReasons.Any(x => x.Contains("larger", StringComparison.OrdinalIgnoreCase)), "capacity refusal is explicit");

var physicalSource = safety.PreviewImageToDiskWrite(
    @"\\.\PhysicalDrive7",
    1024,
    safeDisk);
Check(physicalSource.IsRefused, "physical-device source is refused by file-image write plan");
Check(physicalSource.RefusalReasons.Any(x => x.Contains("regular image file", StringComparison.OrdinalIgnoreCase)), "physical source refusal is explicit");
Check(physicalSource.RefusalReasons.Any(x => x.Contains("same physical device", StringComparison.OrdinalIgnoreCase)), "same source/destination device is rejected explicitly");

var invalidLength = safety.PreviewImageToDiskWrite(imagePath, 0, safeDisk);
Check(invalidLength.IsRefused, "zero-length source is refused");

var unknownBus = safety.PreviewImageToDiskWrite(
    imagePath,
    1024,
    safeDisk with { BusType = "Unknown", IsRemovable = false });
Check(unknownBus.IsAllowed, "unknown bus type alone does not invent a hard refusal when stable identity and capacity are proven");
Check(unknownBus.Warnings.Any(x => x.Contains("bus type", StringComparison.OrdinalIgnoreCase)), "unknown bus type remains visible to the operator");

try
{
    var sourceBytes = Enumerable.Range(0, 96 * 1024)
        .Select(i => (byte)(i % 251))
        .ToArray();
    await File.WriteAllBytesAsync(imagePath, sourceBytes);

    var executionPlan = safety.PreviewImageToDiskWrite(imagePath, sourceBytes.Length, safeDisk);
    var progressEvents = new List<PhysicalMediaWriteProgress>();
    var successSink = new MemoryPhysicalMediaWriteSink(safeDisk);
    var success = await execution.ExecuteAsync(
        executionPlan,
        executionPlan.ConfirmationToken,
        successSink,
        new InlineProgress<PhysicalMediaWriteProgress>(progressEvents.Add),
        bufferSizeBytes: 16 * 1024);

    Check(success.Status == PhysicalMediaWriteExecutionStatus.Completed, "execution coordinator completes a fully accepted bounded write");
    Check(success.BytesWritten == sourceBytes.Length, "completed execution reports exact written-byte count");
    Check(success.DestinationMayBeModified, "completed execution truthfully reports destination modification");
    Check(!success.RequiresRecovery, "completed execution does not report recovery requirement");
    Check(successSink.WasFlushed, "successful execution flushes the destination sink");
    Check(successSink.Written.ToArray().SequenceEqual(sourceBytes), "successful execution preserves exact source byte order");
    Check(success.WrittenSha256Hex == Convert.ToHexString(SHA256.HashData(sourceBytes)), "successful execution records SHA-256 evidence for accepted bytes");
    Check(progressEvents.Count >= 2 && progressEvents[0].BytesWritten == 0, "execution reports initial zero-byte progress");
    Check(progressEvents[^1].BytesWritten == sourceBytes.Length, "execution reports final byte progress");
    Check(progressEvents.Zip(progressEvents.Skip(1), (a, b) => b.BytesWritten >= a.BytesWritten).All(x => x), "write progress is monotonic");

    var wrongTokenSink = new MemoryPhysicalMediaWriteSink(safeDisk);
    var wrongToken = await execution.ExecuteAsync(
        executionPlan,
        "ERASE PHYSICALDRIVE7 WRONGTOKEN",
        wrongTokenSink,
        bufferSizeBytes: 16 * 1024);
    Check(wrongToken.Status == PhysicalMediaWriteExecutionStatus.RefusedBeforeWrite, "wrong confirmation token is refused before write");
    Check(wrongTokenSink.Written.Length == 0 && wrongTokenSink.WriteCount == 0, "wrong confirmation cannot touch destination");

    var swappedDisk = safeDisk with
    {
        StableId = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100",
        SerialNumber = "DRAGON-SWAPPED-0007",
    };
    var swappedSink = new MemoryPhysicalMediaWriteSink(swappedDisk);
    var swapped = await execution.ExecuteAsync(
        executionPlan,
        executionPlan.ConfirmationToken,
        swappedSink,
        bufferSizeBytes: 16 * 1024);
    Check(swapped.Status == PhysicalMediaWriteExecutionStatus.RefusedBeforeWrite, "destination identity swap is refused before write");
    Check(swappedSink.WriteCount == 0, "identity swap cannot reach destination write");

    await File.WriteAllBytesAsync(imagePath, sourceBytes.Concat(new byte[] { 0xD7 }).ToArray());
    var changedSourceSink = new MemoryPhysicalMediaWriteSink(safeDisk);
    var changedSource = await execution.ExecuteAsync(
        executionPlan,
        executionPlan.ConfirmationToken,
        changedSourceSink,
        bufferSizeBytes: 16 * 1024);
    Check(changedSource.Status == PhysicalMediaWriteExecutionStatus.FailedBeforeWrite, "source length change after planning fails before write");
    Check(changedSourceSink.WriteCount == 0, "changed source length cannot touch destination");
    await File.WriteAllBytesAsync(imagePath, sourceBytes);

    using (var preCancelledCts = new CancellationTokenSource())
    {
        preCancelledCts.Cancel();
        var preCancelledSink = new MemoryPhysicalMediaWriteSink(safeDisk);
        var preCancelled = await execution.ExecuteAsync(
            executionPlan,
            executionPlan.ConfirmationToken,
            preCancelledSink,
            bufferSizeBytes: 16 * 1024,
            cancellationToken: preCancelledCts.Token);
        Check(preCancelled.Status == PhysicalMediaWriteExecutionStatus.CancelledBeforeWrite, "pre-cancelled operation exits before destination write");
        Check(preCancelledSink.WriteCount == 0 && !preCancelled.DestinationMayBeModified, "pre-cancellation guarantees no destination modification by the coordinator");
    }

    using (var midWriteCts = new CancellationTokenSource())
    {
        var midWriteSink = new MemoryPhysicalMediaWriteSink(safeDisk)
        {
            AfterSuccessfulWrite = writeCount =>
            {
                if (writeCount == 1)
                    midWriteCts.Cancel();
            },
        };
        var midCancelled = await execution.ExecuteAsync(
            executionPlan,
            executionPlan.ConfirmationToken,
            midWriteSink,
            bufferSizeBytes: 16 * 1024,
            cancellationToken: midWriteCts.Token);
        Check(midCancelled.Status == PhysicalMediaWriteExecutionStatus.CancelledAfterWriteStarted, "mid-write cancellation is distinguished from safe pre-write cancellation");
        Check(midCancelled.BytesWritten == 16 * 1024, "mid-write cancellation reports exact accepted prefix length");
        Check(midCancelled.DestinationMayBeModified && midCancelled.RequiresRecovery, "mid-write cancellation explicitly marks destination recovery requirement");
        Check(!midWriteSink.WasFlushed, "cancelled partial write is not reported as flushed completion");
    }

    var failingSink = new MemoryPhysicalMediaWriteSink(safeDisk)
    {
        FailOnWriteNumber = 2,
    };
    var failed = await execution.ExecuteAsync(
        executionPlan,
        executionPlan.ConfirmationToken,
        failingSink,
        bufferSizeBytes: 16 * 1024);
    Check(failed.Status == PhysicalMediaWriteExecutionStatus.FailedAfterWriteStarted, "injected destination failure after first chunk is explicit");
    Check(failed.BytesWritten == 16 * 1024, "failure reports only bytes accepted before the failing write");
    Check(failed.DestinationMayBeModified && failed.RequiresRecovery, "write failure after modification requires recovery");

    var flushFailSink = new MemoryPhysicalMediaWriteSink(safeDisk)
    {
        FailFlush = true,
    };
    var flushFailed = await execution.ExecuteAsync(
        executionPlan,
        executionPlan.ConfirmationToken,
        flushFailSink,
        bufferSizeBytes: 16 * 1024);
    Check(flushFailed.Status == PhysicalMediaWriteExecutionStatus.FailedAfterWriteStarted, "flush failure cannot be misreported as completed");
    Check(flushFailed.BytesWritten == sourceBytes.Length && flushFailed.RequiresRecovery, "flush failure preserves full accepted-byte evidence and requires recovery");

    var throwingProgressSink = new MemoryPhysicalMediaWriteSink(safeDisk);
    var throwingProgress = await execution.ExecuteAsync(
        executionPlan,
        executionPlan.ConfirmationToken,
        throwingProgressSink,
        new InlineProgress<PhysicalMediaWriteProgress>(_ => throw new InvalidOperationException("Injected observer failure.")),
        bufferSizeBytes: 16 * 1024);
    Check(throwingProgress.Status == PhysicalMediaWriteExecutionStatus.Completed, "progress observer failure is isolated from destructive I/O");
    Check(throwingProgressSink.WasFlushed, "observer failure cannot abort a completed physical write contract");
}
finally
{
    if (File.Exists(imagePath))
        File.Delete(imagePath);
}

if (failures == 0)
{
    Console.WriteLine("Dragon DiskForge physical media safety smoke tests passed.");
}
else
{
    Console.Error.WriteLine($"Dragon DiskForge physical media safety smoke tests failed: {failures} check(s).");
    Environment.ExitCode = 1;
}

PhysicalDiskInfo MakeDisk(
    int diskNumber,
    long? capacityBytes,
    string busType,
    bool isRemovable,
    bool isSystemDisk,
    string stableId,
    bool hasStableIdentity)
{
    return new PhysicalDiskInfo(
        diskNumber,
        $@"\\.\PhysicalDrive{diskNumber}",
        capacityBytes,
        busType,
        isRemovable,
        isSystemDisk,
        Vendor: "Dragon",
        Product: "TestDisk",
        Revision: "1.0",
        SerialNumber: hasStableIdentity ? "DRAGON-TEST-0007" : null,
        stableId,
        hasStableIdentity,
        Evidence: new[] { "fixture:test" });
}

void Check(bool condition, string message)
{
    if (condition)
    {
        Console.WriteLine($"PASS  {message}");
        return;
    }

    failures++;
    Console.Error.WriteLine($"FAIL  {message}");
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

sealed class MemoryPhysicalMediaWriteSink(PhysicalDiskInfo destination) : IPhysicalMediaWriteSink
{
    public PhysicalDiskInfo Destination { get; } = destination;
    public MemoryStream Written { get; } = new();
    public int WriteCount { get; private set; }
    public bool WasFlushed { get; private set; }
    public int? FailOnWriteNumber { get; init; }
    public bool FailFlush { get; init; }
    public Action<int>? AfterSuccessfulWrite { get; init; }

    public ValueTask WriteAsync(long offset, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (offset != Written.Length)
            throw new IOException($"Unexpected write offset {offset}; expected {Written.Length}.");

        WriteCount++;
        if (FailOnWriteNumber == WriteCount)
            throw new IOException("Injected destination write failure.");

        Written.Write(buffer.Span);
        AfterSuccessfulWrite?.Invoke(WriteCount);
        return ValueTask.CompletedTask;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailFlush)
            throw new IOException("Injected destination flush failure.");

        WasFlushed = true;
        return ValueTask.CompletedTask;
    }
}
