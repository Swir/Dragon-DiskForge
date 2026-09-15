using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public interface IDirectBrowseProvider : IDiskImageProvider
{
    string DisplayName { get; }

    Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string imagePath,
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExplorerEntry>> SearchAsync(
        string imagePath,
        string startPath,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default);

    Task CopyOutAsync(
        string imagePath,
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
