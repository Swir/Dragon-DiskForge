using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class FilePreviewService : IFilePreviewService
{
    public const long DefaultMaxRenderedImageBytes = 64L * 1024L * 1024L;

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

    private readonly long _maxRenderedImageBytes;

    public FilePreviewService(long maxRenderedImageBytes = DefaultMaxRenderedImageBytes)
    {
        if (maxRenderedImageBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRenderedImageBytes));

        _maxRenderedImageBytes = maxRenderedImageBytes;
    }

    public async Task<PreviewInfo> GetPreviewAsync(
        string filePath,
        int maxTextCharacters = 200_000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Preview path cannot be empty.", nameof(filePath));
        if (maxTextCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTextCharacters));

        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(filePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Preview file was not found.", path);

        var info = new FileInfo(path);
        var extension = info.Extension;
        var modified = new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc));

        if (TextExtensions.Contains(extension))
        {
            var (text, truncated) = await ReadBoundedTextAsync(path, maxTextCharacters, cancellationToken);
            return new PreviewInfo(
                path,
                info.Name,
                PreviewKind.Text,
                info.Length,
                modified,
                $"Text preview • {extension.TrimStart('.').ToUpperInvariant()} • read-only",
                text,
                truncated);
        }

        if (ImageExtensions.Contains(extension))
        {
            if (info.Length > _maxRenderedImageBytes)
            {
                return new PreviewInfo(
                    path,
                    info.Name,
                    PreviewKind.BinaryMetadata,
                    info.Length,
                    modified,
                    $"Image metadata preview • {extension.TrimStart('.').ToUpperInvariant()} • rendering disabled above the {FormatByteLimit(_maxRenderedImageBytes)} safety cap");
            }

            return new PreviewInfo(
                path,
                info.Name,
                PreviewKind.Image,
                info.Length,
                modified,
                $"Image preview • {extension.TrimStart('.').ToUpperInvariant()} • read-only");
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new PreviewInfo(
                path,
                info.Name,
                PreviewKind.PdfMetadata,
                info.Length,
                modified,
                "PDF metadata preview • opening/rendering is intentionally separate from the safe metadata path");
        }

        if (MediaExtensions.Contains(extension))
        {
            return new PreviewInfo(
                path,
                info.Name,
                PreviewKind.MediaMetadata,
                info.Length,
                modified,
                $"Media metadata preview • {extension.TrimStart('.').ToUpperInvariant()} • no auto-play");
        }

        return new PreviewInfo(
            path,
            info.Name,
            PreviewKind.BinaryMetadata,
            info.Length,
            modified,
            string.IsNullOrWhiteSpace(extension)
                ? "Binary/file metadata preview"
                : $"Binary/file metadata preview • {extension.TrimStart('.').ToUpperInvariant()}");
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedTextAsync(
        string path,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: false);

        var builder = new StringBuilder(Math.Min(maxCharacters, 32_768));
        var buffer = new char[Math.Min(16_384, maxCharacters)];

        while (builder.Length < maxCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = maxCharacters - builder.Length;
            var wanted = Math.Min(buffer.Length, remaining);
            var read = await reader.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);
            if (read <= 0)
                return (builder.ToString(), false);

            builder.Append(buffer, 0, read);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var probe = new char[1];
        var extra = await reader.ReadAsync(probe.AsMemory(), cancellationToken);
        return (builder.ToString(), extra > 0);
    }

    private static string FormatByteLimit(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024L * 1024L)
            return $"{bytes / 1024d:0.#} KB";
        if (bytes < 1024L * 1024L * 1024L)
            return $"{bytes / (1024d * 1024d):0.#} MB";
        return $"{bytes / (1024d * 1024d * 1024d):0.##} GB";
    }
}
