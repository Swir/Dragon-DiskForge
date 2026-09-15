using System.Text;
using DiscUtils.Iso9660;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class Iso9660ImageProvider : IDirectImageBrowseProvider
{
    private static readonly string[] ProviderExtensions = [".iso"];

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".json", ".xml", ".csv", ".ini", ".cfg", ".conf",
        ".yaml", ".yml", ".toml", ".ps1", ".bat", ".cmd", ".sh", ".py", ".cs", ".cpp",
        ".c", ".h", ".hpp", ".java", ".js", ".ts", ".html", ".htm", ".css", ".sql"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico"
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma",
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v"
    };

    public string Id => "iso9660-direct";
    public string DisplayName => "ISO9660 / Joliet direct provider";
    public IReadOnlyCollection<string> Extensions => ProviderExtensions;

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
        => await CanBrowseAsync(path, cancellationToken);

    public async ValueTask<bool> CanBrowseAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath)
            || !Path.GetExtension(imagePath).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            return false;

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            return false;

        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = OpenImageStream(fullPath);
        return await Task.Run(() => CDReader.Detect(stream), cancellationToken);
    }

    public async ValueTask<DiskImageInfo> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ValidateImagePath(path);
        if (!await CanBrowseAsync(fullPath, cancellationToken))
            throw new InvalidDataException("The image is not a supported ISO9660/Joliet file system.");

        var info = new FileInfo(fullPath);
        return new DiskImageInfo(
            fullPath,
            info.Name,
            "ISO",
            info.Length,
            "ISO9660 provider signature",
            CanExplore: true,
            CanMount: true,
            CanConvert: false,
            CanVerify: true);
    }

    public Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string imagePath,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var image = ValidateImagePath(imagePath);
        var directory = NormalizeVirtualPath(directoryPath);

        return Task.Run<IReadOnlyList<ExplorerEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = OpenImageStream(image);
            using var reader = OpenReader(stream);
            EnsureDirectory(reader, directory);

            var entries = new List<ExplorerEntry>();
            foreach (var path in reader.GetDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(CreateDirectoryEntry(reader, path));
            }

            foreach (var path in reader.GetFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(CreateFileEntry(reader, path));
            }

            return entries
                .OrderBy(entry => entry.Kind)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ExplorerEntry>> SearchAsync(
        string imagePath,
        string startPath,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query cannot be empty.", nameof(query));
        if (maxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResults));

        var image = ValidateImagePath(imagePath);
        var start = NormalizeVirtualPath(startPath);
        var needle = query.Trim();

        return Task.Run<IReadOnlyList<ExplorerEntry>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = OpenImageStream(image);
            using var reader = OpenReader(stream);
            EnsureDirectory(reader, start);

            var results = new List<ExplorerEntry>(Math.Min(maxResults, 64));
            var pending = new Queue<string>();
            pending.Enqueue(start);

            while (pending.Count > 0 && results.Count < maxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Dequeue();

                foreach (var childDirectory in reader.GetDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pending.Enqueue(NormalizeVirtualPath(childDirectory));
                    var entry = CreateDirectoryEntry(reader, childDirectory);
                    if (entry.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(entry);
                        if (results.Count >= maxResults)
                            break;
                    }
                }

                if (results.Count >= maxResults)
                    break;

                foreach (var file in reader.GetFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = CreateFileEntry(reader, file);
                    if (!entry.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(entry);
                    if (results.Count >= maxResults)
                        break;
                }
            }

            return results
                .OrderBy(entry => entry.Kind)
                .ThenBy(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public async Task CopyOutAsync(
        string imagePath,
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var image = ValidateImagePath(imagePath);
        var source = NormalizeVirtualPath(sourcePath);
        if (string.IsNullOrEmpty(source))
            throw new InvalidOperationException("Copying the provider root as one item is not supported.");

        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));

        var destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(destinationRoot))
            throw new DirectoryNotFoundException($"Destination directory was not found: {destinationRoot}");

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0d);

        using var stream = OpenImageStream(image);
        using var reader = OpenReader(stream);
        EnsureEntry(reader, source);

        var leafName = GetLeafName(source);
        ValidateDestinationName(leafName);
        var target = Path.Combine(destinationRoot, leafName);
        EnsureDestinationAvailable(target);

        if (reader.DirectoryExists(source))
            await CopyDirectoryAsync(reader, source, target, progress, cancellationToken);
        else
            await CopyFileAsync(reader, source, target, cancellationToken);

        progress?.Report(1d);
    }

    public async Task<PreviewInfo> GetPreviewAsync(
        string imagePath,
        string sourcePath,
        int maxTextCharacters = 200_000,
        CancellationToken cancellationToken = default)
    {
        if (maxTextCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTextCharacters));

        var image = ValidateImagePath(imagePath);
        var source = NormalizeVirtualPath(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = OpenImageStream(image);
        using var reader = OpenReader(stream);
        if (!reader.FileExists(source))
            throw new FileNotFoundException("The provider entry is not a file.", source);

        var info = reader.GetFileInfo(source);
        var extension = Path.GetExtension(info.Name);
        var modified = ToOffset(info.LastWriteTimeUtc);

        if (TextExtensions.Contains(extension))
        {
            await using var file = reader.OpenFile(source, FileMode.Open, FileAccess.Read);
            var (text, truncated) = await ReadBoundedTextAsync(file, maxTextCharacters, cancellationToken);
            return new PreviewInfo(
                source,
                info.Name,
                PreviewKind.Text,
                info.Length,
                modified,
                $"Direct ISO text preview • {extension.TrimStart('.').ToUpperInvariant()} • no mount • read-only",
                text,
                truncated);
        }

        var kind = ImageExtensions.Contains(extension)
            ? PreviewKind.Image
            : extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? PreviewKind.PdfMetadata
                : MediaExtensions.Contains(extension)
                    ? PreviewKind.MediaMetadata
                    : PreviewKind.BinaryMetadata;

        var description = kind switch
        {
            PreviewKind.Image => $"Image metadata • {extension.TrimStart('.').ToUpperInvariant()} • direct provider • copy out to render externally",
            PreviewKind.PdfMetadata => "PDF metadata • direct provider • no rendering/execution",
            PreviewKind.MediaMetadata => $"Media metadata • {extension.TrimStart('.').ToUpperInvariant()} • no auto-play",
            _ => string.IsNullOrWhiteSpace(extension)
                ? "Binary/file metadata • direct provider"
                : $"Binary/file metadata • {extension.TrimStart('.').ToUpperInvariant()} • direct provider"
        };

        return new PreviewInfo(source, info.Name, kind, info.Length, modified, description);
    }

    private static CDReader OpenReader(Stream stream)
    {
        if (!CDReader.Detect(stream))
            throw new InvalidDataException("The image does not contain a readable ISO9660 file system.");
        stream.Position = 0;
        return new CDReader(stream, joliet: true, hideVersions: true);
    }

    private static FileStream OpenImageStream(string imagePath)
        => new(
            imagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.SequentialScan);

    private static ExplorerEntry CreateDirectoryEntry(CDReader reader, string path)
    {
        var normalized = NormalizeVirtualPath(path);
        var info = reader.GetDirectoryInfo(normalized);
        return new ExplorerEntry(
            info.Name,
            normalized,
            ExplorerEntryKind.Directory,
            null,
            ToOffset(info.LastWriteTimeUtc));
    }

    private static ExplorerEntry CreateFileEntry(CDReader reader, string path)
    {
        var normalized = NormalizeVirtualPath(path);
        var info = reader.GetFileInfo(normalized);
        return new ExplorerEntry(
            info.Name,
            normalized,
            ExplorerEntryKind.File,
            info.Length,
            ToOffset(info.LastWriteTimeUtc));
    }

    private static async Task CopyDirectoryAsync(
        CDReader reader,
        string source,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var directories = new List<(string Source, string Destination)> { (source, destination) };
        var files = new List<(string Source, string Destination)>();
        var pending = new Queue<(string Source, string Destination)>();
        pending.Enqueue((source, destination));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Dequeue();

            foreach (var childDirectory in reader.GetDirectories(current.Source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var childSource = NormalizeVirtualPath(childDirectory);
                var name = GetLeafName(childSource);
                ValidateDestinationName(name);
                var childDestination = Path.Combine(current.Destination, name);
                directories.Add((childSource, childDestination));
                pending.Enqueue((childSource, childDestination));
            }

            foreach (var childFile in reader.GetFiles(current.Source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var childSource = NormalizeVirtualPath(childFile);
                var name = GetLeafName(childSource);
                ValidateDestinationName(name);
                files.Add((childSource, Path.Combine(current.Destination, name)));
            }
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(directory.Destination) || Directory.Exists(directory.Destination))
                throw new IOException($"Destination already exists: {directory.Destination}");
            Directory.CreateDirectory(directory.Destination);
        }

        if (files.Count == 0)
        {
            progress?.Report(1d);
            return;
        }

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            EnsureDestinationAvailable(file.Destination);
            await CopyFileAsync(reader, file.Source, file.Destination, cancellationToken);
            progress?.Report((index + 1d) / files.Count);
        }
    }

    private static async Task CopyFileAsync(
        CDReader reader,
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = reader.OpenFile(source, FileMode.Open, FileAccess.Read);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 128 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedTextAsync(
        Stream stream,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 32 * 1024,
            leaveOpen: true);

        var builder = new StringBuilder(Math.Min(maxCharacters, 32_768));
        var buffer = new char[Math.Min(16_384, maxCharacters)];

        while (builder.Length < maxCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wanted = Math.Min(buffer.Length, maxCharacters - builder.Length);
            var read = await reader.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);
            if (read <= 0)
                return (builder.ToString(), false);
            builder.Append(buffer, 0, read);
        }

        var probe = new char[1];
        var extra = await reader.ReadAsync(probe.AsMemory(), cancellationToken);
        return (builder.ToString(), extra > 0);
    }

    private static string ValidateImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path cannot be empty.", nameof(imagePath));
        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Disk image was not found.", fullPath);
        return fullPath;
    }

    private static string NormalizeVirtualPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "\\" || path == "/")
            return string.Empty;

        var parts = path.Replace('/', '\\')
            .Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Any(part => part is "." or ".." || part.Contains(':') || part.Contains('\0')))
            throw new InvalidOperationException("The provider path contains an unsafe segment.");

        return string.Join('\\', parts);
    }

    private static string GetLeafName(string path)
    {
        var normalized = NormalizeVirtualPath(path);
        var separator = normalized.LastIndexOf('\\');
        return separator < 0 ? normalized : normalized[(separator + 1)..];
    }

    private static void EnsureDirectory(CDReader reader, string path)
    {
        if (!reader.DirectoryExists(path))
            throw new DirectoryNotFoundException(string.IsNullOrEmpty(path) ? "ISO root was not found." : $"ISO directory was not found: {path}");
    }

    private static void EnsureEntry(CDReader reader, string path)
    {
        if (!reader.FileExists(path) && !reader.DirectoryExists(path))
            throw new FileNotFoundException("The ISO provider entry was not found.", path);
    }

    private static void EnsureDestinationAvailable(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException($"Destination already exists: {path}");
    }

    private static void ValidateDestinationName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"The ISO entry name cannot be copied safely to Windows: {name}");
    }

    private static DateTimeOffset ToOffset(DateTime utc)
        => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}
