using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IDiskImageProvider
{
    string Id { get; }
    IReadOnlyCollection<string> Extensions { get; }
    ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default);
    ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default);
}
