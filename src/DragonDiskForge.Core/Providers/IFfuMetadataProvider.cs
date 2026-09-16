using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IFfuMetadataProvider : IDiskImageProvider
{
    string DisplayName { get; }

    ValueTask<FfuMetadataInfo> ReadFfuMetadataAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
