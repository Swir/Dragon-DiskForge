using System.Text.Json;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class JsonImageLibraryService : IImageLibraryService, IDisposable
{
    private readonly string _storagePath;
    private readonly int _maxRecents;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonImageLibraryService(string storagePath, int maxRecents = 30)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("Image-library storage path cannot be empty.", nameof(storagePath));
        if (maxRecents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRecents));

        _storagePath = Path.GetFullPath(storagePath);
        _maxRecents = maxRecents;
    }

    public async Task<ImageLibrarySnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadUnsafeAsync(cancellationToken);
            return BuildSnapshot(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImageLibrarySnapshot> RecordOpenedAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(imagePath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadUnsafeAsync(cancellationToken);
            var existing = Find(entries, normalized);
            var favorite = existing?.IsFavorite ?? false;
            if (existing is not null)
                entries.Remove(existing);

            entries.Add(new ImageLibraryEntry(normalized, DateTimeOffset.UtcNow, favorite));
            Prune(entries);
            await SaveUnsafeAsync(entries, cancellationToken);
            return BuildSnapshot(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImageLibrarySnapshot> SetFavoriteAsync(
        string imagePath,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(imagePath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadUnsafeAsync(cancellationToken);
            var existing = Find(entries, normalized);
            if (existing is not null)
                entries.Remove(existing);

            entries.Add(new ImageLibraryEntry(
                normalized,
                existing?.LastOpenedUtc ?? DateTimeOffset.UtcNow,
                isFavorite));
            Prune(entries);
            await SaveUnsafeAsync(entries, cancellationToken);
            return BuildSnapshot(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImageLibrarySnapshot> RemoveAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(imagePath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadUnsafeAsync(cancellationToken);
            entries.RemoveAll(x => _pathComparer.Equals(x.Path, normalized));
            await SaveUnsafeAsync(entries, cancellationToken);
            return BuildSnapshot(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ImageLibraryEntry>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storagePath))
            return new List<ImageLibraryEntry>();

        await using var stream = new FileStream(
            _storagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var entries = await JsonSerializer.DeserializeAsync<List<ImageLibraryEntry>>(
            stream,
            _jsonOptions,
            cancellationToken) ?? new List<ImageLibraryEntry>();

        var deduplicated = new List<ImageLibraryEntry>();
        foreach (var entry in entries
                     .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                     .OrderByDescending(x => x.LastOpenedUtc))
        {
            string normalized;
            try
            {
                normalized = Path.GetFullPath(entry.Path);
            }
            catch
            {
                continue;
            }

            if (deduplicated.Any(x => _pathComparer.Equals(x.Path, normalized)))
                continue;

            deduplicated.Add(entry with { Path = normalized });
        }

        Prune(deduplicated);
        return deduplicated;
    }

    private async Task SaveUnsafeAsync(List<ImageLibraryEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storagePath)
            ?? throw new InvalidOperationException("Image-library storage path has no parent directory.");
        Directory.CreateDirectory(directory);

        var tempPath = _storagePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, entries, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _storagePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private void Prune(List<ImageLibraryEntry> entries)
    {
        var keepRecent = entries
            .OrderByDescending(x => x.LastOpenedUtc)
            .Take(_maxRecents)
            .Select(x => x.Path)
            .ToHashSet(_pathComparer);

        entries.RemoveAll(x => !x.IsFavorite && !keepRecent.Contains(x.Path));
    }

    private ImageLibrarySnapshot BuildSnapshot(IEnumerable<ImageLibraryEntry> entries)
    {
        var ordered = entries
            .OrderByDescending(x => x.LastOpenedUtc)
            .ToArray();

        var recents = ordered.Take(_maxRecents).ToArray();
        var favorites = ordered.Where(x => x.IsFavorite).ToArray();
        return new ImageLibrarySnapshot(recents, favorites);
    }

    private ImageLibraryEntry? Find(IEnumerable<ImageLibraryEntry> entries, string normalizedPath)
        => entries.FirstOrDefault(x => _pathComparer.Equals(x.Path, normalizedPath));

    private static string NormalizePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path cannot be empty.", nameof(imagePath));
        return Path.GetFullPath(imagePath);
    }

    public void Dispose() => _gate.Dispose();
}
