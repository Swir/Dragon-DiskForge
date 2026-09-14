using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public interface IFilePreviewService
{
    Task<PreviewInfo> GetPreviewAsync(
        string filePath,
        int maxTextCharacters = 200_000,
        CancellationToken cancellationToken = default);
}
