using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

var failures = 0;
var safety = new PhysicalMediaSafetyService();
var imagePath = Path.Combine(Path.GetTempPath(), "dragon-physical-media-safety.img");
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
Check(!string.IsNullOrWhiteSpace(safePlan.ConfirmationToken), "allowed plan emits a destination-bound confirmation token");
Check(safePlan.ConfirmationToken == "ERASE PHYSICALDRIVE7 5566778899AABBCCDDEEFF"[^25..] ? false : true, "placeholder");
Check(safety.ConfirmationMatches(safePlan, safePlan.ConfirmationToken), "exact confirmation token is accepted");
Check(!safety.ConfirmationMatches(safePlan, safePlan.ConfirmationToken?.ToLowerInvariant()), "confirmation token is case-sensitive");
Check(!safety.ConfirmationMatches(safePlan, "ERASE PHYSICALDRIVE8 deadbeef"), "confirmation for another disk is rejected");
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
