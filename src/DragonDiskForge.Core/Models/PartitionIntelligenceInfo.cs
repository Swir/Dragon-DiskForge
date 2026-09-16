namespace DragonDiskForge.Core.Models;

public enum PartitionFindingSeverity
{
    Info,
    Warning,
    Error
}

public sealed record PartitionLayoutFinding(
    string Code,
    PartitionFindingSeverity Severity,
    string Message,
    int? PartitionIndex = null);

public sealed record PartitionIntelligenceInfo(
    string ProviderId,
    string ProviderDisplayName,
    PartitionTableScheme Scheme,
    int SectorSize,
    long ImageSizeBytes,
    IReadOnlyList<PartitionInfo> Partitions,
    int BootablePartitionCount,
    IReadOnlyList<PartitionLayoutFinding> Findings)
{
    public string SchemeDisplay => Scheme == PartitionTableScheme.Gpt ? "GPT" : "MBR";
    public bool HasErrors => Findings.Any(x => x.Severity == PartitionFindingSeverity.Error);
    public bool HasWarnings => Findings.Any(x => x.Severity == PartitionFindingSeverity.Warning);
}
