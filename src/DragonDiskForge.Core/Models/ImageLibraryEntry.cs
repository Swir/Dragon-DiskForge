namespace DragonDiskForge.Core.Models;

public sealed record ImageLibraryEntry(
    string Path,
    DateTimeOffset LastOpenedUtc,
    bool IsFavorite)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed record ImageLibrarySnapshot(
    IReadOnlyList<ImageLibraryEntry> Recents,
    IReadOnlyList<ImageLibraryEntry> Favorites);
