using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

namespace DragonDiskForge.Windows.Services;

public sealed record WindowsPhysicalMediaWritePreflight(
    PhysicalDiskInfo Destination,
    int LogicalSectorSizeBytes,
    IReadOnlyList<int> SourceBackingDiskNumbers,
    bool IsAllowed,
    IReadOnlyList<string> RefusalReasons,
    IReadOnlyList<string> Evidence)
{
    public bool IsRefused => !IsAllowed;
}

/// <summary>
/// Revalidates Windows-specific facts that the platform-independent Core write plan cannot prove:
/// the current physical-disk identity, source-file backing disk and device sector geometry.
/// This service is read-only and never opens a destination for write access.
/// </summary>
public sealed class WindowsPhysicalMediaWritePreflightService
{
    private readonly IPhysicalDiskInventoryService _inventory;

    public WindowsPhysicalMediaWritePreflightService(IPhysicalDiskInventoryService? inventory = null)
    {
        _inventory = inventory ?? new WindowsPhysicalDiskInventoryService();
    }

    public async Task<WindowsPhysicalMediaWritePreflight> ValidateAsync(
        PhysicalMediaWritePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows physical-media preflight is implemented only for Windows.");

        cancellationToken.ThrowIfCancellationRequested();

        var refusals = new List<string>();
        var evidence = new List<string>
        {
            "windows-preflight:read-only",
            $"planned-disk-number:{plan.Destination.DiskNumber}"
        };

        if (!plan.IsAllowed)
        {
            refusals.Add("The Core physical-media write plan is refused and cannot enter Windows writer preflight.");
            refusals.AddRange(plan.RefusalReasons);
        }

        if (!File.Exists(plan.SourcePath))
            refusals.Add("Source image no longer exists at Windows writer preflight.");

        var disks = await _inventory.GetDisksAsync(cancellationToken).ConfigureAwait(false);
        var currentDestination = disks.FirstOrDefault(x => x.DiskNumber == plan.Destination.DiskNumber);
        if (currentDestination is null)
        {
            refusals.Add("Destination physical disk is no longer present.");
            return Result(plan.Destination, 0, Array.Empty<int>(), refusals, evidence);
        }

        evidence.Add($"current-device:{currentDestination.DevicePath}");
        evidence.Add($"current-stable-identity:{currentDestination.HasStableIdentity}");

        if (!string.Equals(
                currentDestination.DevicePath,
                plan.Destination.DevicePath,
                StringComparison.OrdinalIgnoreCase))
        {
            refusals.Add("Destination device path changed after planning.");
        }

        if (!currentDestination.HasStableIdentity
            || string.IsNullOrWhiteSpace(currentDestination.StableId)
            || !string.Equals(
                currentDestination.StableId,
                plan.Destination.StableId,
                StringComparison.Ordinal))
        {
            refusals.Add("Destination stable hardware identity changed or is no longer trustworthy.");
        }

        if (currentDestination.IsSystemDisk)
            refusals.Add("Destination currently resolves to a Windows system disk.");

        if (currentDestination.CapacityBytes is null or <= 0)
        {
            refusals.Add("Destination capacity is unavailable during Windows writer preflight.");
        }
        else
        {
            evidence.Add($"current-capacity-bytes:{currentDestination.CapacityBytes.Value}");

            if (plan.Destination.CapacityBytes is not null
                && currentDestination.CapacityBytes.Value != plan.Destination.CapacityBytes.Value)
            {
                refusals.Add("Destination capacity changed after planning.");
            }

            if (plan.SourceLengthBytes > currentDestination.CapacityBytes.Value)
                refusals.Add("Source image is larger than the current destination capacity.");
        }

        if (File.Exists(plan.SourcePath))
        {
            var currentSourceLength = new FileInfo(plan.SourcePath).Length;
            evidence.Add($"current-source-length:{currentSourceLength}");
            if (currentSourceLength != plan.SourceLengthBytes)
                refusals.Add("Source image length changed after planning.");
        }

        IReadOnlyList<int> sourceBackingDisks = Array.Empty<int>();
        var sourceIsUnc = plan.SourcePath.StartsWith(@"\\", StringComparison.Ordinal)
            && !plan.SourcePath.StartsWith(@"\\.\", StringComparison.Ordinal)
            && !plan.SourcePath.StartsWith(@"\\?\", StringComparison.Ordinal);

        if (File.Exists(plan.SourcePath))
        {
            if (sourceIsUnc)
            {
                evidence.Add("source-backing:remote-unc");
            }
            else
            {
                try
                {
                    sourceBackingDisks = WindowsPhysicalMediaInterop.GetBackingDiskNumbersForPath(plan.SourcePath);
                    foreach (var diskNumber in sourceBackingDisks)
                        evidence.Add($"source-backing-disk:{diskNumber}");

                    if (sourceBackingDisks.Count == 0)
                    {
                        refusals.Add("Source image backing disk could not be proven for the local source path.");
                    }
                    else if (sourceBackingDisks.Contains(currentDestination.DiskNumber))
                    {
                        refusals.Add("Source image is stored on the destination physical disk; destructive write is refused.");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    refusals.Add($"Source image backing-disk resolution failed: {ex.Message}");
                }
            }
        }

        var logicalSectorSize = 0;
        try
        {
            logicalSectorSize = WindowsPhysicalMediaInterop.GetLogicalSectorSize(currentDestination.DevicePath);
            evidence.Add($"logical-sector-bytes:{logicalSectorSize}");

            if (plan.SourceLengthBytes <= 0 || plan.SourceLengthBytes % logicalSectorSize != 0)
            {
                refusals.Add(
                    $"Source image length must be a positive multiple of the destination logical sector size ({logicalSectorSize} bytes).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            refusals.Add($"Destination logical sector size could not be proven: {ex.Message}");
        }

        return Result(currentDestination, logicalSectorSize, sourceBackingDisks, refusals, evidence);
    }

    private static WindowsPhysicalMediaWritePreflight Result(
        PhysicalDiskInfo destination,
        int logicalSectorSize,
        IReadOnlyList<int> sourceBackingDisks,
        List<string> refusals,
        List<string> evidence)
        => new(
            destination,
            logicalSectorSize,
            sourceBackingDisks,
            IsAllowed: refusals.Count == 0,
            RefusalReasons: refusals.ToArray(),
            Evidence: evidence.ToArray());
}
