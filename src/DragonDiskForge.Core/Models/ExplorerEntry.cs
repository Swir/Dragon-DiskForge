namespace DragonDiskForge.Core.Models;

public enum ExplorerEntryKind
{
    Directory,
    File
}

public sealed record ExplorerEntry(
    string Name,
    string FullPath,
    ExplorerEntryKind Kind,
    long? SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    bool IsReparsePoint = false)
{
    public bool IsDirectory => Kind == ExplorerEntryKind.Directory;

    public string SizeDisplay
        => SizeBytes is null ? string.Empty : FormatBytes(SizeBytes.Value);

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)value;
        var unit = 0;
        while (size >= 1024d && unit < units.Length - 1)
        {
            size /= 1024d;
            unit++;
        }

        return unit == 0 ? $"{value} {units[unit]}" : $"{size:0.##} {units[unit]}";
    }
}
