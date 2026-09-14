using System.Text.Json;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class JsonMountHistoryService : IMountHistoryService, IDisposable
{
    private readonly string _storagePath;
    private readonly int _maxEntries;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonMountHistoryService(string storagePath, int maxEntries = 100)
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
            return await LoadUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MountHistoryEntry>> RecordAsync(
        string imagePath,
        MountHistoryAction action,
        string? targetDisplay = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action));

        var normalized = NormalizePath(imagePath);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entries = (await LoadUnsafeAsync(cancellationToken)).ToList();
            entries.Add(new MountHistoryEntry(
                normalized,
                action,
                DateTimeOffset.UtcNow,
                NormalizeTarget(targetDisplay)));

            var pruned = NormalizeAndPrune(entries);
            await SaveUnsafeAsync(pruned, cancellationToken);
            return pruned;
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
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_storagePath))
                File.Delete(_storagePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<MountHistoryEntry>> LoadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storagePath))
            return Array.Empty<MountHistoryEntry>();

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

        return NormalizeAndPrune(entries);
    }

    private IReadOnlyList<MountHistoryEntry> NormalizeAndPrune(IEnumerable<MountHistoryEntry> entries)
    {
        var valid = new List<MountHistoryEntry>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.ImagePath) || !Enum.IsDefined(entry.Action))
                continue;

            string normalized;
            try
            {
                normalized = Path.GetFullPath(entry.ImagePath);
            }
            catch
            {
                continue;
            }

            valid.Add(entry with
            {
                ImagePath = normalized,
                TargetDisplay = NormalizeTarget(entry.TargetDisplay)
            });
        }

        return valid
            .OrderByDescending(x => x.TimestampUtc)
            .Take(_maxEntries)
            .ToArray();
    }

    private async Task SaveUnsafeAsync(
        IReadOnlyList<MountHistoryEntry> entries,
        CancellationToken cancellationToken)
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

    private static string NormalizePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path cannot be empty.", nameof(imagePath));
        return Path.GetFullPath(imagePath);
    }

    private static string? NormalizeTarget(string? targetDisplay)
        => string.IsNullOrWhiteSpace(targetDisplay) ? null : targetDisplay.Trim();

    public void Dispose() => _gate.Dispose();
}
