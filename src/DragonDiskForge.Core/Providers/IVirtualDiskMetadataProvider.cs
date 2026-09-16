using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IVirtualDiskMetadataProvider : IDiskImageProvider
{
    string DisplayName { get; }

    ValueTask<VirtualDiskMetadataInfo> ReadVirtualDiskMetadataAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
