using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public interface IMountHistoryService
{
    Task<IReadOnlyList<MountHistoryEntry>> GetAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MountHistoryEntry>> RecordAsync(
        string imagePath,
        MountHistoryAction action,
        string? targetDisplay = null,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
