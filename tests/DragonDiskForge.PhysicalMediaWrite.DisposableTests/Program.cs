using System.Security.Principal;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using DragonDiskForge.Windows.Services;

const string OptInVariable = "DDF_DISPOSABLE_WRITE_OPT_IN";
const string OptInPhrase = "ERASE_DISPOSABLE_MEDIA_FOR_DRAGON_DISKFORGE_TEST";
const string FixedMediaVariable = "DDF_ALLOW_FIXED_DISPOSABLE_MEDIA";
const string FixedMediaPhrase = "I_HAVE_VERIFIED_THIS_FIXED_DISK_IS_DISPOSABLE";

if (!string.Equals(Environment.GetEnvironmentVariable(OptInVariable), OptInPhrase, StringComparison.Ordinal))
{
    Console.WriteLine($"SKIP  Disposable physical-media write harness is locked. Set {OptInVariable} to the exact documented opt-in phrase only on dedicated disposable test media.");
    return;
}

if (!OperatingSystem.IsWindows())
    Fail("Disposable physical-media validation requires Windows.");
if (!IsAdministrator())
    Fail("Disposable physical-media validation requires an elevated administrator session.");

var diskNumber = ParseDiskNumber(Environment.GetEnvironmentVariable("DDF_DISPOSABLE_DISK_NUMBER"));
var expectedStableId = Require("DDF_DISPOSABLE_STABLE_ID");
var sourcePath = Path.GetFullPath(Require("DDF_DISPOSABLE_IMAGE_PATH"));

if (!File.Exists(sourcePath))
    Fail($"Source image does not exist: {sourcePath}");

var inventory = new WindowsPhysicalDiskInventoryService();
var disks = await inventory.GetDisksAsync();
var destination = disks.SingleOrDefault(x => x.DiskNumber == diskNumber)
    ?? throw new InvalidOperationException($"PhysicalDrive{diskNumber} is not present.");

if (!destination.HasStableIdentity || string.IsNullOrWhiteSpace(destination.StableId))
    Fail("Selected disposable target does not expose a stable serial-backed identity.");
if (!string.Equals(destination.StableId, expectedStableId, StringComparison.Ordinal))
    Fail("Selected target stable identity does not exactly match DDF_DISPOSABLE_STABLE_ID.");
if (destination.IsSystemDisk)
    Fail("Selected target is a Windows system disk.");

var disposableMediaEvidence = destination.IsRemovable
    || destination.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase)
    || destination.BusType.Equals("SD", StringComparison.OrdinalIgnoreCase)
    || destination.BusType.Equals("MMC", StringComparison.OrdinalIgnoreCase);

if (!disposableMediaEvidence
    && !string.Equals(Environment.GetEnvironmentVariable(FixedMediaVariable), FixedMediaPhrase, StringComparison.Ordinal))
{
    Fail(
        $"PhysicalDrive{diskNumber} does not report removable/USB/SD/MMC evidence. " +
        $"A dedicated fixed test disk requires the additional exact {FixedMediaVariable} acknowledgement.");
}

var sourceLength = new FileInfo(sourcePath).Length;
var safety = new PhysicalMediaSafetyService();
var plan = safety.PreviewImageToDiskWrite(sourcePath, sourceLength, destination);
if (!plan.IsAllowed || string.IsNullOrWhiteSpace(plan.ConfirmationToken))
    Fail($"Core safety plan refused the target: {string.Join(" ", plan.RefusalReasons)}");

var preflightService = new WindowsPhysicalMediaWritePreflightService(inventory);
var preflight = await preflightService.ValidateAsync(plan);
if (preflight.IsRefused)
    Fail($"Windows physical-media preflight refused the target: {string.Join(" ", preflight.RefusalReasons)}");

