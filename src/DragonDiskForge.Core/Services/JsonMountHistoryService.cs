using System.Text.Json;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class JsonMountHistoryService : IMountHistoryService, IDisposable
{
    private readonly string _storagePath;
    private readonly int _maxEntries;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonMountHistoryService(string storagePath, int maxEntries = 50)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("Mount-history storage path cannot be empty.", nameof(storagePath));
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries));

        _storagePath = Path.GetFullPath(storagePath);
        _maxEntries = maxEntries;
    }

    public async Task<IReadOnlyList<MountHistoryEntry>> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return BuildSnapshot(await LoadUnsafeAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MountHistoryEntry>> RecordMountedAsync(
        string imagePath,
        string? targetDisplay,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(imagePath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = await LoadUnsafeAsync(cancellationToken);
            entries.RemoveAll(x => _pathComparer.Equals(x.Path, normalized));
            entries.Add(new MountHistoryEntry(
                normalized,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(targetDisplay) ? null : targetDisplay.Trim()));
            Prune(entries);
            await SaveUnsafeAsync(entries, cancellationToken);
            return BuildSnapshot(entries);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MountHistoryEntry>> RemoveAsync(
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

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_storagePath))
                File.Delete(_storagePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<MountHistoryEntry>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storagePath))
            return new List<MountHistoryEntry>();

        await using var stream = new FileStream(
            _storagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var entries = await JsonSerializer.DeserializeAsync<List<MountHistoryEntry>>(
            stream,
            _jsonOptions,
            cancellationToken) ?? new List<MountHistoryEntry>();

        var deduplicated = new List<MountHistoryEntry>();
        foreach (var entry in entries
                     .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                     .OrderByDescending(x => x.LastSeenMountedUtc))
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

    private async Task SaveUnsafeAsync(List<MountHistoryEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storagePath)
            ?? throw new InvalidOperationException("Mount-history storage path has no parent directory.");
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

    private IReadOnlyList<MountHistoryEntry> BuildSnapshot(IEnumerable<MountHistoryEntry> entries)
        => entries
            .OrderByDescending(x => x.LastSeenMountedUtc)
            .Take(_maxEntries)
            .ToArray();

    private void Prune(List<MountHistoryEntry> entries)
    {
        if (entries.Count <= _maxEntries)
            return;

        var keep = entries
            .OrderByDescending(x => x.LastSeenMountedUtc)
            .Take(_maxEntries)
            .Select(x => x.Path)
            .ToHashSet(_pathComparer);
        entries.RemoveAll(x => !keep.Contains(x.Path));
    }

    private static string NormalizePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path cannot be empty.", nameof(imagePath));
        return Path.GetFullPath(imagePath);
    }

    public void Dispose() => _gate.Dispose();
}
