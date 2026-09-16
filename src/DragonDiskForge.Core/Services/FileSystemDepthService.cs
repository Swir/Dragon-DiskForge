using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Performs bounded read-only filesystem checks that intentionally sit beyond basic recognition.
/// The service never traverses a filesystem and never reads outside a previously recognized
/// physical filesystem region.
/// </summary>
public sealed class FileSystemDepthService
{
    private const int MaxUdfDescriptorSequenceBytes = 16 * 1024 * 1024;
    private const ushort UdfPrimaryVolumeDescriptorTagId = 1;
    private const ushort UdfAnchorVolumeDescriptorPointerTagId = 2;
    private const ushort UdfLogicalVolumeDescriptorTagId = 6;
    private const ushort UdfTerminatingDescriptorTagId = 8;

    public async Task<FileSystemDepthInfo> AnalyzeAsync(
        string imagePath,
        FileSystemRecognitionInfo recognition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(recognition);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Disk image was not found.", fullPath);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var identity = new List<ImageIdentityEvidence>();
        var health = new List<ImageHealthFinding>();

        foreach (var detection in recognition.Detections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDetectionRegion(detection, stream.Length);

            switch (detection.Kind)
            {
                case FileSystemKind.ExFat:
                    await AnalyzeExFatBootRegionAsync(stream, detection, health, cancellationToken);
                    break;
                case FileSystemKind.Fat32:
                    await AnalyzeFat32FsInfoAsync(stream, detection, health, cancellationToken);
                    break;
                case FileSystemKind.Udf:
                    await AnalyzeUdfMetadataAsync(stream, detection, identity, health, cancellationToken);
                    break;
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

        return new FileSystemDepthInfo(
            Array.AsReadOnly(normalizedIdentity),
            Array.AsReadOnly(normalizedHealth));
    }

    private static async Task AnalyzeExFatBootRegionAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        var sectorSize = detection.LogicalBlockSize.GetValueOrDefault(512);
        if (sectorSize is < 512 or > 4096 || !IsPowerOfTwo(sectorSize))
            return;

        var requiredLength = checked(24L * sectorSize);
        if (detection.RegionSizeBytes < requiredLength)
        {
            findings.Add(new ImageHealthFinding(
                "EXFAT_BOOT_REGION_TRUNCATED",
                ImageHealthSeverity.Error,
                "The recognized exFAT region is too small to contain both 12-sector boot regions.",
                "exFAT boot region",
                detection.PartitionIndex));
            return;
        }

        var bootRegions = await ReadBoundedAsync(
            stream,
            detection.PhysicalOffsetBytes,
            checked((int)requiredLength),
            detection,
            cancellationToken);

        var main = bootRegions.AsSpan(0, 12 * sectorSize);
        var backup = bootRegions.AsSpan(12 * sectorSize, 12 * sectorSize);
        var mainChecksum = ComputeExFatBootChecksum(main[..(11 * sectorSize)]);
        var backupChecksum = ComputeExFatBootChecksum(backup[..(11 * sectorSize)]);

        if (!ChecksumSectorMatches(main.Slice(11 * sectorSize, sectorSize), mainChecksum))
        {
            findings.Add(new ImageHealthFinding(
                "EXFAT_MAIN_BOOT_CHECKSUM_MISMATCH",
                ImageHealthSeverity.Error,
                "The exFAT main boot-region checksum sector does not match the bounded main boot region.",
                "exFAT main boot region",
                detection.PartitionIndex));
        }

        if (!ChecksumSectorMatches(backup.Slice(11 * sectorSize, sectorSize), backupChecksum))
        {
            findings.Add(new ImageHealthFinding(
                "EXFAT_BACKUP_BOOT_CHECKSUM_MISMATCH",
                ImageHealthSeverity.Error,
                "The exFAT backup boot-region checksum sector does not match the bounded backup boot region.",
                "exFAT backup boot region",
                detection.PartitionIndex));
        }

        if (!ExFatBootCopiesEquivalent(main[..(11 * sectorSize)], backup[..(11 * sectorSize)]))
        {
            findings.Add(new ImageHealthFinding(
                "EXFAT_BACKUP_BOOT_MISMATCH",
                ImageHealthSeverity.Warning,
                "The exFAT main and backup boot regions differ outside the mutable VolumeFlags/PercentInUse bytes.",
                "exFAT boot region",
                detection.PartitionIndex));
        }

        var percentInUse = main[112];
        if (percentInUse != 0xFF && percentInUse > 100)
        {
            findings.Add(new ImageHealthFinding(
                "EXFAT_PERCENT_IN_USE_INVALID",
                ImageHealthSeverity.Warning,
                $"The exFAT PercentInUse field contains the invalid value {percentInUse}.",
                "exFAT main boot sector",
                detection.PartitionIndex));
        }
    }

    private static async Task AnalyzeFat32FsInfoAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        var boot = await ReadBoundedAsync(
            stream,
            detection.PhysicalOffsetBytes,
            512,
            detection,
            cancellationToken);

        var bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11, 2));
        var sectorsPerCluster = boot[13];
        var reservedSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14, 2));
        var fatCount = boot[16];
        var totalSectors = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(19, 2));
        if (totalSectors == 0)
            totalSectors = checked((ushort)0);
        var totalSectors32 = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(32, 4));
        var fatSectors = BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(36, 4));
        var fsInfoSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(48, 2));

        if (bytesPerSector is < 512 or > 4096
            || !IsPowerOfTwo(bytesPerSector)
            || !IsPowerOfTwo(sectorsPerCluster)
            || sectorsPerCluster == 0
            || reservedSectors == 0
            || fatCount == 0
            || fatSectors == 0
            || fsInfoSector is 0 or 0xFFFF)
        {
            return;
        }

        if (fsInfoSector >= reservedSectors)
        {
            findings.Add(new ImageHealthFinding(
                "FAT32_FSINFO_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The FAT32 FSInfo sector lies outside the reserved-sector area.",
                "FAT32 BPB",
                detection.PartitionIndex));
            return;
        }

        var fsInfoRelativeOffset = checked((long)fsInfoSector * bytesPerSector);
        if (fsInfoRelativeOffset > detection.RegionSizeBytes - bytesPerSector)
        {
            findings.Add(new ImageHealthFinding(
                "FAT32_FSINFO_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The FAT32 FSInfo sector lies outside the bounded filesystem region.",
                "FAT32 BPB",
                detection.PartitionIndex));
            return;
        }

        var fsInfo = await ReadBoundedAsync(
            stream,
            checked(detection.PhysicalOffsetBytes + fsInfoRelativeOffset),
            bytesPerSector,
            detection,
            cancellationToken);

        var leadSignature = BinaryPrimitives.ReadUInt32LittleEndian(fsInfo.AsSpan(0, 4));
        var structureSignature = BinaryPrimitives.ReadUInt32LittleEndian(fsInfo.AsSpan(484, 4));
        var trailSignature = BinaryPrimitives.ReadUInt32LittleEndian(fsInfo.AsSpan(508, 4));
        if (leadSignature != 0x41615252
            || structureSignature != 0x61417272
            || trailSignature != 0xAA550000)
        {
            findings.Add(new ImageHealthFinding(
                "FAT32_FSINFO_SIGNATURE_INVALID",
                ImageHealthSeverity.Warning,
                "The FAT32 FSInfo sector does not contain the required lead/structure/trail signatures.",
                "FAT32 FSInfo",
                detection.PartitionIndex));
            return;
        }

        var total = totalSectors != 0 ? totalSectors : totalSectors32;
        var metadataSectors = (ulong)reservedSectors + ((ulong)fatCount * fatSectors);
        if (total == 0 || metadataSectors >= total)
            return;

        var clusterCount = ((ulong)total - metadataSectors) / sectorsPerCluster;
        if (clusterCount == 0)
            return;

        var freeCount = BinaryPrimitives.ReadUInt32LittleEndian(fsInfo.AsSpan(488, 4));
        if (freeCount != uint.MaxValue && freeCount > clusterCount)
        {
            findings.Add(new ImageHealthFinding(
                "FAT32_FSINFO_FREE_COUNT_INVALID",
                ImageHealthSeverity.Warning,
                $"The FAT32 FSInfo free-cluster count ({freeCount}) exceeds the bounded data-cluster count ({clusterCount}).",
                "FAT32 FSInfo",
                detection.PartitionIndex));
        }

        var nextFree = BinaryPrimitives.ReadUInt32LittleEndian(fsInfo.AsSpan(492, 4));
        if (nextFree != uint.MaxValue && (nextFree < 2 || (ulong)nextFree > clusterCount + 1))
        {
            findings.Add(new ImageHealthFinding(
                "FAT32_FSINFO_NEXT_FREE_INVALID",
                ImageHealthSeverity.Warning,
                $"The FAT32 FSInfo next-free hint ({nextFree}) lies outside the bounded cluster range.",
                "FAT32 FSInfo",
                detection.PartitionIndex));
        }
    }

    private static async Task AnalyzeUdfMetadataAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageIdentityEvidence> identity,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        var blockSize = detection.LogicalBlockSize.GetValueOrDefault(2048);
        if (blockSize is < 512 or > 4096 || !IsPowerOfTwo(blockSize))
            return;

        var anchorRelativeOffset = checked(256L * blockSize);
        if (anchorRelativeOffset > detection.RegionSizeBytes - blockSize)
        {
            findings.Add(new ImageHealthFinding(
                "UDF_PRIMARY_ANCHOR_UNAVAILABLE",
                ImageHealthSeverity.Warning,
                "UDF was recognized from the Volume Recognition Sequence, but the primary anchor at logical block 256 is outside the bounded region.",
                "UDF anchor",
                detection.PartitionIndex));
            return;
        }

        var anchor = await ReadBoundedAsync(
            stream,
            checked(detection.PhysicalOffsetBytes + anchorRelativeOffset),
            blockSize,
            detection,
            cancellationToken);

        if (!TryValidateUdfDescriptorTag(
                anchor,
                UdfAnchorVolumeDescriptorPointerTagId,
                256,
                out var anchorError))
        {
            findings.Add(new ImageHealthFinding(
                "UDF_PRIMARY_ANCHOR_INVALID",
                ImageHealthSeverity.Error,
                "The UDF primary anchor is invalid: " + anchorError,
                "UDF anchor",
                detection.PartitionIndex));
            return;
        }

        var sequenceLength = BinaryPrimitives.ReadUInt32LittleEndian(anchor.AsSpan(16, 4));
        var sequenceLocation = BinaryPrimitives.ReadUInt32LittleEndian(anchor.AsSpan(20, 4));
        if (sequenceLength < blockSize || sequenceLocation == 0)
        {
            findings.Add(new ImageHealthFinding(
                "UDF_MAIN_DESCRIPTOR_SEQUENCE_INVALID",
                ImageHealthSeverity.Error,
                "The UDF primary anchor declares an empty or invalid main volume-descriptor sequence.",
                "UDF anchor",
                detection.PartitionIndex));
            return;
        }

        if (sequenceLength > MaxUdfDescriptorSequenceBytes)
        {
            findings.Add(new ImageHealthFinding(
                "UDF_MAIN_DESCRIPTOR_SEQUENCE_TOO_LARGE",
                ImageHealthSeverity.Warning,
                $"The UDF main volume-descriptor sequence ({sequenceLength} bytes) exceeds the bounded {MaxUdfDescriptorSequenceBytes}-byte inspection limit.",
                "UDF anchor",
                detection.PartitionIndex));
            return;
        }

        long sequenceRelativeOffset;
        try
        {
            sequenceRelativeOffset = checked((long)sequenceLocation * blockSize);
        }
        catch (OverflowException)
        {
            findings.Add(new ImageHealthFinding(
                "UDF_MAIN_DESCRIPTOR_SEQUENCE_OVERFLOW",
                ImageHealthSeverity.Error,
                "The UDF main volume-descriptor sequence offset overflows the supported range.",
                "UDF anchor",
                detection.PartitionIndex));
            return;
        }

        if (sequenceRelativeOffset < 0
            || sequenceRelativeOffset > detection.RegionSizeBytes
            || sequenceLength > detection.RegionSizeBytes - sequenceRelativeOffset)
        {
            findings.Add(new ImageHealthFinding(
                "UDF_MAIN_DESCRIPTOR_SEQUENCE_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The UDF main volume-descriptor sequence lies outside the bounded filesystem region.",
                "UDF anchor",
                detection.PartitionIndex));
            return;
        }

        var sequence = await ReadBoundedAsync(
            stream,
            checked(detection.PhysicalOffsetBytes + sequenceRelativeOffset),
            checked((int)sequenceLength),
            detection,
            cancellationToken);

        var sawPrimary = false;
        var sawLogical = false;
        var descriptorCount = sequence.Length / blockSize;
        for (var index = 0; index < descriptorCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = sequence.AsSpan(index * blockSize, blockSize);
            if (descriptor.IndexOfAnyExcept((byte)0) < 0)
                continue;

            var tagId = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[..2]);
            var expectedLocation = checked(sequenceLocation + (uint)index);
            if (!TryValidateUdfDescriptorTag(descriptor, tagId, expectedLocation, out var tagError))
            {
                findings.Add(new ImageHealthFinding(
                    "UDF_DESCRIPTOR_TAG_INVALID",
                    ImageHealthSeverity.Error,
                    $"UDF descriptor tag {tagId} at logical block {expectedLocation} is invalid: {tagError}",
                    "UDF volume descriptor sequence",
                    detection.PartitionIndex));
                continue;
            }

            if (tagId == UdfTerminatingDescriptorTagId)
                break;

            if (tagId == UdfPrimaryVolumeDescriptorTagId)
            {
                sawPrimary = true;
                if (descriptor.Length >= 56)
                {
                    var volumeId = ReadOstaDString(descriptor.Slice(24, 32));
                    if (!string.IsNullOrWhiteSpace(volumeId))
                    {
                        identity.Add(new ImageIdentityEvidence(
                            "udf-volume-id",
                            volumeId,
                            "UDF primary volume descriptor",
                            detection.PartitionIndex));
                    }
                }
            }
            else if (tagId == UdfLogicalVolumeDescriptorTagId)
            {
                sawLogical = true;
                if (descriptor.Length >= 440)
                {
                    var label = ReadOstaDString(descriptor.Slice(84, 128));
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        identity.Add(new ImageIdentityEvidence(
                            "filesystem-label",
                            label,
                            "UDF logical volume descriptor",
                            detection.PartitionIndex));
                    }

                    var logicalBlockSize = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(212, 4));
                    if (logicalBlockSize is < 512 or > 4096 || !IsPowerOfTwo(logicalBlockSize))
                    {
                        findings.Add(new ImageHealthFinding(
                            "UDF_LOGICAL_BLOCK_SIZE_INVALID",
                            ImageHealthSeverity.Error,
                            $"The UDF logical volume descriptor declares unsupported logical block size {logicalBlockSize}.",
                            "UDF logical volume descriptor",
                            detection.PartitionIndex));
                    }

                    var domainIdentifier = Encoding.ASCII
                        .GetString(descriptor.Slice(217, 23))
                        .TrimEnd('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(domainIdentifier)
                        && !domainIdentifier.Equals("*OSTA UDF Compliant", StringComparison.Ordinal))
                    {
                        findings.Add(new ImageHealthFinding(
                            "UDF_DOMAIN_IDENTIFIER_UNEXPECTED",
                            ImageHealthSeverity.Warning,
                            $"The UDF logical volume descriptor uses unexpected domain identifier '{domainIdentifier}'.",
                            "UDF logical volume descriptor",
                            detection.PartitionIndex));
                    }
                }
            }
        }

        if (!sawPrimary)
        {
            findings.Add(new ImageHealthFinding(
                "UDF_PRIMARY_VOLUME_DESCRIPTOR_MISSING",
                ImageHealthSeverity.Warning,
                "No validated Primary Volume Descriptor was found in the bounded UDF main descriptor sequence.",
                "UDF volume descriptor sequence",
                detection.PartitionIndex));
        }

        if (!sawLogical)
        {
            findings.Add(new ImageHealthFinding(
                "UDF_LOGICAL_VOLUME_DESCRIPTOR_MISSING",
                ImageHealthSeverity.Warning,
                "No validated Logical Volume Descriptor was found in the bounded UDF main descriptor sequence.",
                "UDF volume descriptor sequence",
                detection.PartitionIndex));
        }
    }

    private static bool TryValidateUdfDescriptorTag(
        ReadOnlySpan<byte> descriptor,
        ushort expectedTagId,
        uint expectedLocation,
        out string error)
    {
        if (descriptor.Length < 16)
        {
            error = "descriptor is shorter than the 16-byte tag";
            return false;
        }

        var tagId = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[..2]);
        if (tagId != expectedTagId)
        {
            error = $"expected tag id {expectedTagId}, found {tagId}";
            return false;
        }

        byte checksum = 0;
        for (var index = 0; index < 16; index++)
        {
            if (index != 4)
                checksum = unchecked((byte)(checksum + descriptor[index]));
        }

        if (checksum != descriptor[4])
        {
            error = "descriptor-tag checksum does not match";
            return false;
        }

        var tagLocation = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(12, 4));
        if (tagLocation != expectedLocation)
        {
            error = $"tag location {tagLocation} does not match expected logical block {expectedLocation}";
            return false;
        }

        var crcLength = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(10, 2));
        if (crcLength > descriptor.Length - 16)
        {
            error = "descriptor CRC length exceeds the bounded descriptor block";
            return false;
        }

        if (crcLength > 0)
        {
            var expectedCrc = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(8, 2));
            var actualCrc = ComputeCrc16(descriptor.Slice(16, crcLength));
            if (expectedCrc != actualCrc)
            {
                error = "descriptor CRC does not match";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static uint ComputeExFatBootChecksum(ReadOnlySpan<byte> bootSectors)
    {
        uint checksum = 0;
        for (var index = 0; index < bootSectors.Length; index++)
        {
            if (index is 106 or 107 or 112)
                continue;

            checksum = unchecked(((checksum << 31) | (checksum >> 1)) + bootSectors[index]);
        }

        return checksum;
    }

    private static bool ChecksumSectorMatches(ReadOnlySpan<byte> sector, uint checksum)
    {
        if (sector.Length == 0 || sector.Length % sizeof(uint) != 0)
            return false;

        for (var offset = 0; offset < sector.Length; offset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(sector.Slice(offset, sizeof(uint))) != checksum)
                return false;
        }

        return true;
    }

    private static bool ExFatBootCopiesEquivalent(ReadOnlySpan<byte> main, ReadOnlySpan<byte> backup)
    {
        if (main.Length != backup.Length)
            return false;

        for (var index = 0; index < main.Length; index++)
        {
            if (index is 106 or 107 or 112)
                continue;
            if (main[index] != backup[index])
                return false;
        }

        return true;
    }

    private static string ReadOstaDString(ReadOnlySpan<byte> field)
    {
        if (field.Length < 2)
            return string.Empty;

        var recordedLength = field[^1];
        if (recordedLength == 0 || recordedLength > field.Length - 1)
            return string.Empty;

        var payload = field[..recordedLength];
        if (payload.Length < 2)
            return string.Empty;

        string value;
        if (payload[0] == 8)
        {
            value = Encoding.Latin1.GetString(payload[1..]);
        }
        else if (payload[0] == 16)
        {
            if ((payload.Length - 1) % 2 != 0)
                return string.Empty;

            var builder = new StringBuilder((payload.Length - 1) / 2);
            for (var index = 1; index + 1 < payload.Length; index += 2)
            {
                var codePoint = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(index, 2));
                if (codePoint != 0)
                    builder.Append((char)codePoint);
            }
            value = builder.ToString();
        }
        else
        {
            return string.Empty;
        }

        return value.TrimEnd('\0', ' ');
    }

    private static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var value in data)
        {
            crc ^= (ushort)(value << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return crc;
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
            throw new InvalidDataException("Filesystem depth read begins before the bounded filesystem region.");

        var relative = checked(absoluteOffset - detection.PhysicalOffsetBytes);
        if (relative > detection.RegionSizeBytes || length > detection.RegionSizeBytes - relative)
            throw new InvalidDataException("Filesystem depth read exceeds the bounded filesystem region.");
        if (absoluteOffset > stream.Length || length > stream.Length - absoluteOffset)
            throw new InvalidDataException("Filesystem depth read exceeds the physical image.");

        var buffer = new byte[length];
        stream.Position = absoluteOffset;
        await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken);
        return buffer;
    }

    private static void ValidateDetectionRegion(FileSystemDetectionInfo detection, long imageLength)
    {
        if (detection.PhysicalOffsetBytes < 0 || detection.RegionSizeBytes < 0)
            throw new InvalidDataException("Filesystem detection contains a negative physical region.");
        if (detection.PhysicalOffsetBytes > imageLength
            || detection.RegionSizeBytes > imageLength - detection.PhysicalOffsetBytes)
        {
            throw new InvalidDataException("Filesystem detection exceeds the physical image.");
        }
    }

    private static bool IsPowerOfTwo(int value)
        => value > 0 && (value & (value - 1)) == 0;

    private static bool IsPowerOfTwo(uint value)
        => value > 0 && (value & (value - 1)) == 0;
}
