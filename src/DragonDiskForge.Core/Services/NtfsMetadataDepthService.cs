using System.Buffers.Binary;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Performs bounded read-only NTFS metadata validation beyond boot-sector recognition.
/// Reads are restricted to filesystem regions already accepted by FileSystemRecognitionService.
/// The service never repairs metadata, follows attributes or traverses directories.
/// </summary>
public sealed class NtfsMetadataDepthService
{
    private const int MinimumRecordBytes = 512;
    private const int MaximumRecordBytes = 1024 * 1024;

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

        var findings = new List<ImageHealthFinding>();
        foreach (var detection in recognition.Detections.Where(x => x.Kind == FileSystemKind.Ntfs))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDetectionRegion(detection, stream.Length);
            await AnalyzeNtfsAsync(stream, detection, findings, cancellationToken);
        }

        var normalized = findings
            .GroupBy(
                x => $"{x.Code}\u001f{x.PartitionIndex?.ToString() ?? string.Empty}\u001f{x.Message}",
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.PartitionIndex ?? int.MinValue)
            .ToArray();

        return new FileSystemDepthInfo(
            Array.Empty<ImageIdentityEvidence>(),
            Array.AsReadOnly(normalized));
    }

    private static async Task AnalyzeNtfsAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        List<ImageHealthFinding> findings,
        CancellationToken cancellationToken)
    {
        const int bootProbeBytes = 512;
        if (detection.RegionSizeBytes < bootProbeBytes)
            return;

        var boot = await ReadBoundedAsync(
            stream,
            detection.PhysicalOffsetBytes,
            bootProbeBytes,
            detection,
            cancellationToken);

        if (!boot.AsSpan(3, 8).SequenceEqual("NTFS    "u8))
            return;

        var bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11, 2));
        var sectorsPerCluster = boot[13];
        if (bytesPerSector is < 512 or > 4096
            || !IsPowerOfTwo(bytesPerSector)
            || sectorsPerCluster == 0
            || sectorsPerCluster > 128
            || !IsPowerOfTwo(sectorsPerCluster))
        {
            findings.Add(Finding(
                "NTFS_CLUSTER_GEOMETRY_INVALID",
                ImageHealthSeverity.Error,
                "The NTFS boot sector declares unsupported sector/cluster geometry.",
                detection));
            return;
        }

        int clusterSize;
        try
        {
            clusterSize = checked(bytesPerSector * sectorsPerCluster);
        }
        catch (OverflowException)
        {
            findings.Add(Finding(
                "NTFS_CLUSTER_GEOMETRY_OVERFLOW",
                ImageHealthSeverity.Error,
                "The NTFS cluster size overflows the supported range.",
                detection));
            return;
        }

        var totalSectors = BinaryPrimitives.ReadUInt64LittleEndian(boot.AsSpan(40, 8));
        var mftCluster = BinaryPrimitives.ReadUInt64LittleEndian(boot.AsSpan(48, 8));
        var mftMirrorCluster = BinaryPrimitives.ReadUInt64LittleEndian(boot.AsSpan(56, 8));
        if (totalSectors == 0)
            return;

        ulong volumeBytes;
        try
        {
            volumeBytes = checked(totalSectors * bytesPerSector);
        }
        catch (OverflowException)
        {
            findings.Add(Finding(
                "NTFS_VOLUME_GEOMETRY_OVERFLOW",
                ImageHealthSeverity.Error,
                "The NTFS volume geometry overflows the supported range.",
                detection));
            return;
        }

        if (volumeBytes > (ulong)detection.RegionSizeBytes)
        {
            findings.Add(Finding(
                "NTFS_VOLUME_GEOMETRY_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The NTFS boot sector declares a volume larger than the bounded filesystem region.",
                detection));
            return;
        }

        var totalClusters = totalSectors / sectorsPerCluster;
        if (totalClusters == 0)
            return;

        if (mftCluster >= totalClusters)
        {
            findings.Add(Finding(
                "NTFS_MFT_CLUSTER_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                $"The NTFS $MFT start cluster ({mftCluster}) lies outside the bounded cluster range.",
                detection));
            return;
        }

        if (mftMirrorCluster >= totalClusters)
        {
            findings.Add(Finding(
                "NTFS_MFTMIRR_CLUSTER_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                $"The NTFS $MFTMirr start cluster ({mftMirrorCluster}) lies outside the bounded cluster range.",
                detection));
            return;
        }

        if (!TryDecodeNtfsUnitSize(unchecked((sbyte)boot[64]), clusterSize, out var fileRecordSize))
        {
            findings.Add(Finding(
                "NTFS_FILE_RECORD_SIZE_INVALID",
                ImageHealthSeverity.Error,
                "The NTFS clusters-per-file-record field does not describe a supported bounded FILE record size.",
                detection));
            return;
        }

        if (!TryDecodeNtfsUnitSize(unchecked((sbyte)boot[68]), clusterSize, out _))
        {
            findings.Add(Finding(
                "NTFS_INDEX_BUFFER_SIZE_INVALID",
                ImageHealthSeverity.Warning,
                "The NTFS clusters-per-index-buffer field does not describe a supported bounded index-buffer size.",
                detection));
        }

        if (!TryComputeRecordOffset(mftCluster, clusterSize, fileRecordSize, detection.RegionSizeBytes, out var mftRelative))
        {
            findings.Add(Finding(
                "NTFS_MFT_RECORD_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The first NTFS $MFT FILE record lies outside the bounded filesystem region.",
                detection));
            return;
        }

        if (!TryComputeRecordOffset(mftMirrorCluster, clusterSize, fileRecordSize, detection.RegionSizeBytes, out var mirrorRelative))
        {
            findings.Add(Finding(
                "NTFS_MFTMIRR_RECORD_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The first NTFS $MFTMirr FILE record lies outside the bounded filesystem region.",
                detection));
            return;
        }

        var mftRecord = await ReadBoundedAsync(
            stream,
            checked(detection.PhysicalOffsetBytes + mftRelative),
            fileRecordSize,
            detection,
            cancellationToken);
        var mirrorRecord = await ReadBoundedAsync(
            stream,
            checked(detection.PhysicalOffsetBytes + mirrorRelative),
            fileRecordSize,
            detection,
            cancellationToken);

        var mftValid = TryValidateAndRestoreFileRecord(mftRecord, bytesPerSector, out var normalizedMft, out var mftError);
        if (!mftValid)
        {
            findings.Add(Finding(
                "NTFS_MFT_RECORD_INVALID",
                ImageHealthSeverity.Error,
                "The first NTFS $MFT FILE record is invalid: " + mftError,
                detection));
        }

        var mirrorValid = TryValidateAndRestoreFileRecord(mirrorRecord, bytesPerSector, out var normalizedMirror, out var mirrorError);
        if (!mirrorValid)
        {
            findings.Add(Finding(
                "NTFS_MFTMIRR_RECORD_INVALID",
                ImageHealthSeverity.Error,
                "The first NTFS $MFTMirr FILE record is invalid: " + mirrorError,
                detection));
        }

        if (mftValid && mirrorValid && !normalizedMft.AsSpan().SequenceEqual(normalizedMirror))
        {
            findings.Add(Finding(
                "NTFS_MFT_MIRROR_RECORD_MISMATCH",
                ImageHealthSeverity.Warning,
                "The first validated $MFT and $MFTMirr FILE records differ after update-sequence fixup normalization.",
                detection));
        }
    }

    private static bool TryDecodeNtfsUnitSize(sbyte encoded, int clusterSize, out int size)
    {
        size = 0;
        if (encoded == 0)
            return false;

        try
        {
            if (encoded > 0)
            {
                size = checked(encoded * clusterSize);
            }
            else
            {
                var exponent = -encoded;
                if (exponent is < 9 or > 20)
                    return false;
                size = 1 << exponent;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        return size is >= MinimumRecordBytes and <= MaximumRecordBytes
            && IsPowerOfTwo(size);
    }

    private static bool TryComputeRecordOffset(
        ulong cluster,
        int clusterSize,
        int recordSize,
        long regionSize,
        out long relativeOffset)
    {
        relativeOffset = 0;
        ulong rawOffset;
        try
        {
            rawOffset = checked(cluster * (ulong)clusterSize);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (rawOffset > long.MaxValue)
            return false;

        relativeOffset = (long)rawOffset;
        return relativeOffset >= 0
            && relativeOffset <= regionSize
            && recordSize <= regionSize - relativeOffset;
    }

    private static bool TryValidateAndRestoreFileRecord(
        ReadOnlySpan<byte> record,
        int bytesPerSector,
        out byte[] normalized,
        out string error)
    {
        normalized = Array.Empty<byte>();
        if (record.Length < 48 || record.Length % bytesPerSector != 0)
        {
            error = "record length is not aligned to the NTFS sector size";
            return false;
        }

        if (!record[..4].SequenceEqual("FILE"u8))
        {
            error = "FILE signature is missing";
            return false;
        }

        var usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(4, 2));
        var usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(6, 2));
        var expectedUsaCount = (record.Length / bytesPerSector) + 1;
        if (usaCount != expectedUsaCount
            || usaOffset < 8
            || usaOffset > record.Length
            || checked((int)usaCount * 2) > record.Length - usaOffset)
        {
            error = "update-sequence array geometry is invalid";
            return false;
        }

        var firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(20, 2));
        var bytesInUse = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(24, 4));
        var bytesAllocated = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(28, 4));
        var usaEnd = checked(usaOffset + (usaCount * 2));
        if (bytesInUse < 48
            || bytesInUse > record.Length
            || bytesAllocated < bytesInUse
            || bytesAllocated > record.Length
            || firstAttributeOffset < usaEnd
            || firstAttributeOffset >= bytesInUse)
        {
            error = "FILE record header sizes are inconsistent with the bounded record";
            return false;
        }

        normalized = record.ToArray();
        var usn = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(usaOffset, 2));
        for (var sector = 1; sector < usaCount; sector++)
        {
            var trailerOffset = checked((sector * bytesPerSector) - 2);
            if (BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(trailerOffset, 2)) != usn)
            {
                normalized = Array.Empty<byte>();
                error = $"sector {sector} update-sequence trailer does not match the record USN";
                return false;
            }

            var replacementOffset = checked(usaOffset + (sector * 2));
            var replacement = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(replacementOffset, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(normalized.AsSpan(trailerOffset, 2), replacement);
        }

        normalized.AsSpan(usaOffset, usaCount * 2).Clear();
        error = string.Empty;
        return true;
    }

    private static ImageHealthFinding Finding(
        string code,
        ImageHealthSeverity severity,
        string message,
        FileSystemDetectionInfo detection)
        => new(code, severity, message, "NTFS bounded metadata", detection.PartitionIndex);

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
            throw new InvalidDataException("NTFS metadata read begins before the bounded filesystem region.");

        var relative = checked(absoluteOffset - detection.PhysicalOffsetBytes);
        if (relative > detection.RegionSizeBytes || length > detection.RegionSizeBytes - relative)
            throw new InvalidDataException("NTFS metadata read exceeds the bounded filesystem region.");
        if (absoluteOffset > stream.Length || length > stream.Length - absoluteOffset)
            throw new InvalidDataException("NTFS metadata read exceeds the physical image.");

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

    private static bool IsPowerOfTwo(ushort value)
        => value > 0 && (value & (value - 1)) == 0;
}
