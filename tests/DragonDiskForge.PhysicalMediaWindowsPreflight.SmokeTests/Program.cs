using DragonDiskForge.Core.Models;
using DragonDiskForge.Windows.Services;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("SKIP  Windows physical-media preflight smoke tests require Windows.");
    return;
}

var failures = 0;
var inventory = new WindowsPhysicalDiskInventoryService();
var disks = await inventory.GetDisksAsync();
var systemDisk = disks.FirstOrDefault(x => x.IsSystemDisk);
Check(systemDisk is not null, "physical inventory exposes at least one Windows system disk");

if (systemDisk is not null)
{
    var sourcePath = Path.Combine(Path.GetTempPath(), $"dragon-physical-preflight-{Guid.NewGuid():N}.img");
    try
    {
        var payload = new byte[1024 * 1024];
        for (var i = 0; i < payload.Length; i += 4096)
            payload[i] = (byte)(i / 4096 % 251);
        await File.WriteAllBytesAsync(sourcePath, payload);

        // The Core layer would independently refuse a system disk. This deliberately constructs
        // an allowed-shaped plan so the Windows layer itself must prove both system-disk refusal
        // and that the local source resides on that same physical disk.
        var syntheticPlan = new PhysicalMediaWritePlan(
            sourcePath,
            payload.LongLength,
            systemDisk,
            IsAllowed: true,
            RequiresExplicitConfirmation: true,
            ConfirmationToken: "TEST-ONLY-NOT-A-REAL-CONFIRMATION",
            RefusalReasons: Array.Empty<string>(),
            Warnings: Array.Empty<string>());

        var service = new WindowsPhysicalMediaWritePreflightService(inventory);
        var result = await service.ValidateAsync(syntheticPlan);

        Check(result.IsRefused, "Windows writer preflight refuses the synthetic system-disk plan");
        Check(result.RefusalReasons.Any(x => x.Contains("system disk", StringComparison.OrdinalIgnoreCase)),
            "Windows writer preflight independently refuses current system-disk identity");
        Check(result.SourceBackingDiskNumbers.Contains(systemDisk.DiskNumber),
            "Windows writer preflight maps the local source file back to its physical system disk");
        Check(result.RefusalReasons.Any(x => x.Contains("stored on the destination", StringComparison.OrdinalIgnoreCase)),
            "Windows writer preflight independently refuses a source stored on the target physical disk");
        Check(result.LogicalSectorSizeBytes >= 512,
            "Windows writer preflight proves a plausible destination logical sector size");
        Check(result.Evidence.Contains($"source-backing-disk:{systemDisk.DiskNumber}"),
            "Windows writer preflight retains source-backing provenance");
        Check(result.Evidence.Any(x => x.StartsWith("logical-sector-bytes:", StringComparison.Ordinal)),
            "Windows writer preflight retains logical-sector provenance");

        var futureEvidencePath = Path.Combine(Path.GetDirectoryName(sourcePath)!, $"dragon-evidence-{Guid.NewGuid():N}.json");
        var evidenceBackingDisks = WindowsPhysicalMediaPathEvidence.GetLocalBackingDiskNumbers(futureEvidencePath);
        Check(evidenceBackingDisks.Contains(systemDisk.DiskNumber),
            "local-path evidence resolves a future evidence file through its existing parent volume");

        var uncRejected = false;
        try
        {
            _ = WindowsPhysicalMediaPathEvidence.GetLocalBackingDiskNumbers(@"\\server\share\evidence.json");
        }
        catch (InvalidDataException)
        {
            uncRejected = true;
        }
        Check(uncRejected, "local-path evidence rejects UNC/device-namespace storage before any destructive path");

        await ExpectOpenRefusalAsync(
            syntheticPlan,
            "WRONG-CONFIRMATION",
            service,
            "physical writer rejects a wrong token before Windows destructive access");
        await ExpectOpenRefusalAsync(
            syntheticPlan,
            syntheticPlan.ConfirmationToken!,
            service,
            "physical writer rejects system/source-on-target preflight before Windows destructive access");
    }
    finally
    {
        if (File.Exists(sourcePath))
            File.Delete(sourcePath);
    }
}

if (failures == 0)
{
    Console.WriteLine("Dragon DiskForge Windows physical-media preflight smoke tests passed.");
}
else
{
    Console.Error.WriteLine($"Dragon DiskForge Windows physical-media preflight smoke tests failed: {failures} check(s).");
    Environment.ExitCode = 1;
}

async Task ExpectOpenRefusalAsync(
    PhysicalMediaWritePlan plan,
    string confirmation,
    WindowsPhysicalMediaWritePreflightService service,
    string message)
{
    try
    {
        await using var sink = await WindowsPhysicalMediaWriteSink.OpenAsync(plan, confirmation, service);
        Check(false, message);
    }
    catch (InvalidOperationException)
    {
        Check(true, message);
    }
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
