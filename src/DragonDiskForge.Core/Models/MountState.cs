namespace DragonDiskForge.Core.Models;

public sealed record MountState(
    string ImagePath,
    bool IsMounted,
    string? DevicePath,
    IReadOnlyList<string> DriveLetters,
    bool RequiresElevation)
{
    public string TargetDisplay => DriveLetters.Count > 0
        ? string.Join(", ", DriveLetters)
        : !string.IsNullOrWhiteSpace(DevicePath)
            ? DevicePath
            : IsMounted ? "Mounted" : "Not mounted";

    public static MountState Detached(string imagePath, bool requiresElevation)
        => new(imagePath, false, null, Array.Empty<string>(), requiresElevation);
}
