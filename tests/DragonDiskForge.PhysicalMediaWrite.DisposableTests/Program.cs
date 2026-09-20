using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using DragonDiskForge.Windows.Services;

const string OptInVariable = "DDF_DISPOSABLE_WRITE_OPT_IN";
const string OptInPhrase = "ERASE_DISPOSABLE_MEDIA_FOR_DRAGON_DISKFORGE_TEST";
const string FixedMediaVariable = "DDF_ALLOW_FIXED_DISPOSABLE_MEDIA";
const string FixedMediaPhrase = "I_HAVE_VERIFIED_THIS_FIXED_DISK_IS_DISPOSABLE";
const string EvidencePathVariable = "DDF_DISPOSABLE_EVIDENCE_PATH";

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
var evidencePath = ValidateEvidencePath(Require(EvidencePathVariable), sourcePath);
var evidenceSidecarPath = evidencePath + ".sha256";

if (!File.Exists(sourcePath))
    Fail($"Source image does not exist: {sourcePath}");
if (File.Exists(evidencePath) || File.Exists(evidenceSidecarPath))
    Fail("Disposable-media evidence output already exists. Choose a fresh .json path so a prior hardware witness can never be overwritten.");

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

var suppliedConfirmationToken = Require("DDF_DISPOSABLE_CONFIRMATION_TOKEN");
if (!safety.ConfirmationMatches(plan, suppliedConfirmationToken))
{
    Fail(
        "DDF_DISPOSABLE_CONFIRMATION_TOKEN does not exactly match the current destination-bound token. " +
        $"Expected: {plan.ConfirmationToken}");
}

var preflightService = new WindowsPhysicalMediaWritePreflightService(inventory);
var preflight = await preflightService.ValidateAsync(plan);
if (preflight.IsRefused)
    Fail($"Windows physical-media preflight refused the target: {string.Join(" ", preflight.RefusalReasons)}");
if (preflight.SourceBackingDiskNumbers.Count == 0)
    Fail("Disposable-media evidence requires at least one proven source backing disk.");
if (preflight.SourceBackingDiskNumbers.Contains(destination.DiskNumber))
    Fail("Source backing-disk evidence resolves to the destructive destination; validation is refused.");

var evidenceBackingDiskNumbers = WindowsPhysicalMediaPathEvidence.GetLocalBackingDiskNumbers(evidencePath);
if (evidenceBackingDiskNumbers.Contains(destination.DiskNumber))
{
    Fail(
        "DDF_DISPOSABLE_EVIDENCE_PATH resolves to the destructive destination physical disk. " +
        "Evidence must be stored on separately proven local storage.");
}

Console.WriteLine($"DANGER  This validation will erase the beginning of PhysicalDrive{diskNumber}: {destination.DisplayName}");
Console.WriteLine($"INFO    Device path: {destination.DevicePath}");
Console.WriteLine($"INFO    Stable ID: {destination.StableId}");
Console.WriteLine($"INFO    Capacity: {destination.CapacityBytes} bytes");
Console.WriteLine($"INFO    Source: {sourcePath} ({sourceLength} bytes)");
Console.WriteLine($"INFO    Logical sector: {preflight.LogicalSectorSizeBytes} bytes");
Console.WriteLine($"INFO    Confirmation binding: {plan.ConfirmationToken}");
Console.WriteLine($"INFO    Evidence output: {evidencePath}");
Console.WriteLine($"INFO    Evidence backing disks: {string.Join(",", evidenceBackingDiskNumbers)}");

// Keep a read-only handle open without FileShare.Write/Delete while hashing and writing. This
// closes the same-length source-mutation gap between pre-hash and destructive execution.
await using var sourceMutationLock = new FileStream(
    sourcePath,
    new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        Options = FileOptions.SequentialScan,
        BufferSize = 4096,
    });
if (sourceMutationLock.Length != sourceLength)
    Fail("Source image length changed while acquiring the source mutation lock.");

var verifier = new ImageVerificationService();
var sourceSha256 = await verifier.ComputeSha256Async(sourcePath);
var execution = new PhysicalMediaWriteExecutionService(safety);
var bufferSize = ChooseAlignedBufferSize(preflight.LogicalSectorSizeBytes);
PhysicalMediaWriteExecutionResult result;

