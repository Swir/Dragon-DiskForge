using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Performs a deliberately restricted UDF root-directory traversal.
/// Only validated Type 1 partition maps that translate directly to bounded physical
/// image bytes are accepted. Virtual, sparable and metadata partition maps are not
/// followed. The service never writes, repairs, extracts or recursively walks trees.
/// </summary>
public sealed class UdfTraversalService
{
    private const int MaxVolumeDescriptorSequenceBytes = 16 * 1024 * 1024;
    private const int MaxRootDirectoryBytes = 8 * 1024 * 1024;
    private const int MaxRootEntries = 4096;

    private const ushort AnchorTagId = 2;
    private const ushort PartitionDescriptorTagId = 5;
    private const ushort LogicalVolumeDescriptorTagId = 6;
    private const ushort TerminatingDescriptorTagId = 8;
    private const ushort FileSetDescriptorTagId = 256;
    private const ushort FileIdentifierDescriptorTagId = 257;
    private const ushort FileEntryTagId = 261;

    public async Task<UdfTraversalInfo> AnalyzeAsync(
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
        var names = new List<string>();
        var traversed = false;

        foreach (var detection in recognition.Detections.Where(x => x.Kind == FileSystemKind.Udf))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDetectionRegion(detection, stream.Length);

            var result = await AnalyzeDetectionAsync(stream, detection, cancellationToken);
            traversed |= result.Traversed;
            identity.AddRange(result.Identity);
            health.AddRange(result.HealthFindings);
            names.AddRange(result.RootEntryNames);
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

        var normalizedNames = names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        return new UdfTraversalInfo(
            traversed,
            normalizedNames.Length,
            Array.AsReadOnly(normalizedNames),
            Array.AsReadOnly(normalizedIdentity),
            Array.AsReadOnly(normalizedHealth));
    }

