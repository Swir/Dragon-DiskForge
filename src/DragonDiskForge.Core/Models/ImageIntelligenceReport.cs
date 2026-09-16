using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Models;

public sealed record PartitionIntelligenceSummary(
    PartitionTableScheme Scheme,
    int SectorSize,
    int PartitionCount,
    int BootableFlaggedPartitionCount,
    long AllocatedBytes,
    long LargestPartitionBytes)
{
    public string SchemeDisplay => Scheme == PartitionTableScheme.Gpt ? "GPT" : "MBR";
    public bool HasBootableFlaggedPartition => BootableFlaggedPartitionCount > 0;
}

public sealed record ImageIntelligenceReport(
    string ImagePath,
    string FileName,
    string Format,
    long SizeBytes,
    string ProviderId,
    string ProviderDisplayName,
    ProviderCapabilities Capabilities,
    PartitionIntelligenceSummary? PartitionSummary,
    int ProviderProbeCount,
    int FailedProviderProbeCount)
{
    public bool HasPartitionTable => PartitionSummary is not null;
}
