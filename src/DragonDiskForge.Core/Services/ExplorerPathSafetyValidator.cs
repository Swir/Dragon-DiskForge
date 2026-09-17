namespace DragonDiskForge.Core.Services;

/// <summary>
/// Validates a mounted-filesystem path against Dragon Explorer's read-only safety boundary.
/// The mounted root itself is treated as the trusted anchor because Windows may represent
/// that root through a mount mechanism; every component below it must remain non-reparse.
/// </summary>
public sealed class ExplorerPathSafetyValidator
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
            throw new ArgumentException("Explorer source path cannot be empty.", nameof(candidatePath));
        if (declaredReparsePoint)
            throw new InvalidOperationException("Explorer source is blocked because it is a reparse point or junction.");

        var root = Normalize(rootPath);
        var candidate = Normalize(candidatePath);
        if (!IsInsideRoot(root, candidate))
            throw new InvalidOperationException("Explorer source is outside the mounted root.");

        if (isDirectory)
        {
            if (!Directory.Exists(candidate))
                throw new DirectoryNotFoundException($"The Explorer directory is no longer available: {candidate}");
        }
        else if (!File.Exists(candidate))
        {
            throw new FileNotFoundException("The Explorer source is no longer available.", candidate);
        }

        EnsureNoReparseTraversal(root, candidate);
        return candidate;
    }

    private static void EnsureNoReparseTraversal(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var current = candidate;
        while (!string.Equals(current, root, comparison))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    "Explorer source path traverses a reparse point or junction below the mounted root.");

            var parent = Directory.GetParent(current);
            if (parent is null)
                throw new InvalidOperationException(
                    "Explorer source path could not be proven to remain inside the mounted root.");

            current = Normalize(parent.FullName);
            if (!IsInsideRoot(root, current))
                throw new InvalidOperationException(
                    "Explorer source path could not be proven to remain inside the mounted root.");
        }
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