Console.WriteLine($"DANGER  This validation will erase the beginning of PhysicalDrive{diskNumber}: {destination.DisplayName}");
Console.WriteLine($"INFO    Device path: {destination.DevicePath}");
Console.WriteLine($"INFO    Stable ID: {destination.StableId}");
Console.WriteLine($"INFO    Capacity: {destination.CapacityBytes} bytes");
Console.WriteLine($"INFO    Source: {sourcePath} ({sourceLength} bytes)");
Console.WriteLine($"INFO    Logical sector: {preflight.LogicalSectorSizeBytes} bytes");
Console.WriteLine($"INFO    Confirmation binding: {plan.ConfirmationToken}");

var verifier = new ImageVerificationService();
var sourceSha256 = await verifier.ComputeSha256Async(sourcePath);
var execution = new PhysicalMediaWriteExecutionService(safety);
var bufferSize = ChooseAlignedBufferSize(preflight.LogicalSectorSizeBytes);
PhysicalMediaWriteExecutionResult result;

await using (var sink = await WindowsPhysicalMediaWriteSink.OpenAsync(plan, preflightService))
{
    result = await execution.ExecuteAsync(
        plan,
        plan.ConfirmationToken,
        sink,
        new ConsoleWriteProgress(),
        bufferSizeBytes: bufferSize);
}

if (result.Status != PhysicalMediaWriteExecutionStatus.Completed)
{
    Fail(
        $"Physical write did not complete: status={result.Status}; bytes={result.BytesWritten}/{result.TotalBytes}; " +
        $"recoveryRequired={result.RequiresRecovery}; error={result.ErrorMessage}");
}

if (!string.Equals(result.WrittenSha256Hex, sourceSha256, StringComparison.Ordinal))
    Fail("Execution SHA-256 evidence does not match the source image hash.");

var readback = new WindowsPhysicalMediaReadbackVerifier(inventory);
var readbackSha256 = await readback.ComputePrefixSha256Async(destination, sourceLength);
if (!string.Equals(readbackSha256, sourceSha256, StringComparison.Ordinal))
    Fail("Physical-device read-back SHA-256 does not match the source image. The disposable target requires recovery/rewrite.");

Console.WriteLine();
Console.WriteLine("PASS  Disposable physical-media write + flush + read-back SHA-256 validation completed.");
Console.WriteLine($"PASS  SHA-256: {readbackSha256}");

static int ChooseAlignedBufferSize(int sectorSize)
{
    if (sectorSize <= 0)
        throw new ArgumentOutOfRangeException(nameof(sectorSize));

    var target = PhysicalMediaWriteExecutionService.DefaultBufferSizeBytes;
    var aligned = target / sectorSize * sectorSize;
    if (aligned < PhysicalMediaWriteExecutionService.MinimumBufferSizeBytes)
        aligned = sectorSize;

    if (aligned < PhysicalMediaWriteExecutionService.MinimumBufferSizeBytes
        || aligned > PhysicalMediaWriteExecutionService.MaximumBufferSizeBytes)
    {
        throw new InvalidOperationException($"No supported execution buffer can satisfy the {sectorSize}-byte sector alignment.");
    }

    return aligned;
}

static int ParseDiskNumber(string? value)
{
    if (!int.TryParse(value, out var diskNumber) || diskNumber < 0)
        Fail("DDF_DISPOSABLE_DISK_NUMBER must contain a non-negative physical disk number.");
    return diskNumber;
}

static string Require(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
        Fail($"Required environment variable {name} is missing.");
    return value!;
}

static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

static void Fail(string message)
    => throw new InvalidOperationException(message);

sealed class ConsoleWriteProgress : IProgress<PhysicalMediaWriteProgress>
{
    private int _lastPercent = -1;

    public void Report(PhysicalMediaWriteProgress value)
    {
        var percent = value.TotalBytes <= 0
            ? 0
            : (int)Math.Min(100, value.BytesWritten * 100 / value.TotalBytes);
        if (percent == _lastPercent)
            return;

        _lastPercent = percent;
        Console.WriteLine($"WRITE   {percent,3}%  {value.BytesWritten}/{value.TotalBytes} bytes");
    }
}
