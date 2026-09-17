namespace DragonDiskForge.Core.Models;

public sealed record DragonApplicationSettings(bool RestoreLastImage = true);

public sealed record DragonSessionState(
    string? LastImagePath,
    DateTimeOffset? LastSavedUtc);

public sealed record DragonPortableState(
    int SchemaVersion,
    DragonApplicationSettings Settings,
    DragonSessionState Session)
{
    public const int CurrentSchemaVersion = 1;

    public static DragonPortableState Default { get; } = new(
        CurrentSchemaVersion,
        new DragonApplicationSettings(),
        new DragonSessionState(null, null));
}

public sealed record DragonDiagnosticSnapshot(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    string OperatingSystem,
    string Runtime,
    string ProcessArchitecture,
    string ProductVersion,
    bool RestoreLastImage,
    bool HasSavedSession,
    string? SavedImageExtension,
    int ProviderCount,
    string[] ProviderIds);
