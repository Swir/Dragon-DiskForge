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
}
