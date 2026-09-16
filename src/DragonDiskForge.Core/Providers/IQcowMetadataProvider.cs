using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IQcowMetadataProvider : IDiskImageProvider
{
    string DisplayName { get; }

    ValueTask<QcowMetadataInfo> ReadQcowMetadataAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
