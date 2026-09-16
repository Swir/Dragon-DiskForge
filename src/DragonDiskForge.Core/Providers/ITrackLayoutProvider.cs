using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface ITrackLayoutProvider : IDiskImageProvider
{
    string DisplayName { get; }

    ValueTask<OpticalTrackLayoutInfo> ReadTrackLayoutAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
