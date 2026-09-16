using System.Buffers.Binary;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Aggregates bounded, read-only image intelligence from already-proven provider capabilities.
/// It never invents guest-sector access for sparse/compressed containers and never mounts,
/// repairs or writes an image.
/// </summary>
public sealed class ImageIntelligenceService
{
    private readonly ProviderRegistry _registry;
    private readonly PartitionIntelligenceService _partitionService;
    private readonly FileSystemRecognitionService _fileSystemService;
    private readonly BootInstallerIntelligenceService _bootInstallerService;

    public ImageIntelligenceService(ProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _partitionService = new PartitionIntelligenceService(registry);
        _fileSystemService = new FileSystemRecognitionService(registry);
        _bootInstallerService = new BootInstallerIntelligenceService(registry);
    }

    public async Task<ImageIntelligenceInfo> AnalyzeAsync(
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
        if (resolution.Provider is null || resolution.Descriptor is null)
            throw new NotSupportedException("Image intelligence requires a registered provider that recognizes the image.");

        var descriptor = resolution.Descriptor;
        PartitionIntelligenceInfo? partitions = null;
        FileSystemRecognitionInfo? fileSystems = null;
        BootInstallerIntelligenceInfo? bootInstaller = null;

        if (descriptor.Capabilities.HasFlag(ProviderCapabilities.PartitionTable))
            partitions = await _partitionService.AnalyzeAsync(fullPath, cancellationToken);

        if (CanMapPhysicalFilesystemBytes(descriptor.Capabilities))
            fileSystems = await _fileSystemService.AnalyzeAsync(fullPath, cancellationToken);

        if (descriptor.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse))
            bootInstaller = await _bootInstallerService.AnalyzeAsync(fullPath, cancellationToken);

        var identity = new List<ImageIdentityEvidence>();
        if (partitions is not null)
        {
            foreach (var partition in partitions.Partitions)
            {
                if (!string.IsNullOrWhiteSpace(partition.Name))
                {
                    identity.Add(new ImageIdentityEvidence(
                        "partition-name",
                        partition.Name.Trim(),
                        "partition-table",
                        partition.Index));
                }
            }
        }

        if (fileSystems is not null)
        {
            foreach (var detection in fileSystems.Detections)
            {
                if (!string.IsNullOrWhiteSpace(detection.Label))
                {
                    identity.Add(new ImageIdentityEvidence(
                        "filesystem-label",
                        detection.Label.Trim(),
                        detection.DisplayName,
                        detection.PartitionIndex));
                }

                if (!string.IsNullOrWhiteSpace(detection.Identifier))
                {
                    identity.Add(new ImageIdentityEvidence(
                        "filesystem-id",
                        detection.Identifier.Trim(),
                        detection.DisplayName,
                        detection.PartitionIndex));
                }
            }
        }

        if (resolution.Provider is IWimMetadataProvider wimProvider
            && descriptor.Capabilities.HasFlag(ProviderCapabilities.ContainerMetadata))
        {
            var metadata = await wimProvider.ReadWimMetadataAsync(fullPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(metadata.Guid))
                identity.Add(new ImageIdentityEvidence("container-guid", metadata.Guid.Trim(), "WIM/ESD"));
        }

        if (resolution.Provider is IFfuMetadataProvider ffuProvider
            && descriptor.Capabilities.HasFlag(ProviderCapabilities.ContainerMetadata))
        {
            var metadata = await ffuProvider.ReadFfuMetadataAsync(fullPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(metadata.Store.PlatformId))
                identity.Add(new ImageIdentityEvidence("platform-id", metadata.Store.PlatformId.Trim(), "FFU"));
        }

        var architectures = bootInstaller?.ArchitectureHints
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();

        var health = new List<ImageHealthFinding>();
        if (partitions is not null)
        {
            health.AddRange(partitions.Findings.Select(finding => new ImageHealthFinding(
                "PARTITION_" + finding.Code,
                finding.Severity switch
                {
                    PartitionFindingSeverity.Error => ImageHealthSeverity.Error,
                    PartitionFindingSeverity.Warning => ImageHealthSeverity.Warning,
                    _ => ImageHealthSeverity.Info
                },
                finding.Message,
                "partition-table",
                finding.PartitionIndex)));
        }

