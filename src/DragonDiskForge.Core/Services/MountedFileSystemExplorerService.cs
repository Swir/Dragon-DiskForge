using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class MountedFileSystemExplorerService : IExplorerService
{
    private const string CopyStagingSuffix = ".dragon-copy-tmp";

    private static readonly EnumerationOptions ShallowOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false
    };

    private static readonly ExplorerPathSafetyValidator PathSafety = new();
    private static readonly SafeOutputService SafeOutput = new();

    public Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string rootPath,
        string directoryPath,
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ExplorerEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = ValidateRoot(rootPath);
            var directory = EnsureInsideRoot(root, directoryPath);
            directory = PathSafety.Validate(root, directory, isDirectory: true);

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
            start = PathSafety.Validate(root, start, isDirectory: true);

            var results = new List<ExplorerEntry>();
            var pending = new Stack<string>();
            pending.Push(start);

            while (pending.Count > 0 && results.Count < maxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = PathSafety.Validate(root, pending.Pop(), isDirectory: true);

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
        var sourceCandidate = EnsureInsideRoot(root, sourcePath);
        var destinationRoot = Path.GetFullPath(destinationDirectory);

        if (IsInsideRoot(root, destinationRoot))
            throw new InvalidOperationException("Copy-out destination must be outside the mounted image.");
        if (!Directory.Exists(destinationRoot))
            Directory.CreateDirectory(destinationRoot);

        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(sourceCandidate))
        {
            var source = PathSafety.Validate(root, sourceCandidate, isDirectory: false);
            var destination = Path.Combine(destinationRoot, Path.GetFileName(source));
            EnsureDestinationFree(destination);
            var length = new FileInfo(source).Length;
            await CopyFileAsync(root, source, destination, 0L, Math.Max(1L, length), progress, cancellationToken);
            progress?.Report(1d);
            return;
        }

        if (!Directory.Exists(sourceCandidate))
            throw new FileNotFoundException("Explorer source was not found.", sourceCandidate);

        var sourceDirectory = PathSafety.Validate(root, sourceCandidate, isDirectory: true);
        var destinationBase = Path.Combine(
            destinationRoot,
            Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        EnsureDestinationFree(destinationBase);

        var plan = CollectCopyPlan(root, sourceDirectory, cancellationToken);
        var totalBytes = Math.Max(1L, plan.Files.Sum(x => x.Length));
        var stagingBase = Path.Combine(
            destinationRoot,
            $".{Path.GetFileName(destinationBase)}.{Guid.NewGuid():N}{CopyStagingSuffix}");
        var committed = false;

        try
        {
            Directory.CreateDirectory(stagingBase);

            foreach (var directory in plan.Directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(CombineInsideDestinationRoot(stagingBase, directory));
            }

            long copiedBefore = 0;
            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var safeFile = PathSafety.Validate(root, file.FullName, isDirectory: false);
                var relative = Path.GetRelativePath(sourceDirectory, safeFile);
                var destination = CombineInsideDestinationRoot(stagingBase, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(root, safeFile, destination, copiedBefore, totalBytes, progress, cancellationToken);
                copiedBefore += file.Length;
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureDestinationFree(destinationBase);
            Directory.Move(stagingBase, destinationBase);
            committed = true;
            progress?.Report(1d);
        }
        finally
        {
            if (!committed)
                TryDeleteDirectory(stagingBase);
        }
    }

    private static CopyPlan CollectCopyPlan(
        string root,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>();
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(sourceDirectory);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = PathSafety.Validate(root, pending.Pop(), isDirectory: true);

            foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", ShallowOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    var safeDirectory = PathSafety.Validate(root, path, isDirectory: true);
                    directories.Add(Path.GetRelativePath(sourceDirectory, safeDirectory));
                    pending.Push(safeDirectory);
                }
                else
                {
                    var safeFile = PathSafety.Validate(root, path, isDirectory: false);
                    files.Add(new FileInfo(safeFile));
                }
            }
        }

        return new CopyPlan(files, directories);
    }

    private static async Task CopyFileAsync(
        string root,
        string source,
        string destination,
        long copiedBefore,
        long totalBytes,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        await SafeOutput.WriteAsync(
            destination,
            async (output, token) =>
            {
                var safeSource = PathSafety.Validate(root, source, isDirectory: false);
                await using var input = new FileStream(
                    safeSource,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 1024 * 128,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                var buffer = new byte[1024 * 128];
                long copiedCurrent = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                    if (read <= 0)
                        break;

                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    copiedCurrent += read;
                    progress?.Report(Math.Clamp((copiedBefore + copiedCurrent) / (double)totalBytes, 0d, 1d));
                }
            },
            OutputOverwritePolicy.FailIfExists,
            cancellationToken);
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

    private static string CombineInsideDestinationRoot(string destinationRoot, string relativePath)
    {
        var candidate = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
        if (!IsInsideRoot(destinationRoot, candidate))
            throw new InvalidOperationException("Copy-out destination path escaped the staging root.");
        return candidate;
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

    private static void EnsureDestinationFree(string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException($"Copy-out destination already contains an item named '{Path.GetFileName(destination)}'. Dragon DiskForge will not overwrite it.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Preserve the original failure/cancellation. The staging path is uniquely Dragon-owned.
        }
    }

    private sealed record CopyPlan(IReadOnlyList<FileInfo> Files, IReadOnlyList<string> Directories);
}
