using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public interface IDirectImageExplorer
{
    string ProviderId { get; }
    string ImagePath { get; }
    string RootPath { get; }

    Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExplorerEntry>> SearchAsync(
        string startPath,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default);

    Task CopyOutAsync(
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
