using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

public sealed class PartitionIntelligenceService
{
    private readonly ProviderRegistry _registry;

    public PartitionIntelligenceService(ProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<PartitionIntelligenceInfo> AnalyzeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Disk image was not found.", fullPath);

        var resolution = await _registry.ResolveAsync(fullPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (resolution.Provider is not IPartitionTableProvider partitionProvider
            || resolution.Descriptor is null
            || !resolution.Descriptor.Capabilities.HasFlag(ProviderCapabilities.PartitionTable))
        {
            var recognized = resolution.Descriptor is null
                ? "No registered provider recognized this image."
                : $"{resolution.Descriptor.DisplayName} recognized this image but does not expose PartitionTable capability.";
            throw new NotSupportedException($"Partition intelligence is unavailable. {recognized}");
        }

        var table = await partitionProvider.ReadPartitionTableAsync(fullPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return AnalyzeLayout(
            table,
            file.Length,
            resolution.Descriptor.Id,
            resolution.Descriptor.DisplayName);
    }

    public static PartitionIntelligenceInfo AnalyzeLayout(
        PartitionTableInfo table,
        long imageSizeBytes,
        string providerId,
        string providerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerDisplayName);

        if (imageSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(imageSizeBytes));
        if (table.SectorSize <= 0)
            throw new InvalidDataException("Partition table sector size must be positive.");
        if (table.Partitions is null)
            throw new InvalidDataException("Partition table returned a null partition list.");

        var findings = new List<PartitionLayoutFinding>();
        var seenIndices = new HashSet<int>();
        var ranges = new List<PartitionRange>(table.Partitions.Count);

        if (table.Partitions.Count == 0)
        {
            findings.Add(new PartitionLayoutFinding(
                "NO_PARTITIONS",
                PartitionFindingSeverity.Info,
                "The partition table contains no partition entries."));
        }

        foreach (var partition in table.Partitions)
        {
            if (!seenIndices.Add(partition.Index))
            {
                findings.Add(new PartitionLayoutFinding(
                    "DUPLICATE_INDEX",
                    PartitionFindingSeverity.Error,
                    $"Partition index {partition.Index} appears more than once.",
                    partition.Index));
            }

            if (partition.SectorCount == 0)
            {
                findings.Add(new PartitionLayoutFinding(
                    "ZERO_LENGTH",
                    PartitionFindingSeverity.Error,
                    "Partition declares zero sectors.",
                    partition.Index));
            }

            ValidateByteGeometry(partition, table.SectorSize, imageSizeBytes, findings);

            try
            {
                var endExclusive = checked(partition.FirstLba + partition.SectorCount);
                if (partition.SectorCount > 0)
                    ranges.Add(new PartitionRange(partition, endExclusive));
            }
            catch (OverflowException)
            {
                findings.Add(new PartitionLayoutFinding(
                    "LBA_OVERFLOW",
                    PartitionFindingSeverity.Error,
                    "Partition LBA range overflows the supported address space.",
                    partition.Index));
            }
        }

        DetectOverlaps(ranges, findings);

        return new PartitionIntelligenceInfo(
            providerId,
            providerDisplayName,
            table.Scheme,
            table.SectorSize,
            imageSizeBytes,
            Array.AsReadOnly(table.Partitions.ToArray()),
            table.Partitions.Count(x => x.IsBootable),
            Array.AsReadOnly(findings.ToArray()));
    }

    private static void ValidateByteGeometry(
        PartitionInfo partition,
        int sectorSize,
        long imageSizeBytes,
        List<PartitionLayoutFinding> findings)
    {
        if (partition.OffsetBytes < 0)
        {
            findings.Add(new PartitionLayoutFinding(
                "NEGATIVE_OFFSET",
                PartitionFindingSeverity.Error,
                "Partition byte offset is negative.",
                partition.Index));
        }

        if (partition.SizeBytes < 0)
        {
            findings.Add(new PartitionLayoutFinding(
                "NEGATIVE_SIZE",
                PartitionFindingSeverity.Error,
                "Partition byte size is negative.",
                partition.Index));
        }

        try
        {
            var expectedOffset = checked(partition.FirstLba * (ulong)sectorSize);
            if (expectedOffset > long.MaxValue || partition.OffsetBytes != (long)expectedOffset)
            {
                findings.Add(new PartitionLayoutFinding(
                    "OFFSET_MISMATCH",
                    PartitionFindingSeverity.Error,
                    "Partition byte offset does not match FirstLba × sector size.",
                    partition.Index));
            }
        }
        catch (OverflowException)
        {
            findings.Add(new PartitionLayoutFinding(
                "OFFSET_OVERFLOW",
                PartitionFindingSeverity.Error,
                "Partition byte offset cannot be represented safely.",
                partition.Index));
        }

        try
        {
            var expectedSize = checked(partition.SectorCount * (ulong)sectorSize);
            if (expectedSize > long.MaxValue || partition.SizeBytes != (long)expectedSize)
            {
                findings.Add(new PartitionLayoutFinding(
                    "SIZE_MISMATCH",
                    PartitionFindingSeverity.Error,
                    "Partition byte size does not match SectorCount × sector size.",
                    partition.Index));
            }
        }
        catch (OverflowException)
        {
            findings.Add(new PartitionLayoutFinding(
                "SIZE_OVERFLOW",
                PartitionFindingSeverity.Error,
                "Partition byte size cannot be represented safely.",
                partition.Index));
        }

        if (partition.OffsetBytes >= 0 && partition.SizeBytes >= 0
            && (partition.OffsetBytes > imageSizeBytes
                || partition.SizeBytes > imageSizeBytes - partition.OffsetBytes))
        {
            findings.Add(new PartitionLayoutFinding(
                "OUT_OF_BOUNDS",
                PartitionFindingSeverity.Error,
                "Partition byte range extends beyond the physical image.",
                partition.Index));
        }
    }

    private static void DetectOverlaps(
        List<PartitionRange> ranges,
        List<PartitionLayoutFinding> findings)
    {
        var ordered = ranges
            .OrderBy(x => x.Partition.FirstLba)
            .ThenBy(x => x.Partition.Index)
            .ToArray();

        PartitionRange? widest = null;
        foreach (var current in ordered)
        {
            if (widest is not null && current.Partition.FirstLba < widest.EndExclusive)
            {
                findings.Add(new PartitionLayoutFinding(
                    "OVERLAP",
                    PartitionFindingSeverity.Error,
                    $"Partition overlaps partition index {widest.Partition.Index} in LBA space.",
                    current.Partition.Index));
            }

            if (widest is null || current.EndExclusive > widest.EndExclusive)
                widest = current;
        }
    }

    private sealed record PartitionRange(PartitionInfo Partition, ulong EndExclusive);
}
