namespace DragonDiskForge.Core.Providers;

public sealed record ProviderProbeDiagnostic(
    string ProviderId,
    string DisplayName,
    bool ExtensionMatched,
    bool Supported,
    string? ErrorMessage);

public sealed record ProviderResolution(
    IDiskImageProvider? Provider,
    ProviderDescriptor? Descriptor,
    IReadOnlyList<ProviderProbeDiagnostic> Diagnostics)
{
    public bool IsResolved => Provider is not null;
}
