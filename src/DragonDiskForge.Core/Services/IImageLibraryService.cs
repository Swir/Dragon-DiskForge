using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public interface IImageLibraryService
{
    Task<ImageLibrarySnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task<ImageLibrarySnapshot> RecordOpenedAsync(string imagePath, CancellationToken cancellationToken = default);
    Task<ImageLibrarySnapshot> SetFavoriteAsync(string imagePath, bool isFavorite, CancellationToken cancellationToken = default);
    Task<ImageLibrarySnapshot> RemoveAsync(string imagePath, CancellationToken cancellationToken = default);
}
