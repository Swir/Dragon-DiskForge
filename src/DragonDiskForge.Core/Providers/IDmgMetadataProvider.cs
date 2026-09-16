using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IDmgMetadataProvider : IDiskImageProvider
{
    string DisplayName { get; }

    ValueTask<DmgMetadataInfo> ReadDmgMetadataAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
