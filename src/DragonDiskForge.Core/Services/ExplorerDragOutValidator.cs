namespace DragonDiskForge.Core.Services;

public sealed class ExplorerDragOutValidator
{
    public string Validate(
        string rootPath,
        string candidatePath,
        bool isDirectory,
        bool declaredReparsePoint = false)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Mounted root path cannot be empty.", nameof(rootPath));
        if (string.IsNullOrWhiteSpace(candidatePath))
            throw new ArgumentException("Drag-out source path cannot be empty.", nameof(candidatePath));
        if (declaredReparsePoint)
            throw new InvalidOperationException("Drag-out is blocked for reparse points and junctions.");

        var root = Normalize(rootPath);
        var candidate = Normalize(candidatePath);
        if (!IsInsideRoot(root, candidate))
            throw new InvalidOperationException("Drag-out source is outside the mounted root.");

        var exists = isDirectory ? Directory.Exists(candidate) : File.Exists(candidate);
        if (!exists)
            throw new FileNotFoundException("The drag-out source is no longer available.", candidate);

        var attributes = File.GetAttributes(candidate);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Drag-out is blocked because the source is a reparse point or junction.");

        return candidate;
    }

    private static bool IsInsideRoot(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(root, candidate, comparison)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static string Normalize(string path)
        => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