        if (fileSystems is not null && fileSystems.Detections.Count > 0)
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.RandomAccess);

            foreach (var detection in fileSystems.Detections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await AnalyzeFileSystemHealthAsync(stream, detection, health, cancellationToken);
            }
        }

        var normalizedIdentity = identity
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(
                x => $"{x.Kind}\u001f{x.Value}\u001f{x.PartitionIndex?.ToString() ?? string.Empty}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PartitionIndex ?? int.MinValue)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedHealth = health
            .GroupBy(
                x => $"{x.Code}\u001f{x.PartitionIndex?.ToString() ?? string.Empty}\u001f{x.Message}",
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.PartitionIndex ?? int.MinValue)
            .ToArray();

        return new ImageIntelligenceInfo(
            descriptor.Id,
            descriptor.DisplayName,
            file.Length,
            partitions,
            fileSystems,
            bootInstaller,
            Array.AsReadOnly(normalizedIdentity),
            Array.AsReadOnly(architectures),
            Array.AsReadOnly(normalizedHealth));
    }

    private static bool CanMapPhysicalFilesystemBytes(ProviderCapabilities capabilities)
        => capabilities.HasFlag(ProviderCapabilities.PartitionTable)
            || capabilities.HasFlag(ProviderCapabilities.MediaGeometry)
            || capabilities.HasFlag(ProviderCapabilities.DirectBrowse);

    private static async Task AnalyzeFileSystemHealthAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        switch (detection.Kind)
        {
            case FileSystemKind.ExFat:
                await AnalyzeExFatAsync(stream, detection, findings, cancellationToken);
                break;
            case FileSystemKind.Ntfs:
                await AnalyzeNtfsAsync(stream, detection, findings, cancellationToken);
                break;
            case FileSystemKind.Fat32:
                await AnalyzeFat32Async(stream, detection, findings, cancellationToken);
                break;
            case FileSystemKind.Ext2:
            case FileSystemKind.Ext3:
            case FileSystemKind.Ext4:
                await AnalyzeExtAsync(stream, detection, findings, cancellationToken);
                break;
        }
    }

    private static async Task AnalyzeExFatAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        var boot = await ReadBoundedAsync(stream, detection.PhysicalOffsetBytes, 512, detection, cancellationToken);
        var volumeFlags = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(106, 2));

        if ((volumeFlags & 0x0002) != 0)
        {
            findings.Add(new ImageHealthFinding(
                "EXFAT_VOLUME_DIRTY",
                ImageHealthSeverity.Warning,
                "The exFAT VolumeDirty flag is set.",
                "exFAT boot region",
                detection.PartitionIndex));
        }

        if ((volumeFlags & 0x0004) != 0)
        {
            findings.Add(new ImageHealthFinding(
                "EXFAT_MEDIA_FAILURE",
                ImageHealthSeverity.Error,
                "The exFAT MediaFailure flag is set.",
                "exFAT boot region",
                detection.PartitionIndex));
        }
    }

    private static async Task AnalyzeExtAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        var probe = await ReadBoundedAsync(stream, detection.PhysicalOffsetBytes, 1088, detection, cancellationToken);
        var state = BinaryPrimitives.ReadUInt16LittleEndian(probe.AsSpan(1024 + 58, 2));

        if ((state & 0x0002) != 0)
        {
            findings.Add(new ImageHealthFinding(
                "EXT_ERRORS_RECORDED",
                ImageHealthSeverity.Error,
                "The ext superblock reports filesystem errors.",
                detection.DisplayName + " superblock",
                detection.PartitionIndex));
        }
        else if ((state & 0x0001) == 0)
        {
            findings.Add(new ImageHealthFinding(
                "EXT_NOT_CLEAN",
                ImageHealthSeverity.Warning,
                "The ext superblock does not report a clean filesystem state.",
                detection.DisplayName + " superblock",
                detection.PartitionIndex));
        }
    }

    private static async Task AnalyzeNtfsAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        var sectorSize = detection.LogicalBlockSize.GetValueOrDefault(512);
        if (sectorSize < 512 || sectorSize > 4096)
            return;

        var primary = await ReadBoundedAsync(stream, detection.PhysicalOffsetBytes, sectorSize, detection, cancellationToken);
        var totalSectors = BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(40, 8));
        if (totalSectors < 2)
            return;

        ulong relativeBackup;
        try
        {
            relativeBackup = checked((totalSectors - 1) * (ulong)sectorSize);
        }
        catch (OverflowException)
        {
            findings.Add(new ImageHealthFinding(
                "NTFS_BACKUP_BOOT_RANGE_OVERFLOW",
                ImageHealthSeverity.Error,
                "The NTFS backup boot-sector location overflows the supported range.",
                "NTFS boot metadata",
                detection.PartitionIndex));
            return;
        }

        if (relativeBackup > (ulong)Math.Max(0, detection.RegionSizeBytes - sectorSize))
        {
            findings.Add(new ImageHealthFinding(
                "NTFS_BACKUP_BOOT_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The NTFS backup boot sector lies outside the bounded filesystem region.",
                "NTFS boot metadata",
                detection.PartitionIndex));
            return;
        }

        var backupOffset = checked(detection.PhysicalOffsetBytes + (long)relativeBackup);
        var backup = await ReadBoundedAsync(stream, backupOffset, sectorSize, detection, cancellationToken);
        if (!primary.AsSpan(0, 84).SequenceEqual(backup.AsSpan(0, 84))
            || !primary.AsSpan(510, 2).SequenceEqual(backup.AsSpan(510, 2)))
        {
            findings.Add(new ImageHealthFinding(
                "NTFS_BACKUP_BOOT_MISMATCH",
                ImageHealthSeverity.Warning,
                "The NTFS primary and backup boot metadata do not match.",
                "NTFS boot metadata",
                detection.PartitionIndex));
        }
    }

    private static async Task AnalyzeFat32Async(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        var primary = await ReadBoundedAsync(stream, detection.PhysicalOffsetBytes, 512, detection, cancellationToken);
        var bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(11, 2));
        var backupSector = BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(50, 2));
        if (bytesPerSector < 512 || backupSector is 0 or 0xFFFF)
            return;

        var relativeBackup = (long)backupSector * bytesPerSector;
        if (relativeBackup < 0 || relativeBackup > detection.RegionSizeBytes - 512)
        {
            findings.Add(new ImageHealthFinding(
                "FAT32_BACKUP_BOOT_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The FAT32 backup boot-sector location lies outside the bounded filesystem region.",
                "FAT32 BPB",
                detection.PartitionIndex));
            return;
        }

        var backup = await ReadBoundedAsync(
            stream,
            checked(detection.PhysicalOffsetBytes + relativeBackup),
            512,
            detection,
            cancellationToken);

        if (!primary.AsSpan(0, 90).SequenceEqual(backup.AsSpan(0, 90))
            || !primary.AsSpan(510, 2).SequenceEqual(backup.AsSpan(510, 2)))
        {
            findings.Add(new ImageHealthFinding(
                "FAT32_BACKUP_BOOT_MISMATCH",
                ImageHealthSeverity.Warning,
                "The FAT32 primary and backup boot metadata do not match.",
                "FAT32 BPB",
                detection.PartitionIndex));
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        FileStream stream,
        long absoluteOffset,
        int length,
        FileSystemDetectionInfo detection,
        CancellationToken cancellationToken)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (absoluteOffset < detection.PhysicalOffsetBytes)
            throw new InvalidDataException("Filesystem health read begins before the bounded filesystem region.");

        long relative;
        try
        {
            relative = checked(absoluteOffset - detection.PhysicalOffsetBytes);
        }
        catch (OverflowException)
        {
            throw new InvalidDataException("Filesystem health read offset overflowed.");
        }

        if (relative > detection.RegionSizeBytes || length > detection.RegionSizeBytes - relative)
            throw new InvalidDataException("Filesystem health read exceeds the bounded filesystem region.");
        if (absoluteOffset > stream.Length || length > stream.Length - absoluteOffset)
            throw new InvalidDataException("Filesystem health read exceeds the physical image.");

        var buffer = new byte[length];
        stream.Position = absoluteOffset;
        await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken);
        return buffer;
    }
}
