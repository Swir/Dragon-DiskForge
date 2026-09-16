using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IMediaGeometryProvider : IDiskImageProvider
{
    string DisplayName { get; }

    ValueTask<MediaGeometryInfo> ReadMediaGeometryAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
