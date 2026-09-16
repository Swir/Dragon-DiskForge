using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Performs bounded, read-only filesystem recognition against physical bytes in an image file.
/// For partition-capable providers, only structurally valid provider-reported partition byte ranges
/// are scanned. This service does not translate compressed/sparse virtual-disk guest sectors.
/// </summary>
public sealed class FileSystemRecognitionService
{
    private const int BootProbeSize = 4096;
    private const int OpticalSectorSize = 2048;
    private const int FirstOpticalDescriptorLba = 16;
    private const int LastOpticalDescriptorLba = 63;
    private const int OpticalDescriptorCount = LastOpticalDescriptorLba - FirstOpticalDescriptorLba + 1;
    private const int OpticalProbeSize = OpticalDescriptorCount * OpticalSectorSize;
    private const uint ExtHasJournal = 0x0004;
    private const uint ExtIncompat64Bit = 0x0080;
    private const uint Ext4SpecificIncompat = 0x0040 | 0x0080 | 0x0100 | 0x0200 | 0x0400
        | 0x1000 | 0x2000 | 0x4000 | 0x8000 | 0x10000 | 0x20000;

    private readonly ProviderRegistry _registry;

    public FileSystemRecognitionService(ProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<FileSystemRecognitionInfo> AnalyzeAsync(
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
            throw new NotSupportedException("Filesystem recognition requires a registered provider that recognizes the image.");

        var regions = new List<PhysicalRegion>();
        if (resolution.Provider is IPartitionTableProvider partitionProvider
            && resolution.Descriptor.Capabilities.HasFlag(ProviderCapabilities.PartitionTable))
        {
            var table = await partitionProvider.ReadPartitionTableAsync(fullPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var layout = PartitionIntelligenceService.AnalyzeLayout(
                table,
                file.Length,
                resolution.Descriptor.Id,
                resolution.Descriptor.DisplayName);

            if (layout.HasErrors)
            {
                var codes = string.Join(", ", layout.Findings
                    .Where(x => x.Severity == PartitionFindingSeverity.Error)
                    .Select(x => x.Code)
                    .Distinct(StringComparer.Ordinal)
                    .Take(8));
                throw new InvalidDataException(
                    $"Filesystem recognition refuses a structurally invalid partition layout. Findings: {codes}.");
            }

            regions.AddRange(layout.Partitions.Select(partition => new PhysicalRegion(
                partition.Index,
                partition.OffsetBytes,
                partition.SizeBytes)));
        }
        else
        {
            regions.Add(new PhysicalRegion(null, 0, file.Length));
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var detections = new List<FileSystemDetectionInfo>();
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRegion(region, stream.Length);
            detections.AddRange(await DetectRegionAsync(stream, region, cancellationToken));
        }

        return new FileSystemRecognitionInfo(
            resolution.Descriptor.Id,
            resolution.Descriptor.DisplayName,
            stream.Length,
            Array.AsReadOnly(detections
                .OrderBy(x => x.PhysicalOffsetBytes)
                .ThenBy(x => x.PartitionIndex ?? 0)
                .ThenBy(x => x.Kind)
                .ToArray()));
    }

    private static async Task<IReadOnlyList<FileSystemDetectionInfo>> DetectRegionAsync(
        FileStream stream,
        PhysicalRegion region,
        CancellationToken cancellationToken)
    {
        var detections = new List<FileSystemDetectionInfo>(3);
        if (region.Length <= 0)
            return detections;

        var bootLength = checked((int)Math.Min(BootProbeSize, region.Length));
        if (bootLength > 0)
        {
            var boot = new byte[bootLength];
            await ReadExactlyAtAsync(stream, region.Offset, boot, cancellationToken);

            var bootDetection = TryDetectExFat(boot, region)
                ?? TryDetectNtfs(boot, region)
                ?? TryDetectFat(boot, region);

            if (bootDetection is not null)
                detections.Add(bootDetection);
            else
            {
                var ext = TryDetectExt(boot, region);
                if (ext is not null)
                    detections.Add(ext);
            }
        }

        if (region.Length >= (FirstOpticalDescriptorLba + 1L) * OpticalSectorSize)
            detections.AddRange(await DetectOpticalAsync(stream, region, cancellationToken));

        return detections;
    }

    private static FileSystemDetectionInfo? TryDetectExFat(
        ReadOnlySpan<byte> boot,
        PhysicalRegion region)
    {
        if (boot.Length < 512
            || !boot.Slice(3, 8).SequenceEqual("EXFAT   "u8)
            || boot[510] != 0x55
            || boot[511] != 0xAA)
        {
            return null;
        }

        var bytesPerSectorShift = boot[108];
        var sectorsPerClusterShift = boot[109];
        var fatCount = boot[110];
        if (bytesPerSectorShift is < 9 or > 12
            || sectorsPerClusterShift > 25 - bytesPerSectorShift
            || fatCount is < 1 or > 2)
        {
            return null;
        }

        var bytesPerSector = 1 << bytesPerSectorShift;
        var sectorsPerCluster = 1 << sectorsPerClusterShift;
        var allocationUnit = checked(bytesPerSector * sectorsPerCluster);
        var volumeLength = BinaryPrimitives.ReadUInt64LittleEndian(boot.Slice(72, 8));
        var fatOffset = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(80, 4));
        var fatLength = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(84, 4));
        var clusterHeapOffset = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(88, 4));
        var clusterCount = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(92, 4));
        var rootDirectoryCluster = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(96, 4));
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(100, 4));

        if (volumeLength == 0
            || fatOffset == 0
            || fatLength == 0
            || clusterCount == 0
            || clusterHeapOffset <= fatOffset
            || (ulong)fatOffset + fatLength > clusterHeapOffset
            || clusterHeapOffset >= volumeLength
            || rootDirectoryCluster < 2
            || rootDirectoryCluster > (ulong)clusterCount + 1
            || !FitsRegion(volumeLength, (ulong)bytesPerSector, region.Length))
        {
            return null;
        }

        return new FileSystemDetectionInfo(
            region.PartitionIndex,
            region.Offset,
            region.Length,
            FileSystemKind.ExFat,
            "exFAT",
            string.Empty,
            serial == 0 ? string.Empty : serial.ToString("X8"),
            bytesPerSector,
            allocationUnit,
            "EXFAT OEM name, boot signature and bounded exFAT geometry");
    }

    private static FileSystemDetectionInfo? TryDetectNtfs(
        ReadOnlySpan<byte> boot,
        PhysicalRegion region)
    {
        if (boot.Length < 512
            || !IsBootJump(boot[0])
            || !boot.Slice(3, 8).SequenceEqual("NTFS    "u8)
            || boot[510] != 0x55
            || boot[511] != 0xAA)
        {
            return null;
        }

        var bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(11, 2));
        var sectorsPerCluster = boot[13];
        var totalSectors = BinaryPrimitives.ReadUInt64LittleEndian(boot.Slice(40, 8));
        var mftCluster = BinaryPrimitives.ReadUInt64LittleEndian(boot.Slice(48, 8));
        var serial = BinaryPrimitives.ReadUInt64LittleEndian(boot.Slice(72, 8));

        if (!IsValidBytesPerSector(bytesPerSector)
            || !IsPowerOfTwo(sectorsPerCluster)
            || sectorsPerCluster > 128
            || totalSectors == 0
            || !FitsRegion(totalSectors, bytesPerSector, region.Length))
        {
            return null;
        }

        var totalClusters = totalSectors / sectorsPerCluster;
        if (totalClusters == 0 || mftCluster >= totalClusters)
            return null;

        return new FileSystemDetectionInfo(
            region.PartitionIndex,
            region.Offset,
            region.Length,
            FileSystemKind.Ntfs,
            "NTFS",
            string.Empty,
            serial == 0 ? string.Empty : serial.ToString("X16"),
            bytesPerSector,
            checked(bytesPerSector * sectorsPerCluster),
            "NTFS OEM name, boot signature and bounded BPB geometry");
    }

    private static FileSystemDetectionInfo? TryDetectFat(
        ReadOnlySpan<byte> boot,
        PhysicalRegion region)
    {
        if (boot.Length < 512
            || !IsBootJump(boot[0])
            || boot[510] != 0x55
            || boot[511] != 0xAA)
        {
            return null;
        }

        var bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(11, 2));
        var sectorsPerCluster = boot[13];
        var reservedSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(14, 2));
        var fatCount = boot[16];
        var rootEntryCount = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(17, 2));
        var total16 = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(19, 2));
        var mediaDescriptor = boot[21];
        var fat16 = BinaryPrimitives.ReadUInt16LittleEndian(boot.Slice(22, 2));
        var total32 = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(32, 4));
        var fat32 = BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(36, 4));

        if (!IsValidBytesPerSector(bytesPerSector)
            || !IsPowerOfTwo(sectorsPerCluster)
            || sectorsPerCluster > 128
            || reservedSectors == 0
            || fatCount is < 1 or > 4
            || (mediaDescriptor != 0xF0 && mediaDescriptor < 0xF8))
        {
            return null;
        }

        var totalSectors = total16 != 0 ? total16 : total32;
        var fatSectors = fat16 != 0 ? fat16 : fat32;
        if (totalSectors == 0 || fatSectors == 0)
            return null;

        var rootDirectorySectors = ((ulong)rootEntryCount * 32UL + (uint)bytesPerSector - 1UL)
            / bytesPerSector;
        var metadataSectors = (ulong)reservedSectors
            + ((ulong)fatCount * fatSectors)
            + rootDirectorySectors;
        if (metadataSectors >= totalSectors)
            return null;

        var dataSectors = (ulong)totalSectors - metadataSectors;
        var clusterCount = dataSectors / sectorsPerCluster;
        if (clusterCount == 0 || !FitsRegion(totalSectors, bytesPerSector, region.Length))
            return null;

        FileSystemKind kind;
        if (clusterCount < 4_085)
            kind = FileSystemKind.Fat12;
        else if (clusterCount < 65_525)
            kind = FileSystemKind.Fat16;
        else
            kind = FileSystemKind.Fat32;

        if (kind == FileSystemKind.Fat32)
        {
            if (fat16 != 0 || rootEntryCount != 0 || fat32 == 0)
                return null;
        }
        else if (fat16 == 0 || rootEntryCount == 0)
        {
            return null;
        }

        var isFat32 = kind == FileSystemKind.Fat32;
        var extendedSignatureOffset = isFat32 ? 66 : 38;
        var serialOffset = isFat32 ? 67 : 39;
        var labelOffset = isFat32 ? 71 : 43;
        var hasExtendedFields = boot[extendedSignatureOffset] is 0x28 or 0x29;
        var label = hasExtendedFields ? ReadAsciiLabel(boot.Slice(labelOffset, 11)) : string.Empty;
        var serial = hasExtendedFields
            ? BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(serialOffset, 4))
            : 0u;

        return new FileSystemDetectionInfo(
            region.PartitionIndex,
            region.Offset,
            region.Length,
            kind,
            kind switch
            {
                FileSystemKind.Fat12 => "FAT12",
                FileSystemKind.Fat16 => "FAT16",
                _ => "FAT32"
            },
            label,
            serial == 0 ? string.Empty : serial.ToString("X8"),
            bytesPerSector,
            checked(bytesPerSector * sectorsPerCluster),
            "FAT BPB cluster-count classification with bounded volume geometry");
    }

    private static FileSystemDetectionInfo? TryDetectExt(
        ReadOnlySpan<byte> boot,
        PhysicalRegion region)
    {
        const int superblockOffset = 1024;
        const int minimumSuperblockBytes = 340;
        if (boot.Length < superblockOffset + minimumSuperblockBytes)
            return null;

        var super = boot[superblockOffset..];
        if (BinaryPrimitives.ReadUInt16LittleEndian(super.Slice(56, 2)) != 0xEF53)
            return null;

        var blocksLow = BinaryPrimitives.ReadUInt32LittleEndian(super.Slice(4, 4));
        var logBlockSize = BinaryPrimitives.ReadUInt32LittleEndian(super.Slice(24, 4));
        var blocksPerGroup = BinaryPrimitives.ReadUInt32LittleEndian(super.Slice(32, 4));
        var inodesPerGroup = BinaryPrimitives.ReadUInt32LittleEndian(super.Slice(40, 4));
        var compat = BinaryPrimitives.ReadUInt32LittleEndian(super.Slice(92, 4));
        var incompat = BinaryPrimitives.ReadUInt32LittleEndian(super.Slice(96, 4));

        if (blocksLow == 0 || logBlockSize > 6 || blocksPerGroup == 0 || inodesPerGroup == 0)
            return null;

        var blockSize = checked(1024 << (int)logBlockSize);
        var blocksHigh = (incompat & ExtIncompat64Bit) != 0
            ? BinaryPrimitives.ReadUInt32LittleEndian(super.Slice(336, 4))
            : 0u;
        var blockCount = blocksLow | ((ulong)blocksHigh << 32);
        if (!FitsRegion(blockCount, (ulong)blockSize, region.Length))
            return null;

        var kind = (incompat & Ext4SpecificIncompat) != 0
            ? FileSystemKind.Ext4
            : (compat & ExtHasJournal) != 0 ? FileSystemKind.Ext3 : FileSystemKind.Ext2;

        var uuidBytes = super.Slice(104, 16);
        var identifier = uuidBytes.IndexOfAnyExcept((byte)0) >= 0
            ? FormatUuid(uuidBytes)
            : string.Empty;
        var label = ReadUtf8Label(super.Slice(120, 16));

        return new FileSystemDetectionInfo(
            region.PartitionIndex,
            region.Offset,
            region.Length,
            kind,
            kind switch
            {
                FileSystemKind.Ext2 => "ext2",
                FileSystemKind.Ext3 => "ext3 (journal feature)",
                _ => "ext4 (ext4-specific feature set)"
            },
            label,
            identifier,
            blockSize,
            blockSize,
            "ext superblock magic, bounded block geometry and feature flags");
    }

    private static async Task<IReadOnlyList<FileSystemDetectionInfo>> DetectOpticalAsync(
        FileStream stream,
        PhysicalRegion region,
        CancellationToken cancellationToken)
    {
        var descriptorOffsetWithinRegion = FirstOpticalDescriptorLba * (long)OpticalSectorSize;
        if (descriptorOffsetWithinRegion >= region.Length)
            return [];

        var available = region.Length - descriptorOffsetWithinRegion;
        var probeLength = checked((int)Math.Min(OpticalProbeSize, available));
        if (probeLength < OpticalSectorSize)
            return [];

        var buffer = new byte[probeLength];
        var physicalOffset = checked(region.Offset + descriptorOffsetWithinRegion);
        await ReadExactlyAtAsync(stream, physicalOffset, buffer, cancellationToken);

        byte[]? primary = null;
        byte[]? joliet = null;
        var beaIndex = -1;
        var nsrIndex = -1;
        var teaIndex = -1;
        var udfRevision = string.Empty;

        var sectorCount = buffer.Length / OpticalSectorSize;
        for (var index = 0; index < sectorCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sector = buffer.AsSpan(index * OpticalSectorSize, OpticalSectorSize);

            if (sector.Slice(1, 5).SequenceEqual("CD001"u8) && sector[6] == 1)
            {
                if (sector[0] == 1 && primary is null)
                    primary = sector.ToArray();
                else if (sector[0] == 2 && joliet is null && IsJolietDescriptor(sector))
                    joliet = sector.ToArray();
            }

            var standardIdentifier = Encoding.ASCII.GetString(sector.Slice(1, 5));
            if (standardIdentifier == "BEA01" && beaIndex < 0)
                beaIndex = index;
            else if ((standardIdentifier == "NSR02" || standardIdentifier == "NSR03") && nsrIndex < 0)
            {
                nsrIndex = index;
                udfRevision = standardIdentifier;
            }
            else if (standardIdentifier == "TEA01" && teaIndex < 0)
                teaIndex = index;
        }

        var detections = new List<FileSystemDetectionInfo>(2);
        if (primary is not null)
        {
            var logicalBlockSize = BinaryPrimitives.ReadUInt16LittleEndian(primary.AsSpan(128, 2));
            var volumeSpaceSize = BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(80, 4));
            if (IsValidOpticalBlockSize(logicalBlockSize)
                && volumeSpaceSize > 0
                && FitsRegion(volumeSpaceSize, logicalBlockSize, region.Length))
            {
                var label = joliet is not null
                    ? ReadJolietLabel(joliet.AsSpan(40, 32))
                    : ReadAsciiLabel(primary.AsSpan(40, 32));
                if (string.IsNullOrWhiteSpace(label))
                    label = ReadAsciiLabel(primary.AsSpan(40, 32));

                detections.Add(new FileSystemDetectionInfo(
                    region.PartitionIndex,
                    region.Offset,
                    region.Length,
                    FileSystemKind.Iso9660,
                    joliet is null ? "ISO9660" : "ISO9660 + Joliet",
                    label,
                    string.Empty,
                    logicalBlockSize,
                    logicalBlockSize,
                    joliet is null
                        ? "CD001 primary volume descriptor"
                        : "CD001 primary + Joliet supplementary volume descriptors"));
            }
        }

        if (beaIndex >= 0 && nsrIndex > beaIndex && teaIndex > nsrIndex)
        {
            detections.Add(new FileSystemDetectionInfo(
                region.PartitionIndex,
                region.Offset,
                region.Length,
                FileSystemKind.Udf,
                udfRevision,
                string.Empty,
                string.Empty,
                OpticalSectorSize,
                OpticalSectorSize,
                $"UDF Volume Recognition Sequence BEA01/{udfRevision}/TEA01"));
        }

        return detections;
    }

    private static bool IsJolietDescriptor(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 91)
            return false;

        var escape = descriptor.Slice(88, 3);
        return escape.SequenceEqual("%/@"u8)
            || escape.SequenceEqual("%/C"u8)
            || escape.SequenceEqual("%/E"u8);
    }

    private static void ValidateRegion(PhysicalRegion region, long imageLength)
    {
        if (region.Offset < 0 || region.Length < 0)
            throw new InvalidDataException("Filesystem recognition region has a negative physical range.");
        if (region.Offset > imageLength || region.Length > imageLength - region.Offset)
            throw new InvalidDataException("Filesystem recognition region extends beyond the physical image.");
    }

    private static async ValueTask ReadExactlyAtAsync(
        FileStream stream,
        long offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > stream.Length || buffer.Length > stream.Length - offset)
            throw new EndOfStreamException("Requested filesystem-recognition range is outside the image.");

        stream.Position = offset;
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of image during filesystem recognition.");
            totalRead += read;
        }
    }

    private static bool FitsRegion(ulong units, ulong bytesPerUnit, long regionLength)
    {
        if (units == 0 || bytesPerUnit == 0 || regionLength < 0)
            return false;
        if (units > ulong.MaxValue / bytesPerUnit)
            return false;
        return units * bytesPerUnit <= (ulong)regionLength;
    }

    private static bool IsValidBytesPerSector(ushort value)
        => value is 512 or 1024 or 2048 or 4096;

    private static bool IsValidOpticalBlockSize(ushort value)
        => value is 512 or 1024 or 2048 or 4096;

    private static bool IsPowerOfTwo(byte value)
        => value != 0 && (value & (value - 1)) == 0;

    private static bool IsBootJump(byte value)
        => value is 0xEB or 0xE9;

    private static string ReadAsciiLabel(ReadOnlySpan<byte> bytes)
        => Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');

    private static string ReadUtf8Label(ReadOnlySpan<byte> bytes)
        => Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ');

    private static string ReadJolietLabel(ReadOnlySpan<byte> bytes)
        => Encoding.BigEndianUnicode.GetString(bytes).TrimEnd('\0', ' ');

    private static string FormatUuid(ReadOnlySpan<byte> bytes)
    {
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }

    private sealed record PhysicalRegion(int? PartitionIndex, long Offset, long Length);
}