await using (var sink = await WindowsPhysicalMediaWriteSink.OpenAsync(
                 plan,
                 suppliedConfirmationToken,
                 preflightService))
{
    result = await execution.ExecuteAsync(
        plan,
        suppliedConfirmationToken,
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
    Fail("Execution SHA-256 evidence does not match the locked source image hash.");

var readback = new WindowsPhysicalMediaReadbackVerifier(inventory);
var readbackSha256 = await readback.ComputePrefixSha256Async(destination, sourceLength);
if (!string.Equals(readbackSha256, sourceSha256, StringComparison.Ordinal))
    Fail("Physical-device read-back SHA-256 does not match the source image. The disposable target requires recovery/rewrite.");

var evidence = new DisposablePhysicalMediaEvidence(
    SchemaVersion: 1,
    Result: "pass",
    CompletedUtc: DateTimeOffset.UtcNow,
    OsVersion: Environment.OSVersion.VersionString,
    ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
    DiskNumber: destination.DiskNumber,
    DevicePath: destination.DevicePath,
    StableId: destination.StableId!,
    DisplayName: destination.DisplayName,
    CapacityBytes: destination.CapacityBytes!.Value,
    BusType: destination.BusType,
    IsRemovable: destination.IsRemovable,
    SourceFileName: Path.GetFileName(sourcePath),
    SourceLengthBytes: sourceLength,
    SourceSha256: sourceSha256,
    SourceBackingDiskNumbers: preflight.SourceBackingDiskNumbers.ToArray(),
    EvidenceBackingDiskNumbers: evidenceBackingDiskNumbers.ToArray(),
    LogicalSectorSizeBytes: preflight.LogicalSectorSizeBytes,
    ExecutionBufferSizeBytes: bufferSize,
    BytesWritten: result.BytesWritten,
    ExecutionStatus: result.Status.ToString(),
    ExecutionWrittenSha256: result.WrittenSha256Hex!,
    RequiresRecovery: result.RequiresRecovery,
    TargetVolumeLockDismountCompleted: true,
    DeviceFlushCompleted: true,
    ReadbackVerified: true,
    ReadbackSha256: readbackSha256,
    ConfirmationTokenSha256: Sha256Hex(suppliedConfirmationToken),
    PreflightEvidence: preflight.Evidence.ToArray());

await WriteEvidencePackageAsync(evidencePath, evidence);

Console.WriteLine();
Console.WriteLine("PASS  Disposable physical-media write + flush + read-back SHA-256 validation completed.");
Console.WriteLine($"PASS  SHA-256: {readbackSha256}");
Console.WriteLine($"PASS  Evidence: {evidencePath}");
Console.WriteLine($"PASS  Evidence SHA-256 sidecar: {evidenceSidecarPath}");

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

static string ValidateEvidencePath(string value, string sourcePath)
{
    if (value.StartsWith("\\\\", StringComparison.Ordinal))
        Fail("DDF_DISPOSABLE_EVIDENCE_PATH must be a local filesystem path, not UNC/device namespace storage.");

    var fullPath = Path.GetFullPath(value);
    if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
        Fail("DDF_DISPOSABLE_EVIDENCE_PATH must end in .json.");
    if (string.Equals(fullPath, sourcePath, StringComparison.OrdinalIgnoreCase))
        Fail("Evidence output cannot overwrite the source image.");

    var parent = Path.GetDirectoryName(fullPath);
    if (string.IsNullOrWhiteSpace(parent))
        Fail("Evidence output must have a valid parent directory.");

    Directory.CreateDirectory(parent!);
    return fullPath;
}

static async Task WriteEvidencePackageAsync(string evidencePath, DisposablePhysicalMediaEvidence evidence)
{
    var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    var sidecarPath = evidencePath + ".sha256";
    var tempEvidence = evidencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    var tempSidecar = sidecarPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

    if (File.Exists(evidencePath) || File.Exists(sidecarPath))
        throw new IOException("Evidence package destination already exists and will not be overwritten.");

    try
    {
        await File.WriteAllBytesAsync(tempEvidence, bytes).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            tempSidecar,
            $"{hash}  {Path.GetFileName(evidencePath)}{Environment.NewLine}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);

        File.Move(tempEvidence, evidencePath, overwrite: false);
        try
        {
            File.Move(tempSidecar, sidecarPath, overwrite: false);
        }
        catch
        {
            File.Delete(evidencePath);
            throw;
        }
    }
    finally
    {
        File.Delete(tempEvidence);
        File.Delete(tempSidecar);
    }
}

static string Sha256Hex(string value)
    => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static bool IsAdministrator()
{
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
}

static void Fail(string message)
    => throw new InvalidOperationException(message);

sealed record DisposablePhysicalMediaEvidence(
    int SchemaVersion,
    string Result,
    DateTimeOffset CompletedUtc,
    string OsVersion,
    string ProcessArchitecture,
    int DiskNumber,
    string DevicePath,
    string StableId,
    string DisplayName,
    long CapacityBytes,
    string BusType,
    bool IsRemovable,
    string SourceFileName,
    long SourceLengthBytes,
    string SourceSha256,
    int[] SourceBackingDiskNumbers,
    int[] EvidenceBackingDiskNumbers,
    int LogicalSectorSizeBytes,
    int ExecutionBufferSizeBytes,
    long BytesWritten,
    string ExecutionStatus,
    string ExecutionWrittenSha256,
    bool RequiresRecovery,
    bool TargetVolumeLockDismountCompleted,
    bool DeviceFlushCompleted,
    bool ReadbackVerified,
    string ReadbackSha256,
    string ConfirmationTokenSha256,
    string[] PreflightEvidence);

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
