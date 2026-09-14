using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public interface IMountService
{
    bool CanHandle(string imagePath);
    bool RequiresElevation(string imagePath);

    Task<MountState> GetStateAsync(
        string imagePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MountState>> GetMountedAsync(
        CancellationToken cancellationToken = default);

    Task<MountState> MountAsync(
        MountRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<MountState> UnmountAsync(
        string imagePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
