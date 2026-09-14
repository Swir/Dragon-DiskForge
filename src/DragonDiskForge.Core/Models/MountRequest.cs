namespace DragonDiskForge.Core.Models;

public sealed record MountRequest(
    string ImagePath,
    bool ReadOnly = true,
    bool NoDriveLetter = false);
