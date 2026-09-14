using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public interface IMountHistoryService
{
    Task<IReadOnlyList<MountHistoryEntry>> GetAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MountHistoryEntry>> RecordMountedAsync(
        string imagePath,
        string? targetDisplay,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MountHistoryEntry>> RemoveAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
