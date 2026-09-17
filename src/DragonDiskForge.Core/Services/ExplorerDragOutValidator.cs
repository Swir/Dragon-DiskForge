namespace DragonDiskForge.Core.Services;

public sealed class ExplorerDragOutValidator
{
    private readonly ExplorerPathSafetyValidator _pathSafetyValidator = new();

    public string Validate(
        string rootPath,
        string candidatePath,
        bool isDirectory,
        bool declaredReparsePoint = false)
        => _pathSafetyValidator.Validate(
            rootPath,
            candidatePath,
            isDirectory,
            declaredReparsePoint);

    /// <summary>
    /// Revalidates the source after Windows has resolved it to a storage item and
    /// proves that the resolved item still represents the same file-system path
    /// and shape that Dragon Explorer originally selected.
    /// </summary>
    public string ValidateResolvedStorageItem(
        string rootPath,
        string expectedSourcePath,
        string resolvedStoragePath,
        bool expectedIsDirectory,
        bool resolvedIsDirectory,
        bool declaredReparsePoint = false)
    {
        if (expectedIsDirectory != resolvedIsDirectory)
            throw new InvalidOperationException(
                "Resolved drag-out storage item changed between selection and transfer preparation.");

        var expected = _pathSafetyValidator.Validate(
            rootPath,
            expectedSourcePath,
            expectedIsDirectory,
            declaredReparsePoint);
        var resolved = _pathSafetyValidator.Validate(
            rootPath,
            resolvedStoragePath,
            resolvedIsDirectory,
            declaredReparsePoint: false);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(expected, resolved, comparison))
            throw new InvalidOperationException(
                "Resolved drag-out storage item no longer matches the selected Explorer source.");

        return resolved;
    }
}
