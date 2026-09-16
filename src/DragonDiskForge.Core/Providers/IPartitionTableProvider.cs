using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IPartitionTableProvider : IDiskImageProvider
{
    string DisplayName { get; }

    ValueTask<PartitionTableInfo> ReadPartitionTableAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
