using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class PhysicalMediaSafetyService
{
    public PhysicalMediaWritePlan PreviewImageToDiskWrite(string sourcePath, long sourceLengthBytes, PhysicalDiskInfo destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(destination);

        var refusalReasons = new List<string>();
        var warnings = new List<string>();
        var sourceLooksPhysical = LooksLikePhysicalDevicePath(sourcePath);
        var fullSourcePath = sourceLooksPhysical ? sourcePath.Trim() : Path.GetFullPath(sourcePath);

        if (sourceLengthBytes <= 0)
            refusalReasons.Add("Source image length must be greater than zero.");

        if (sourceLooksPhysical)
            refusalReasons.Add("Source must be a regular image file; physical-device sources are refused by this write-plan contract.");

        if (destination.DiskNumber < 0 || string.IsNullOrWhiteSpace(destination.DevicePath))
            refusalReasons.Add("Destination physical-disk identity is incomplete.");

        if (string.Equals(fullSourcePath, destination.DevicePath, StringComparison.OrdinalIgnoreCase))
            refusalReasons.Add("Source and destination resolve to the same physical device.");

        if (!destination.HasStableIdentity || string.IsNullOrWhiteSpace(destination.StableId))
            refusalReasons.Add("Destination does not expose a stable hardware identity; destructive operations are refused.");

        if (destination.IsSystemDisk)
            refusalReasons.Add("Destination is a system disk; destructive operations are refused.");

        if (destination.CapacityBytes is null or <= 0)
        {
            refusalReasons.Add("Destination capacity is unknown; destructive operations are refused.");
        }
        else if (sourceLengthBytes > destination.CapacityBytes.Value)
        {
            refusalReasons.Add("Source image is larger than the destination physical disk.");
        }
        else if (sourceLengthBytes < destination.CapacityBytes.Value)
        {
            warnings.Add("Source image is smaller than the destination disk; trailing capacity would remain outside the written image.");
        }

        if (destination.IsRemovable)
            warnings.Add("Destination reports removable-media semantics.");

        if (destination.BusType.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Destination bus type could not be determined.");

        var isAllowed = refusalReasons.Count == 0;
        var confirmationToken = isAllowed ? BuildConfirmationToken(destination) : null;

        return new PhysicalMediaWritePlan(
            fullSourcePath,
            sourceLengthBytes,
            destination,
            isAllowed,
            RequiresExplicitConfirmation: isAllowed,
            confirmationToken,
            refusalReasons,
            warnings);
    }

    public bool ConfirmationMatches(PhysicalMediaWritePlan plan, string? suppliedToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsAllowed || !plan.RequiresExplicitConfirmation || string.IsNullOrWhiteSpace(plan.ConfirmationToken))
            return false;

        return string.Equals(plan.ConfirmationToken, suppliedToken?.Trim(), StringComparison.Ordinal);
    }

    private static bool LooksLikePhysicalDevicePath(string path)
    {
        var candidate = path.Trim();
        return candidate.StartsWith(@"\\.\PhysicalDrive", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(@"\\?\PhysicalDrive", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildConfirmationToken(PhysicalDiskInfo destination)
    {
        var stableSuffix = destination.StableId.Length > 12
            ? destination.StableId[^12..]
            : destination.StableId;

        return $"ERASE PHYSICALDRIVE{destination.DiskNumber} {stableSuffix.ToUpperInvariant()}";
    }
}
