using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class RawPartitionImageProvider : IPartitionTableProvider
{
    private const int MbrSectorSize = 512;
    private const int PartitionTableOffset = 446;
    private const int MbrEntrySize = 16;
    private const int MaxExtendedPartitions = 128;
    private const int MaxGptEntries = 16_384;
    private const int MaxGptEntrySize = 4_096;
    private static readonly int[] CandidateSectorSizes = [512, 4096];

    private static readonly HashSet<byte> ExtendedPartitionTypes = new() { 0x05, 0x0F, 0x85 };

    private static readonly IReadOnlyDictionary<byte, string> MbrTypeNames =
        new Dictionary<byte, string>
        {
            [0x01] = "FAT12",
            [0x04] = "FAT16 <32 MB",
            [0x05] = "Extended",
            [0x06] = "FAT16",
            [0x07] = "NTFS / exFAT / HPFS",
            [0x0B] = "FAT32",
            [0x0C] = "FAT32 LBA",
            [0x0E] = "FAT16 LBA",
            [0x0F] = "Extended LBA",
            [0x27] = "Windows Recovery",
            [0x82] = "Linux swap",
            [0x83] = "Linux filesystem",
            [0x85] = "Linux extended",
            [0x8E] = "Linux LVM",
            [0xA5] = "FreeBSD",
            [0xA8] = "macOS UFS",
            [0xAB] = "macOS boot",
            [0xAF] = "Apple HFS/HFS+",
            [0xEE] = "GPT protective",
            [0xEF] = "EFI System"
        };

    private static readonly IReadOnlyDictionary<Guid, string> GptTypeNames =
        new Dictionary<Guid, string>
        {
            [Guid.Parse("C12A7328-F81F-11D2-BA4B-00A0C93EC93B")] = "EFI System",
            [Guid.Parse("E3C9E316-0B5C-4DB8-817D-F92DF00215AE")] = "Microsoft Reserved",
            [Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")] = "Microsoft Basic Data",
            [Guid.Parse("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC")] = "Windows Recovery",
            [Guid.Parse("0FC63DAF-8483-4772-8E79-3D69D8477DE4")] = "Linux filesystem",
            [Guid.Parse("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F")] = "Linux swap",
            [Guid.Parse("E6D6D379-F507-44C2-A23C-238F2A3DF928")] = "Linux LVM",
            [Guid.Parse("48465300-0000-11AA-AA11-00306543ECAC")] = "Apple HFS+",
            [Guid.Parse("7C3457EF-0000-11AA-AA11-00306543ECAC")] = "Apple APFS"
        };

    public string Id => "raw-partitions";
    public string DisplayName => "IMG / RAW partitions";
    public IReadOnlyCollection<string> Extensions { get; } = [".img", ".raw", ".dd"];

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            _ = await ReadPartitionTableAsync(path, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException or OverflowException)
        {
            return false;
        }
    }

    public async ValueTask<DiskImageInfo> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var table = await ReadPartitionTableAsync(path, cancellationToken);
        var file = new FileInfo(path);
        var scheme = table.SchemeDisplay;

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            $"IMG / RAW ({scheme})",
            file.Length,
            $"{scheme} partition table ({table.Partitions.Count} partition{(table.Partitions.Count == 1 ? string.Empty : "s")})",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    public async ValueTask<PartitionTableInfo> ReadPartitionTableAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Disk image was not found.", fullPath);

        await using var stream = OpenRead(fullPath);
        if (stream.Length < MbrSectorSize)
            throw new InvalidDataException("RAW image is too small to contain an MBR/GPT partition table.");

        var mbr = new byte[MbrSectorSize];
        await ReadExactlyAtAsync(stream, 0, mbr, cancellationToken);
        ValidateMbrSignature(mbr);

        var primaryEntries = ParseMbrEntries(mbr);
        if (primaryEntries.Count == 0)
            throw new InvalidDataException("MBR signature exists, but no partition entries are present.");

        var hasProtectiveGpt = primaryEntries.Any(entry => entry.Type == 0xEE);
        if (hasProtectiveGpt)
        {
            foreach (var sectorSize in CandidateSectorSizes)
            {
                var gpt = await TryReadGptAsync(stream, sectorSize, cancellationToken);
                if (gpt is not null)
                    return gpt;
            }

            throw new InvalidDataException("Protective MBR found, but no supported GPT header was found.");
        }

        var partitions = new List<PartitionInfo>();
        foreach (var entry in primaryEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ExtendedPartitionTypes.Contains(entry.Type))
            {
                var logical = await ReadExtendedPartitionsAsync(
                    stream,
                    entry,
                    5,
                    cancellationToken);
                partitions.AddRange(logical);
                continue;
            }

            partitions.Add(ToMbrPartition(entry, entry.Slot, stream.Length));
        }

        if (partitions.Count == 0)
            throw new InvalidDataException("The MBR contains no data partitions.");

        return new PartitionTableInfo(
            PartitionTableScheme.Mbr,
            MbrSectorSize,
            partitions.OrderBy(partition => partition.Index).ToArray());
    }

    private static FileStream OpenRead(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

    private static void ValidateMbrSignature(ReadOnlySpan<byte> mbr)
    {
        if (mbr.Length < MbrSectorSize || mbr[510] != 0x55 || mbr[511] != 0xAA)
            throw new InvalidDataException("The image does not contain a valid MBR signature.");
    }

    private static List<MbrEntry> ParseMbrEntries(ReadOnlySpan<byte> mbr)
    {
        var entries = new List<MbrEntry>(4);
        for (var slot = 0; slot < 4; slot++)
        {
            var offset = PartitionTableOffset + (slot * MbrEntrySize);
            var status = mbr[offset];
            var type = mbr[offset + 4];
            var firstLba = BinaryPrimitives.ReadUInt32LittleEndian(mbr.Slice(offset + 8, 4));
            var sectorCount = BinaryPrimitives.ReadUInt32LittleEndian(mbr.Slice(offset + 12, 4));

            if (type == 0 || sectorCount == 0)
                continue;

            entries.Add(new MbrEntry(slot + 1, status, type, firstLba, sectorCount));
        }

        return entries;
    }

    private static PartitionInfo ToMbrPartition(MbrEntry entry, int index, long fileLength)
    {
        ValidateRange(entry.FirstLba, entry.SectorCount, MbrSectorSize, fileLength);

        return new PartitionInfo(
            index,
            entry.FirstLba,
            entry.SectorCount,
            ToLongBytes(entry.FirstLba, MbrSectorSize),
            ToLongBytes(entry.SectorCount, MbrSectorSize),
            $"0x{entry.Type:X2}",
            GetMbrTypeName(entry.Type),
            string.Empty,
            entry.Status == 0x80);
    }

    private static async ValueTask<IReadOnlyList<PartitionInfo>> ReadExtendedPartitionsAsync(
        FileStream stream,
        MbrEntry extendedContainer,
        int firstLogicalIndex,
        CancellationToken cancellationToken)
    {
        var results = new List<PartitionInfo>();
        var baseExtendedLba = (ulong)extendedContainer.FirstLba;
        var currentEbrLba = baseExtendedLba;
        var visited = new HashSet<ulong>();

        for (var count = 0; count < MaxExtendedPartitions; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(currentEbrLba))
                throw new InvalidDataException("The MBR extended-partition chain contains a loop.");

            ValidateRange(currentEbrLba, 1, MbrSectorSize, stream.Length);
            var ebr = new byte[MbrSectorSize];
            await ReadExactlyAtAsync(
                stream,
                checked((long)(currentEbrLba * MbrSectorSize)),
                ebr,
                cancellationToken);

            ValidateMbrSignature(ebr);
            var entries = ParseMbrEntries(ebr);
            if (entries.Count == 0)
                break;

            var logical = entries.FirstOrDefault(entry => !ExtendedPartitionTypes.Contains(entry.Type));
            if (logical is not null)
            {
                var absoluteFirstLba = checked(currentEbrLba + logical.FirstLba);
                ValidateRange(absoluteFirstLba, logical.SectorCount, MbrSectorSize, stream.Length);

                results.Add(new PartitionInfo(
                    firstLogicalIndex + results.Count,
                    absoluteFirstLba,
                    logical.SectorCount,
                    ToLongBytes(absoluteFirstLba, MbrSectorSize),
                    ToLongBytes(logical.SectorCount, MbrSectorSize),
                    $"0x{logical.Type:X2}",
                    GetMbrTypeName(logical.Type),
                    string.Empty,
                    logical.Status == 0x80));
            }

            var link = entries.FirstOrDefault(entry => ExtendedPartitionTypes.Contains(entry.Type));
            if (link is null || link.SectorCount == 0)
                break;

            currentEbrLba = checked(baseExtendedLba + link.FirstLba);
        }

        if (results.Count >= MaxExtendedPartitions)
            throw new InvalidDataException("The MBR extended-partition chain reached the safety limit.");

        return results;
    }

    private static async ValueTask<PartitionTableInfo?> TryReadGptAsync(
        FileStream stream,
        int sectorSize,
        CancellationToken cancellationToken)
    {
        if (stream.Length < sectorSize * 2L)
            return null;

        var header = new byte[sectorSize];
        await ReadExactlyAtAsync(stream, sectorSize, header, cancellationToken);

        if (!header.AsSpan(0, 8).SequenceEqual("EFI PART"u8))
            return null;

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
        var currentLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24, 8));
        var firstUsableLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(40, 8));
        var lastUsableLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(48, 8));
        var entryLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(72, 8));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80, 4));
        var entrySize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(84, 4));

        if (headerSize < 92 || headerSize > sectorSize)
            throw new InvalidDataException("GPT header size is outside the supported bounds.");
        if (currentLba != 1)
            throw new InvalidDataException("GPT primary header is not located at LBA 1.");
        if (firstUsableLba > lastUsableLba)
            throw new InvalidDataException("GPT usable LBA range is invalid.");
        if (entryCount == 0 || entryCount > MaxGptEntries)
            throw new InvalidDataException("GPT partition-entry count exceeds the safety limit.");
        if (entrySize < 128 || entrySize > MaxGptEntrySize || entrySize % 8 != 0)
            throw new InvalidDataException("GPT partition-entry size is invalid.");

        var tableBytes = checked((ulong)entryCount * entrySize);
        var tableOffset = checked(entryLba * (ulong)sectorSize);
        if (tableOffset > (ulong)stream.Length || tableBytes > (ulong)stream.Length - tableOffset)
            throw new InvalidDataException("GPT partition-entry array extends beyond the image.");

        var partitions = new List<PartitionInfo>();
        var entryBuffer = new byte[checked((int)entrySize)];

        for (var i = 0u; i < entryCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = checked(tableOffset + ((ulong)i * entrySize));
            await ReadExactlyAtAsync(stream, checked((long)offset), entryBuffer, cancellationToken);

            var typeGuid = new Guid(entryBuffer.AsSpan(0, 16));
            if (typeGuid == Guid.Empty)
                continue;

            var firstLba = BinaryPrimitives.ReadUInt64LittleEndian(entryBuffer.AsSpan(32, 8));
            var lastLba = BinaryPrimitives.ReadUInt64LittleEndian(entryBuffer.AsSpan(40, 8));
            var attributes = BinaryPrimitives.ReadUInt64LittleEndian(entryBuffer.AsSpan(48, 8));

            if (firstLba > lastLba)
                throw new InvalidDataException($"GPT partition {i + 1} has an invalid LBA range.");
            if (firstLba < firstUsableLba || lastLba > lastUsableLba)
                throw new InvalidDataException($"GPT partition {i + 1} lies outside the usable GPT LBA range.");

            var sectorCount = checked(lastLba - firstLba + 1);
            ValidateRange(firstLba, sectorCount, sectorSize, stream.Length);

            var nameBytes = Math.Min(72, entryBuffer.Length - 56);
            var name = nameBytes > 0
                ? Encoding.Unicode.GetString(entryBuffer, 56, nameBytes).TrimEnd('\0')
                : string.Empty;

            partitions.Add(new PartitionInfo(
                checked((int)i + 1),
                firstLba,
                sectorCount,
                ToLongBytes(firstLba, sectorSize),
                ToLongBytes(sectorCount, sectorSize),
                typeGuid.ToString("D"),
                GptTypeNames.GetValueOrDefault(typeGuid, "Unknown GPT type"),
                name,
                (attributes & (1UL << 2)) != 0));
        }

        if (partitions.Count == 0)
            throw new InvalidDataException("GPT header is supported, but the partition table contains no partitions.");

        return new PartitionTableInfo(
            PartitionTableScheme.Gpt,
            sectorSize,
            partitions.ToArray());
    }

    private static async ValueTask ReadExactlyAtAsync(
        FileStream stream,
        long offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > stream.Length || buffer.Length > stream.Length - offset)
            throw new EndOfStreamException("Requested image range is outside the file.");

        stream.Position = offset;
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of disk image.");
            totalRead += read;
        }
    }

    private static void ValidateRange(
        ulong firstLba,
        ulong sectorCount,
        int sectorSize,
        long fileLength)
    {
        if (sectorCount == 0)
            throw new InvalidDataException("Partition has zero sectors.");

        var offset = checked(firstLba * (ulong)sectorSize);
        var length = checked(sectorCount * (ulong)sectorSize);
        var fileSize = checked((ulong)fileLength);

        if (offset > fileSize || length > fileSize - offset)
            throw new InvalidDataException("Partition extends beyond the image boundary.");
    }

    private static long ToLongBytes(ulong value, int sectorSize)
    {
        var bytes = checked(value * (ulong)sectorSize);
        if (bytes > long.MaxValue)
            throw new InvalidDataException("Partition byte range exceeds the supported size.");
        return (long)bytes;
    }

    private static string GetMbrTypeName(byte type)
        => MbrTypeNames.GetValueOrDefault(type, "Unknown MBR type");

    private sealed record MbrEntry(
        int Slot,
        byte Status,
        byte Type,
        uint FirstLba,
        uint SectorCount);
}
