namespace DragonDiskForge.Windows.Services;

/// <summary>
/// Read-only Windows helper for proving which physical disks back a local filesystem path.
/// It is used by destructive-operation evidence tooling to ensure evidence is not written
/// onto the same physical device that is about to be modified.
/// </summary>
public static class WindowsPhysicalMediaPathEvidence
{
    public static IReadOnlyList<int> GetLocalBackingDiskNumbers(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Physical-disk path evidence is implemented only for Windows.");

        if (path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Physical-disk path evidence requires a normal local filesystem path; UNC and device namespace paths are refused.");
        }

        var fullPath = Path.GetFullPath(path);
        var probePath = File.Exists(fullPath) || Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(probePath)
            || (!File.Exists(probePath) && !Directory.Exists(probePath)))
        {
            throw new InvalidDataException(
                "Physical-disk path evidence requires an existing local file or parent directory.");
        }

        var backingDisks = WindowsPhysicalMediaInterop.GetBackingDiskNumbersForPath(probePath);
        if (backingDisks.Count == 0)
            throw new InvalidDataException("The local path backing physical disk could not be proven.");

        return backingDisks;
    }
}
