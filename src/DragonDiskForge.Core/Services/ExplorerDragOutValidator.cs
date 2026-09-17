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

        EnsureNoReparseTraversal(root, candidate);
        return candidate;
    }

    private static void EnsureNoReparseTraversal(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // The mounted root is the trusted anchor. Every component below that root,
        // including the selected item itself, must remain a normal filesystem entry.
        // Checking only the leaf is insufficient because an intermediate junction can
        // make a lexically in-root path resolve to data outside the mounted volume.
        var current = candidate;
        while (!string.Equals(current, root, comparison))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    "Drag-out is blocked because the source path traverses a reparse point or junction.");

            var parent = Directory.GetParent(current);
            if (parent is null)
                throw new InvalidOperationException(
                    "Drag-out source path could not be proven to remain inside the mounted root.");

            current = Normalize(parent.FullName);
            if (!IsInsideRoot(root, current))
                throw new InvalidOperationException(
                    "Drag-out source path could not be proven to remain inside the mounted root.");
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
