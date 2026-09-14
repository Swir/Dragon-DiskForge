using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class MountedFileSystemExplorerService : IExplorerService
{
    private static readonly EnumerationOptions ShallowOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false
    };

    public Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string rootPath,
        string directoryPath,
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ExplorerEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = ValidateRoot(rootPath);
            var directory = EnsureInsideRoot(root, directoryPath);
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Explorer directory was not found: {directory}");

            var items = new List<ExplorerEntry>();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", ShallowOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = TryCreateEntry(path);
                if (entry is not null)
                    items.Add(entry);
            }

            return items
                .OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<ExplorerEntry>> SearchAsync(
        string rootPath,
        string startPath,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ExplorerEntry>>(() =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<ExplorerEntry>();
            if (maxResults <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxResults));

            var root = ValidateRoot(rootPath);
            var start = EnsureInsideRoot(root, startPath);
            if (!Directory.Exists(start))
                throw new DirectoryNotFoundException($"Explorer search root was not found: {start}");

            var results = new List<ExplorerEntry>();
            var pending = new Stack<string>();
            pending.Push(start);

            while (pending.Count > 0 && results.Count < maxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();

                IEnumerable<string> entries;
                try
                {
                    entries = Directory.EnumerateFileSystemEntries(directory, "*", ShallowOptions);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var path in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = TryCreateEntry(path);
                    if (entry is null)
                        continue;

                    if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(entry);
                        if (results.Count >= maxResults)
                            break;
                    }

                    if (entry.IsDirectory && !entry.IsReparsePoint)
                        pending.Push(entry.FullPath);
                }
            }

            return results
                .OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);

    public async Task CopyOutAsync(
        string rootPath,
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = ValidateRoot(rootPath);
        var source = EnsureInsideRoot(root, sourcePath);
        var destinationRoot = Path.GetFullPath(destinationDirectory);

        if (IsInsideRoot(root, destinationRoot))
            throw new InvalidOperationException("Copy-out destination must be outside the mounted image.");
        if (!Directory.Exists(destinationRoot))
            Directory.CreateDirectory(destinationRoot);

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(source))
        {
            RejectReparsePoint(source);
            var destination = Path.Combine(destinationRoot, Path.GetFileName(source));
            var length = new FileInfo(source).Length;
            await CopyFileAsync(source, destination, 0L, Math.Max(1L, length), progress, cancellationToken);
            progress?.Report(1d);
            return;
        }

        if (!Directory.Exists(source))
            throw new FileNotFoundException("Explorer source was not found.", source);

        RejectReparsePoint(source);
        var files = CollectCopyFiles(source, cancellationToken);
        var totalBytes = Math.Max(1L, files.Sum(x => x.Length));
        var destinationBase = Path.Combine(destinationRoot, Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        Directory.CreateDirectory(destinationBase);

        long copiedBefore = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file.FullName);
            var destination = Path.Combine(destinationBase, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(file.FullName, destination, copiedBefore, totalBytes, progress, cancellationToken);
            copiedBefore += file.Length;
        }

        progress?.Report(1d);
    }

    private static List<FileInfo> CollectCopyFiles(string sourceDirectory, CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>();
        var pending = new Stack<string>();
        pending.Push(sourceDirectory);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", ShallowOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);
                else
                    files.Add(new FileInfo(path));
            }
        }

        return files;
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        long copiedBefore,
        long totalBytes,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[1024 * 128];
        long copiedCurrent = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copiedCurrent += read;
            progress?.Report(Math.Clamp((copiedBefore + copiedCurrent) / (double)totalBytes, 0d, 1d));
        }

        await output.FlushAsync(cancellationToken);
    }

    private static ExplorerEntry? TryCreateEntry(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
            var lastWrite = isDirectory
                ? new DirectoryInfo(path).LastWriteTimeUtc
                : new FileInfo(path).LastWriteTimeUtc;
            long? size = isDirectory ? null : new FileInfo(path).Length;

            return new ExplorerEntry(
                Path.GetFileName(path),
                Path.GetFullPath(path),
                isDirectory ? ExplorerEntryKind.Directory : ExplorerEntryKind.File,
                size,
                new DateTimeOffset(DateTime.SpecifyKind(lastWrite, DateTimeKind.Utc)),
                isReparsePoint);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ValidateRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Explorer root cannot be empty.", nameof(rootPath));

        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Explorer root was not found: {root}");
        return root;
    }

    private static string EnsureInsideRoot(string root, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
            throw new ArgumentException("Explorer path cannot be empty.", nameof(candidatePath));

        var fullPath = Path.GetFullPath(candidatePath);
        if (!IsInsideRoot(root, fullPath))
            throw new InvalidOperationException("Explorer path escaped the mounted-image root.");
        return fullPath;
    }

    private static bool IsInsideRoot(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedRoot, normalizedCandidate, comparison))
            return true;

        var rootedPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(rootedPrefix, comparison);
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Dragon Explorer does not follow reparse points during copy-out.");
    }
}
