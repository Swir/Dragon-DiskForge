using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public interface IPhysicalDiskInventoryService
{
    Task<IReadOnlyList<PhysicalDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default);
}
