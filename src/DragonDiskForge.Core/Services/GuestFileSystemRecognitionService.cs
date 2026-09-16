using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Performs bounded, read-only filesystem recognition against a proven guest-visible byte source.
/// It uses only the guest bytes exposed by an <see cref="IGuestByteReader"/> and never falls back to
/// physical-container offsets when guest translation is unavailable.
/// </summary>
public sealed class GuestFileSystemRecognitionService
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

    public async Task<GuestFileSystemRecognitionInfo> AnalyzeAsync(
        IGuestByteReader reader,
        string providerId,
        string providerDisplayName,
        PartitionTableInfo? partitionTable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerDisplayName);
        cancellationToken.ThrowIfCancellationRequested();

        if (reader.Length > long.MaxValue)
            throw new NotSupportedException("Guest filesystem recognition currently supports guest address spaces up to Int64.MaxValue bytes.");

        var guestLength = (long)reader.Length;
        var regions = new List<GuestRegion>();
        if (partitionTable is not null)
        {
            var layout = PartitionIntelligenceService.AnalyzeLayout(
                partitionTable,
                guestLength,
                providerId,
                providerDisplayName + " guest");

            if (layout.HasErrors)
            {
                var codes = string.Join(", ", layout.Findings
                    .Where(x => x.Severity == PartitionFindingSeverity.Error)
                    .Select(x => x.Code)
                    .Distinct(StringComparer.Ordinal)
                    .Take(8));
                throw new InvalidDataException(
                    $"Guest filesystem recognition refuses a structurally invalid partition layout. Findings: {codes}.");
            }

            regions.AddRange(layout.Partitions.Select(partition => new GuestRegion(
                partition.Index,
                partition.OffsetBytes,
                partition.SizeBytes)));
        }
        else
        {
            regions.Add(new GuestRegion(null, 0, guestLength));
        }

        var detections = new List<GuestFileSystemDetectionInfo>();
        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRegion(region, guestLength);
            detections.AddRange(await DetectRegionAsync(reader, region, cancellationToken));
        }

        return new GuestFileSystemRecognitionInfo(
            providerId,
            providerDisplayName,
            guestLength,
            Array.AsReadOnly(detections
                .OrderBy(x => x.GuestOffsetBytes)
                .ThenBy(x => x.PartitionIndex ?? 0)
                .ThenBy(x => x.Kind)
                .ToArray()));
    }

    private static async Task<IReadOnlyList<GuestFileSystemDetectionInfo>> DetectRegionAsync(
        IGuestByteReader reader,
        GuestRegion region,
        CancellationToken cancellationToken)
    {
        var detections = new List<GuestFileSystemDetectionInfo>(3);
        if (region.Length <= 0)
            return detections;

        var bootLength = checked((int)Math.Min(BootProbeSize, region.Length));
        if (bootLength > 0)
        {
            var boot = new byte[bootLength];
            await ReadExactlyAtAsync(reader, region.Offset, boot, cancellationToken);

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
            detections.AddRange(await DetectOpticalAsync(reader, region, cancellationToken));

        return detections;
    }

    private static GuestFileSystemDetectionInfo? TryDetectExFat(ReadOnlySpan<byte> boot, GuestRegion region)
    {
        if (boot.Length < 512
            || !boot.Slice(3, 8).SequenceEqual("EXFAT   "u8)
            || boot[510] != 0x55
            || boot[511] != 0xAA)
            return null;

        var bytesPerSectorShift = boot[108];
        var sectorsPerClusterShift = boot[109];
        var fatCount = boot[110];
        if (bytesPerSectorShift is < 9 or > 12
            || sectorsPerClusterShift > 25 - bytesPerSectorShift
            || fatCount is < 1 or > 2)
            return null;

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
            return null;

        return Detection(
            region, FileSystemKind.ExFat, "exFAT", string.Empty,
            serial == 0 ? string.Empty : serial.ToString("X8"),
            bytesPerSector, allocationUnit,
            "EXFAT OEM name, boot signature and bounded exFAT guest geometry");
    }

    private static GuestFileSystemDetectionInfo? TryDetectNtfs(ReadOnlySpan<byte> boot, GuestRegion region)
    {
        if (boot.Length < 512
            || !IsBootJump(boot[0])
            || !boot.Slice(3, 8).SequenceEqual("NTFS    "u8)
            || boot[510] != 0x55
            || boot[511] != 0xAA)
            return null;

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
            return null;

        var totalClusters = totalSectors / sectorsPerCluster;
        if (totalClusters == 0 || mftCluster >= totalClusters)
            return null;

        return Detection(
            region, FileSystemKind.Ntfs, "NTFS", string.Empty,
            serial == 0 ? string.Empty : serial.ToString("X16"),
            bytesPerSector, checked(bytesPerSector * sectorsPerCluster),
            "NTFS OEM name, boot signature and bounded guest BPB geometry");
    }

    private static GuestFileSystemDetectionInfo? TryDetectFat(ReadOnlySpan<byte> boot, GuestRegion region)
    {
        if (boot.Length < 512
            || !IsBootJump(boot[0])
            || boot[510] != 0x55
            || boot[511] != 0xAA)
            return null;

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
            return null;

        var totalSectors = total16 != 0 ? total16 : total32;
        var fatSectors = fat16 != 0 ? fat16 : fat32;
        if (totalSectors == 0 || fatSectors == 0)
            return null;

        var rootDirectorySectors = ((ulong)rootEntryCount * 32UL + (uint)bytesPerSector - 1UL) / bytesPerSector;
        var metadataSectors = (ulong)reservedSectors + ((ulong)fatCount * fatSectors) + rootDirectorySectors;
        if (metadataSectors >= totalSectors)
            return null;

        var clusterCount = ((ulong)totalSectors - metadataSectors) / sectorsPerCluster;
        if (clusterCount == 0 || !FitsRegion(totalSectors, bytesPerSector, region.Length))
            return null;

        var kind = clusterCount < 4_085
            ? FileSystemKind.Fat12
            : clusterCount < 65_525 ? FileSystemKind.Fat16 : FileSystemKind.Fat32;

        if (kind == FileSystemKind.Fat32)
        {
            if (fat16 != 0 || rootEntryCount != 0 || fat32 == 0)
                return null;
        }
        else if (fat16 == 0 || rootEntryCount == 0)
            return null;

        var isFat32 = kind == FileSystemKind.Fat32;
        var extendedSignatureOffset = isFat32 ? 66 : 38;
        var serialOffset = isFat32 ? 67 : 39;
        var labelOffset = isFat32 ? 71 : 43;
        var hasExtendedFields = boot[extendedSignatureOffset] is 0x28 or 0x29;
        var label = hasExtendedFields ? ReadAsciiLabel(boot.Slice(labelOffset, 11)) : string.Empty;
        var serial = hasExtendedFields
            ? BinaryPrimitives.ReadUInt32LittleEndian(boot.Slice(serialOffset, 4))
            : 0u;

        return Detection(
            region,
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
            "FAT BPB cluster-count classification with bounded guest volume geometry");
    }

    private static GuestFileSystemDetectionInfo? TryDetectExt(ReadOnlySpan<byte> boot, GuestRegion region)
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
        var identifier = uuidBytes.IndexOfAnyExcept((byte)0) >= 0 ? FormatUuid(uuidBytes) : string.Empty;
        var label = ReadUtf8Label(super.Slice(120, 16));

        return Detection(
            region,
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
            "ext superblock magic, bounded guest block geometry and feature flags");
    }

    private static async Task<IReadOnlyList<GuestFileSystemDetectionInfo>> DetectOpticalAsync(
        IGuestByteReader reader,
        GuestRegion region,
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
        await ReadExactlyAtAsync(reader, checked(region.Offset + descriptorOffsetWithinRegion), buffer, cancellationToken);

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

        var detections = new List<GuestFileSystemDetectionInfo>(2);
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

                detections.Add(Detection(
                    region,
                    FileSystemKind.Iso9660,
                    joliet is null ? "ISO9660" : "ISO9660 + Joliet",
                    label,
                    string.Empty,
                    logicalBlockSize,
                    logicalBlockSize,
                    joliet is null
                        ? "CD001 primary volume descriptor in guest bytes"
                        : "CD001 primary + Joliet supplementary descriptors in guest bytes"));
            }
        }

        if (beaIndex >= 0 && nsrIndex > beaIndex && teaIndex > nsrIndex)
        {
            detections.Add(Detection(
                region,
                FileSystemKind.Udf,
                udfRevision,
                string.Empty,
                string.Empty,
                OpticalSectorSize,
                OpticalSectorSize,
                $"Guest UDF Volume Recognition Sequence BEA01/{udfRevision}/TEA01"));
        }

        return detections;
    }

    private static GuestFileSystemDetectionInfo Detection(
        GuestRegion region,
        FileSystemKind kind,
        string variant,
        string label,
        string identifier,
        int? logicalBlockSize,
        int? allocationUnitSize,
        string evidence)
        => new(
            region.PartitionIndex,
            region.Offset,
            region.Length,
            kind,
            variant,
            label,
            identifier,
            logicalBlockSize,
            allocationUnitSize,
            evidence);

    private static bool IsJolietDescriptor(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 91)
            return false;
        var escape = descriptor.Slice(88, 3);
        return escape.SequenceEqual("%/@"u8)
            || escape.SequenceEqual("%/C"u8)
            || escape.SequenceEqual("%/E"u8);
    }

    private static void ValidateRegion(GuestRegion region, long guestLength)
    {
        if (region.Offset < 0 || region.Length < 0)
            throw new InvalidDataException("Guest filesystem-recognition region has a negative range.");
        if (region.Offset > guestLength || region.Length > guestLength - region.Offset)
            throw new InvalidDataException("Guest filesystem-recognition region extends beyond the guest address space.");
    }

    private static async ValueTask ReadExactlyAtAsync(
        IGuestByteReader reader,
        long offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (offset < 0)
            throw new EndOfStreamException("Requested guest filesystem-recognition range has a negative offset.");
        var unsignedOffset = (ulong)offset;
        if (unsignedOffset > reader.Length || (ulong)buffer.Length > reader.Length - unsignedOffset)
            throw new EndOfStreamException("Requested guest filesystem-recognition range is outside the guest address space.");
        await reader.ReadExactlyAsync(unsignedOffset, buffer, cancellationToken);
    }

    private static bool FitsRegion(ulong units, ulong bytesPerUnit, long regionLength)
    {
        if (units == 0 || bytesPerUnit == 0 || regionLength < 0)
            return false;
        if (units > ulong.MaxValue / bytesPerUnit)
            return false;
        return units * bytesPerUnit <= (ulong)regionLength;
    }

    private static bool IsValidBytesPerSector(ushort value) => value is 512 or 1024 or 2048 or 4096;
    private static bool IsValidOpticalBlockSize(ushort value) => value is 512 or 1024 or 2048 or 4096;
    private static bool IsPowerOfTwo(byte value) => value != 0 && (value & (value - 1)) == 0;
    private static bool IsBootJump(byte value) => value is 0xEB or 0xE9;
    private static string ReadAsciiLabel(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    private static string ReadUtf8Label(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ');
    private static string ReadJolietLabel(ReadOnlySpan<byte> bytes) => Encoding.BigEndianUnicode.GetString(bytes).TrimEnd('\0', ' ');

    private static string FormatUuid(ReadOnlySpan<byte> bytes)
    {
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }

    private sealed record GuestRegion(int? PartitionIndex, long Offset, long Length);
}
