using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IWimMetadataProvider : IDiskImageProvider
{
    string DisplayName { get; }

    ValueTask<WimMetadataInfo> ReadWimMetadataAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
