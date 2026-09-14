namespace DragonDiskForge.Core.Models;

public sealed record SupportedFormat(
    string Name,
    IReadOnlyList<string> Extensions,
    bool NativeWindowsMount,
    bool PlannedExplore = true,
    bool PlannedConvert = true);
