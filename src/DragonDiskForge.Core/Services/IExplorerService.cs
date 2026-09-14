using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public interface IExplorerService
{
    Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string rootPath,
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExplorerEntry>> SearchAsync(
        string rootPath,
        string startPath,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default);

    Task CopyOutAsync(
        string rootPath,
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
