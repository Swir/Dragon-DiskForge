using DragonDiskForge.Core.Services;

namespace DragonDiskForge.Core.Providers;

public interface IDirectBrowseProvider
{
    string Id { get; }
    IReadOnlyCollection<string> Extensions { get; }

    ValueTask<bool> CanHandleAsync(
        string imagePath,
        CancellationToken cancellationToken = default);

    ValueTask<IDirectImageExplorer> OpenAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}
