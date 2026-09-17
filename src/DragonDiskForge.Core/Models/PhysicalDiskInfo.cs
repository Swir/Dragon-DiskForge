namespace DragonDiskForge.Core.Models;

public sealed record PhysicalDiskInfo(
    int DiskNumber,
    string DevicePath,
    long? CapacityBytes,
    string BusType,
    bool IsRemovable,
    bool IsSystemDisk,
    string? Vendor,
    string? Product,
    string? Revision,
    string? SerialNumber,
    string StableId,
    bool HasStableIdentity,
    IReadOnlyList<string> Evidence)
{
    public string DisplayName
    {
        get
        {
            var parts = new[] { Vendor, Product }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToArray();

            return parts.Length > 0
                ? string.Join(" ", parts)
                : $"PhysicalDrive{DiskNumber}";
        }
    }
}
