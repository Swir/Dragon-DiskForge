namespace DragonDiskForge.Core.Models;

public enum PreviewKind
{
    None,
    Text,
    Image,
    PdfMetadata,
    MediaMetadata,
    BinaryMetadata
}

public sealed record PreviewInfo(
    string Path,
    string Name,
    PreviewKind Kind,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    string Description,
    string? Text = null,
    bool IsTruncated = false)
{
    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024d:0.#} KB",
        < 1024L * 1024L * 1024L => $"{SizeBytes / (1024d * 1024d):0.#} MB",
        _ => $"{SizeBytes / (1024d * 1024d * 1024d):0.##} GB"
    };
}
