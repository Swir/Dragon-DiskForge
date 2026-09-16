namespace DragonDiskForge.Core.Models;

public enum ImageHealthSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ImageIdentityEvidence(
    string Kind,
    string Value,
    string Source,
    int? PartitionIndex = null);

public sealed record ImageHealthFinding(
    string Code,
    ImageHealthSeverity Severity,
    string Message,
    string Source,
    int? PartitionIndex = null);

public sealed record ImageIntelligenceInfo(
    string ProviderId,
    string ProviderDisplayName,
    long PhysicalImageSizeBytes,
    PartitionIntelligenceInfo? PartitionLayout,
    FileSystemRecognitionInfo? FileSystems,
    BootInstallerIntelligenceInfo? BootInstaller,
    IReadOnlyList<ImageIdentityEvidence> Identity,
    IReadOnlyList<string> ArchitectureHints,
    IReadOnlyList<ImageHealthFinding> HealthFindings)
{
    public bool HasErrors => HealthFindings.Any(x => x.Severity == ImageHealthSeverity.Error);
    public bool HasWarnings => HealthFindings.Any(x => x.Severity == ImageHealthSeverity.Warning);
    public bool HasHealthFindings => HealthFindings.Count > 0;
}