    private static async Task<UdfTraversalInfo> AnalyzeDetectionAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        CancellationToken cancellationToken)
    {
        var identity = new List<ImageIdentityEvidence>();
        var health = new List<ImageHealthFinding>();
        var names = new List<string>();

        var blockSize = detection.LogicalBlockSize.GetValueOrDefault(2048);
        if (blockSize is < 512 or > 4096 || !IsPowerOfTwo(blockSize))
            return Empty();

        var anchorOffset = checked(256L * blockSize);
        if (!RangeFits(anchorOffset, blockSize, detection.RegionSizeBytes))
            return Empty();

        var anchor = await ReadRelativeAsync(stream, detection, anchorOffset, blockSize, cancellationToken);
        if (!TryValidateDescriptorTag(anchor, AnchorTagId, 256, out _))
            return Empty();

        var sequenceLength = BinaryPrimitives.ReadUInt32LittleEndian(anchor.AsSpan(16, 4));
        var sequenceLocation = BinaryPrimitives.ReadUInt32LittleEndian(anchor.AsSpan(20, 4));
        if (sequenceLength < blockSize
            || sequenceLength > MaxVolumeDescriptorSequenceBytes
            || sequenceLocation == 0)
        {
            return Empty();
        }

        long sequenceOffset;
        try
        {
            sequenceOffset = checked((long)sequenceLocation * blockSize);
        }
        catch (OverflowException)
        {
            return Empty();
        }

        if (!RangeFits(sequenceOffset, sequenceLength, detection.RegionSizeBytes))
            return Empty();

        var sequence = await ReadRelativeAsync(
            stream,
            detection,
            sequenceOffset,
            checked((int)sequenceLength),
            cancellationToken);

        var partitionDescriptors = new Dictionary<ushort, PartitionDescriptorInfo>();
        LogicalVolumeInfo? logicalVolume = null;
        var descriptorCount = sequence.Length / blockSize;

        for (var index = 0; index < descriptorCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = sequence.AsSpan(index * blockSize, blockSize);
            if (descriptor.IndexOfAnyExcept((byte)0) < 0)
                continue;

            var tagId = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[..2]);
            var location = checked(sequenceLocation + (uint)index);
            if (!TryValidateDescriptorTag(descriptor, tagId, location, out _))
                continue;

            if (tagId == TerminatingDescriptorTagId)
                break;

            if (tagId == PartitionDescriptorTagId && descriptor.Length >= 196)
            {
                var partitionNumber = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(22, 2));
                var start = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(188, 4));
                var length = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(192, 4));
                if (length > 0)
                    partitionDescriptors[partitionNumber] = new PartitionDescriptorInfo(partitionNumber, start, length);
            }
            else if (tagId == LogicalVolumeDescriptorTagId && descriptor.Length >= 440)
            {
                logicalVolume = TryParseLogicalVolumeDescriptor(descriptor);
            }
        }

        if (logicalVolume is null
            || logicalVolume.FileSetDescriptor.LengthBytes == 0
            || logicalVolume.Type1Maps.Count == 0)
        {
            // Existing UDF metadata inspection remains valid even when this deliberately
            // narrower traversal path cannot prove a physical Type 1 mapping.
            return Empty();
        }

        if (!logicalVolume.Type1Maps.TryGetValue(
                logicalVolume.FileSetDescriptor.PartitionReferenceNumber,
                out var fileSetPartitionNumber))
        {
            return Empty();
        }

        if (!partitionDescriptors.TryGetValue(fileSetPartitionNumber, out var fileSetPartition))
        {
            health.Add(Finding(
                "UDF_TRAVERSAL_PARTITION_DESCRIPTOR_MISSING",
                ImageHealthSeverity.Error,
                $"The UDF Type 1 partition map references partition {fileSetPartitionNumber}, but no validated Partition Descriptor describes it.",
                detection));
            return Result(false, names, identity, health);
        }

        if (!TryMapLongAd(
                logicalVolume.FileSetDescriptor,
                fileSetPartition,
                blockSize,
                detection.RegionSizeBytes,
                minimumLength: 512,
                out var fileSetOffset,
                out var fileSetError))
        {
            health.Add(Finding(
                "UDF_FILE_SET_DESCRIPTOR_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                fileSetError,
                detection));
            return Result(false, names, identity, health);
        }

        var fileSetBlock = await ReadRelativeAsync(
            stream,
            detection,
            fileSetOffset,
            blockSize,
            cancellationToken);

        if (!TryValidateDescriptorTag(
                fileSetBlock,
                FileSetDescriptorTagId,
                logicalVolume.FileSetDescriptor.LogicalBlockNumber,
                out var fileSetTagError))
        {
            health.Add(Finding(
                "UDF_FILE_SET_DESCRIPTOR_INVALID",
                ImageHealthSeverity.Error,
                "The bounded UDF File Set Descriptor is invalid: " + fileSetTagError,
                detection));
            return Result(false, names, identity, health);
        }

        if (fileSetBlock.Length < 416)
            return Result(false, names, identity, health);

        var fileSetId = ReadOstaDString(fileSetBlock.AsSpan(304, 32));
        if (!string.IsNullOrWhiteSpace(fileSetId))
        {
            identity.Add(new ImageIdentityEvidence(
                "udf-file-set-id",
                fileSetId,
                "UDF File Set Descriptor",
                detection.PartitionIndex));
        }

        var rootIcb = ParseLongAd(fileSetBlock.AsSpan(400, 16));
        if (rootIcb.LengthBytes == 0)
        {
            health.Add(Finding(
                "UDF_ROOT_DIRECTORY_ICB_MISSING",
                ImageHealthSeverity.Warning,
                "The validated UDF File Set Descriptor does not specify a root-directory ICB.",
                detection));
            return Result(false, names, identity, health);
        }

        if (!logicalVolume.Type1Maps.TryGetValue(rootIcb.PartitionReferenceNumber, out var rootPartitionNumber)
            || !partitionDescriptors.TryGetValue(rootPartitionNumber, out var rootPartition))
        {
            health.Add(Finding(
                "UDF_ROOT_DIRECTORY_PARTITION_UNSUPPORTED",
                ImageHealthSeverity.Warning,
                "The UDF root-directory ICB does not resolve through a validated physical Type 1 partition map.",
                detection));
            return Result(false, names, identity, health);
        }

        if (!TryMapLongAd(
                rootIcb,
                rootPartition,
                blockSize,
                detection.RegionSizeBytes,
                minimumLength: 176,
                out var rootEntryOffset,
                out var rootMapError))
        {
            health.Add(Finding(
                "UDF_ROOT_DIRECTORY_ICB_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                rootMapError,
                detection));
            return Result(false, names, identity, health);
        }

        var rootEntry = await ReadRelativeAsync(
            stream,
            detection,
            rootEntryOffset,
            blockSize,
            cancellationToken);

        if (!TryValidateDescriptorTag(
                rootEntry,
                FileEntryTagId,
                rootIcb.LogicalBlockNumber,
                out var rootTagError))
        {
            health.Add(Finding(
                "UDF_ROOT_FILE_ENTRY_INVALID",
                ImageHealthSeverity.Error,
                "The UDF root File Entry is invalid: " + rootTagError,
                detection));
            return Result(false, names, identity, health);
        }

        var strategyType = BinaryPrimitives.ReadUInt16LittleEndian(rootEntry.AsSpan(20, 2));
        var fileType = rootEntry[27];
        var icbFlags = BinaryPrimitives.ReadUInt16LittleEndian(rootEntry.AsSpan(34, 2));
        var allocationType = icbFlags & 0x0007;
        if (strategyType is not 4 and not 4096)
        {
            health.Add(Finding(
                "UDF_ROOT_ICB_STRATEGY_UNSUPPORTED",
                ImageHealthSeverity.Warning,
                $"The UDF root File Entry uses unsupported ICB strategy {strategyType}.",
                detection));
            return Result(false, names, identity, health);
        }

        if (fileType != 4)
        {
            health.Add(Finding(
                "UDF_ROOT_FILE_TYPE_INVALID",
                ImageHealthSeverity.Error,
                $"The UDF root File Entry declares file type {fileType} instead of directory type 4.",
                detection));
            return Result(false, names, identity, health);
        }

        var informationLength = BinaryPrimitives.ReadUInt64LittleEndian(rootEntry.AsSpan(56, 8));
        if (informationLength > MaxRootDirectoryBytes)
        {
            health.Add(Finding(
                "UDF_ROOT_DIRECTORY_TOO_LARGE",
                ImageHealthSeverity.Warning,
                $"The UDF root directory declares {informationLength} bytes, above the bounded {MaxRootDirectoryBytes}-byte traversal limit.",
                detection));
            return Result(false, names, identity, health);
        }

        var extendedAttributesLength = BinaryPrimitives.ReadUInt32LittleEndian(rootEntry.AsSpan(168, 4));
        var allocationDescriptorsLength = BinaryPrimitives.ReadUInt32LittleEndian(rootEntry.AsSpan(172, 4));
        long allocationStart;
        try
        {
            allocationStart = checked(176L + extendedAttributesLength);
        }
        catch (OverflowException)
        {
            health.Add(Finding(
                "UDF_ROOT_ALLOCATION_DESCRIPTOR_OVERFLOW",
                ImageHealthSeverity.Error,
                "The UDF root File Entry allocation-descriptor offset overflows the supported range.",
                detection));
            return Result(false, names, identity, health);
        }

        if (allocationStart > rootEntry.Length
            || allocationDescriptorsLength > rootEntry.Length - allocationStart)
        {
            health.Add(Finding(
                "UDF_ROOT_ALLOCATION_DESCRIPTOR_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                "The UDF root File Entry allocation descriptors exceed the bounded File Entry block.",
                detection));
            return Result(false, names, identity, health);
        }

        if (informationLength == 0)
            return Result(true, names, identity, health);

        if (allocationType != 0)
        {
            health.Add(Finding(
                "UDF_ROOT_ALLOCATION_TYPE_UNSUPPORTED",
                ImageHealthSeverity.Warning,
                $"The bounded root traversal currently accepts only short allocation descriptors; ICB allocation type {allocationType} is not followed.",
                detection));
            return Result(false, names, identity, health);
        }

        if (allocationDescriptorsLength != 8)
        {
            health.Add(Finding(
                "UDF_ROOT_ALLOCATION_LAYOUT_UNSUPPORTED",
                ImageHealthSeverity.Warning,
                "The bounded root traversal currently accepts exactly one short allocation descriptor for the root directory.",
                detection));
            return Result(false, names, identity, health);
        }

        var shortAd = rootEntry.AsSpan(checked((int)allocationStart), 8);
        var rawExtentLength = BinaryPrimitives.ReadUInt32LittleEndian(shortAd[..4]);
        var extentType = rawExtentLength >> 30;
        var extentLength = rawExtentLength & 0x3FFF_FFFF;
        var extentPosition = BinaryPrimitives.ReadUInt32LittleEndian(shortAd.Slice(4, 4));
        if (extentType != 0 || extentLength == 0)
        {
            health.Add(Finding(
                "UDF_ROOT_DIRECTORY_EXTENT_UNRECORDED",
                ImageHealthSeverity.Warning,
                "The UDF root directory does not use one recorded short allocation extent.",
                detection));
            return Result(false, names, identity, health);
        }

        if (extentLength > MaxRootDirectoryBytes || informationLength > extentLength)
        {
            health.Add(Finding(
                "UDF_ROOT_DIRECTORY_LENGTH_INVALID",
                ImageHealthSeverity.Error,
                "The UDF root directory information length exceeds its bounded recorded extent.",
                detection));
            return Result(false, names, identity, health);
        }

        if (!TryMapPartitionExtent(
                extentPosition,
                extentLength,
                rootPartition,
                blockSize,
                detection.RegionSizeBytes,
                out var directoryOffset,
                out var directoryError))
        {
            health.Add(Finding(
                "UDF_ROOT_DIRECTORY_EXTENT_OUT_OF_RANGE",
                ImageHealthSeverity.Error,
                directoryError,
                detection));
            return Result(false, names, identity, health);
        }

        var directoryBytes = await ReadRelativeAsync(
            stream,
            detection,
            directoryOffset,
            checked((int)extentLength),
            cancellationToken);

        var usableLength = checked((int)informationLength);
        var cursor = 0;
        var entryCount = 0;
        while (cursor < usableLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entryCount >= MaxRootEntries)
            {
                health.Add(Finding(
                    "UDF_ROOT_DIRECTORY_ENTRY_LIMIT",
                    ImageHealthSeverity.Warning,
                    $"Root-directory traversal stopped after the bounded {MaxRootEntries}-entry limit.",
                    detection));
                return Result(true, names, identity, health);
            }

            if (usableLength - cursor < 38)
            {
                health.Add(Finding(
                    "UDF_FILE_IDENTIFIER_TRUNCATED",
                    ImageHealthSeverity.Error,
                    "A root-directory File Identifier Descriptor is shorter than its fixed 38-byte header.",
                    detection));
                return Result(true, names, identity, health);
            }

            var nameLength = directoryBytes[cursor + 19];
            var implementationUseLength = BinaryPrimitives.ReadUInt16LittleEndian(
                directoryBytes.AsSpan(cursor + 36, 2));
            if ((implementationUseLength & 3) != 0)
            {
                health.Add(Finding(
                    "UDF_FILE_IDENTIFIER_IMPLEMENTATION_USE_INVALID",
                    ImageHealthSeverity.Error,
                    "A root-directory File Identifier Descriptor has an implementation-use length that is not 4-byte aligned.",
                    detection));
                return Result(true, names, identity, health);
            }

            int descriptorLength;
            int paddedLength;
            try
            {
                descriptorLength = checked(38 + implementationUseLength + nameLength);
                paddedLength = checked((descriptorLength + 3) & ~3);
            }
            catch (OverflowException)
            {
                health.Add(Finding(
                    "UDF_FILE_IDENTIFIER_LENGTH_OVERFLOW",
                    ImageHealthSeverity.Error,
                    "A root-directory File Identifier Descriptor length overflows the supported range.",
                    detection));
                return Result(true, names, identity, health);
            }

            if (paddedLength <= 0 || paddedLength > usableLength - cursor)
            {
                health.Add(Finding(
                    "UDF_FILE_IDENTIFIER_OUT_OF_RANGE",
                    ImageHealthSeverity.Error,
                    "A root-directory File Identifier Descriptor exceeds the bounded root-directory extent.",
                    detection));
                return Result(true, names, identity, health);
            }

            var tagBlockLocation = checked(extentPosition + (uint)(cursor / blockSize));
            var descriptor = directoryBytes.AsSpan(cursor, paddedLength);
            if (!TryValidateDescriptorTag(
                    descriptor,
                    FileIdentifierDescriptorTagId,
                    tagBlockLocation,
                    out var fidError))
            {
                health.Add(Finding(
                    "UDF_FILE_IDENTIFIER_INVALID",
                    ImageHealthSeverity.Error,
                    "A root-directory File Identifier Descriptor is invalid: " + fidError,
                    detection));
                return Result(true, names, identity, health);
            }

            var characteristics = directoryBytes[cursor + 18];
            var isDeleted = (characteristics & 0x04) != 0;
            var isParent = (characteristics & 0x08) != 0;
            if (isParent && nameLength != 0)
            {
                health.Add(Finding(
                    "UDF_PARENT_IDENTIFIER_NAME_INVALID",
                    ImageHealthSeverity.Error,
                    "The UDF parent File Identifier Descriptor contains a non-empty file identifier.",
                    detection));
                return Result(true, names, identity, health);
            }

            if (!isDeleted && !isParent && nameLength > 0)
            {
                var nameOffset = checked(cursor + 38 + implementationUseLength);
                var name = ReadOstaCompressedUnicode(directoryBytes.AsSpan(nameOffset, nameLength));
                if (string.IsNullOrWhiteSpace(name))
                {
                    health.Add(Finding(
                        "UDF_FILE_IDENTIFIER_NAME_INVALID",
                        ImageHealthSeverity.Warning,
                        "A root-directory File Identifier Descriptor contains a name that cannot be decoded as supported OSTA Compressed Unicode.",
                        detection));
                }
                else
                {
                    names.Add(name);
                }
            }

            entryCount++;
            cursor += paddedLength;
        }

        return Result(true, names, identity, health);
    }

    private static LogicalVolumeInfo? TryParseLogicalVolumeDescriptor(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < 440)
            return null;

        var logicalBlockSize = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(212, 4));
        if (logicalBlockSize is < 512 or > 4096 || !IsPowerOfTwo(logicalBlockSize))
            return null;

        var fileSetDescriptor = ParseLongAd(descriptor.Slice(248, 16));
        var mapTableLength = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(264, 4));
        var mapCount = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(268, 4));
        if (mapTableLength == 0 || mapCount == 0)
            return new LogicalVolumeInfo(fileSetDescriptor, new Dictionary<ushort, ushort>());
        if (mapTableLength > descriptor.Length - 440)
            return null;

        var maps = new Dictionary<ushort, ushort>();
        var cursor = 440;
        var end = checked(440 + (int)mapTableLength);
        uint parsedCount = 0;
        ushort referenceNumber = 0;
        while (cursor < end && parsedCount < mapCount)
        {
            if (end - cursor < 2)
                return null;

            var mapType = descriptor[cursor];
            var mapLength = descriptor[cursor + 1];
            if (mapLength < 2 || mapLength > end - cursor)
                return null;

            if (mapType == 1 && mapLength == 6)
            {
                var partitionNumber = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(cursor + 4, 2));
                maps[referenceNumber] = partitionNumber;
            }

            cursor += mapLength;
            parsedCount++;
            referenceNumber++;
        }

        if (parsedCount != mapCount || cursor != end)
            return null;

        return new LogicalVolumeInfo(fileSetDescriptor, maps);
    }

    private static LongAd ParseLongAd(ReadOnlySpan<byte> value)
    {
        if (value.Length < 16)
            return default;

        var rawLength = BinaryPrimitives.ReadUInt32LittleEndian(value[..4]);
        var extentType = rawLength >> 30;
        var length = rawLength & 0x3FFF_FFFF;
        var logicalBlock = BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(4, 4));
        var partitionReference = BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(8, 2));
        return new LongAd(length, logicalBlock, partitionReference, extentType);
    }

    private static bool TryMapLongAd(
        LongAd address,
        PartitionDescriptorInfo partition,
        int blockSize,
        long regionSizeBytes,
        int minimumLength,
        out long relativeOffset,
        out string error)
    {
        relativeOffset = 0;
        if (address.ExtentType != 0)
        {
            error = "The UDF extent is not recorded-and-allocated and cannot be followed safely.";
            return false;
        }
        if (address.LengthBytes < minimumLength)
        {
            error = $"The UDF extent is shorter than the required {minimumLength} bytes.";
            return false;
        }
        return TryMapPartitionExtent(
            address.LogicalBlockNumber,
            address.LengthBytes,
            partition,
            blockSize,
            regionSizeBytes,
            out relativeOffset,
            out error);
    }

    private static bool TryMapPartitionExtent(
        uint partitionRelativeBlock,
        uint extentLength,
        PartitionDescriptorInfo partition,
        int blockSize,
        long regionSizeBytes,
        out long relativeOffset,
        out string error)
    {
        relativeOffset = 0;
        if (partitionRelativeBlock >= partition.LengthBlocks)
        {
            error = "The UDF extent begins outside the mapped physical partition.";
            return false;
        }

        var blocksNeeded = ((ulong)extentLength + (ulong)blockSize - 1) / (ulong)blockSize;
        if (blocksNeeded > (ulong)partition.LengthBlocks - partitionRelativeBlock)
        {
            error = "The UDF extent exceeds the mapped physical partition.";
            return false;
        }

        try
        {
            var absoluteBlock = checked((ulong)partition.StartBlock + partitionRelativeBlock);
            var offset = checked(absoluteBlock * (ulong)blockSize);
            if (offset > long.MaxValue)
            {
                error = "The UDF physical offset exceeds the supported range.";
                return false;
            }

            relativeOffset = (long)offset;
        }
        catch (OverflowException)
        {
            error = "The UDF physical offset overflows the supported range.";
            return false;
        }

        if (!RangeFits(relativeOffset, extentLength, regionSizeBytes))
        {
            error = "The UDF extent exceeds the bounded recognized filesystem region.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateDescriptorTag(
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
            error = "descriptor CRC length exceeds the bounded descriptor";
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

    private static string ReadOstaDString(ReadOnlySpan<byte> field)
    {
        if (field.Length < 2)
            return string.Empty;

        var recordedLength = field[^1];
        if (recordedLength == 0 || recordedLength > field.Length - 1)
            return string.Empty;

        return ReadOstaCompressedUnicode(field[..recordedLength]);
    }

    private static string ReadOstaCompressedUnicode(ReadOnlySpan<byte> value)
    {
        if (value.Length < 2)
            return string.Empty;

        string result;
        if (value[0] == 8)
        {
            result = Encoding.Latin1.GetString(value[1..]);
        }
        else if (value[0] == 16)
        {
            if ((value.Length - 1) % 2 != 0)
                return string.Empty;

            var builder = new StringBuilder((value.Length - 1) / 2);
            for (var index = 1; index + 1 < value.Length; index += 2)
            {
                var codePoint = BinaryPrimitives.ReadUInt16BigEndian(value.Slice(index, 2));
                if (codePoint != 0)
                    builder.Append((char)codePoint);
            }
            result = builder.ToString();
        }
        else
        {
            return string.Empty;
        }

        return result.TrimEnd('\0', ' ');
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

    private static async Task<byte[]> ReadRelativeAsync(
        FileStream stream,
        FileSystemDetectionInfo detection,
        long relativeOffset,
        int length,
        CancellationToken cancellationToken)
    {
        if (!RangeFits(relativeOffset, length, detection.RegionSizeBytes))
            throw new InvalidDataException("UDF traversal read exceeds the bounded filesystem region.");

        var absoluteOffset = checked(detection.PhysicalOffsetBytes + relativeOffset);
        if (absoluteOffset < 0 || absoluteOffset > stream.Length || length > stream.Length - absoluteOffset)
            throw new InvalidDataException("UDF traversal read exceeds the physical image.");

        var buffer = new byte[length];
        stream.Position = absoluteOffset;
        await stream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken);
        return buffer;
    }

    private static bool RangeFits(long offset, long length, long containerLength)
        => offset >= 0 && length >= 0 && offset <= containerLength && length <= containerLength - offset;

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

    private static bool IsPowerOfTwo(uint value)
        => value > 0 && (value & (value - 1)) == 0;

    private static ImageHealthFinding Finding(
        string code,
        ImageHealthSeverity severity,
        string message,
        FileSystemDetectionInfo detection)
        => new(code, severity, message, "UDF root traversal", detection.PartitionIndex);

    private static UdfTraversalInfo Empty()
        => new(false, 0, Array.Empty<string>(), Array.Empty<ImageIdentityEvidence>(), Array.Empty<ImageHealthFinding>());

    private static UdfTraversalInfo Result(
        bool traversed,
        IReadOnlyCollection<string> names,
        IReadOnlyCollection<ImageIdentityEvidence> identity,
        IReadOnlyCollection<ImageHealthFinding> health)
        => new(
            traversed,
            names.Count,
            Array.AsReadOnly(names.ToArray()),
            Array.AsReadOnly(identity.ToArray()),
            Array.AsReadOnly(health.ToArray()));

    private sealed record LogicalVolumeInfo(
        LongAd FileSetDescriptor,
        IReadOnlyDictionary<ushort, ushort> Type1Maps);

    private readonly record struct PartitionDescriptorInfo(
        ushort PartitionNumber,
        uint StartBlock,
        uint LengthBlocks);

    private readonly record struct LongAd(
        uint LengthBytes,
        uint LogicalBlockNumber,
        ushort PartitionReferenceNumber,
        uint ExtentType);
}
