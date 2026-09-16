using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Reads MBR/EBR/GPT metadata from a proven guest-visible byte source.
/// The reader never reaches outside the guest address space exposed by <see cref="IGuestByteReader"/>.
/// </summary>
public static class GuestPartitionTableReader
{
    private const int MbrSectorSize = 512;
    private const int PartitionTableOffset = 446;
    private const int MbrEntrySize = 16;
    private const int MaxExtendedPartitions = 128;
    private const int MaxGptEntries = 16_384;
    private const int MaxGptEntrySize = 4_096;
    private const int CrcBufferSize = 64 * 1024;
    private static readonly int[] CandidateSectorSizes = [512, 4096];
    private static readonly HashSet<byte> ExtendedPartitionTypes = new() { 0x05, 0x0F, 0x85 };
    private static readonly uint[] Crc32Table = CreateCrc32Table();

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

    public static async ValueTask<PartitionTableInfo?> TryReadAsync(
        IGuestByteReader reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        cancellationToken.ThrowIfCancellationRequested();

        if (reader.Length < MbrSectorSize)
            return null;

        var mbr = new byte[MbrSectorSize];
        await ReadExactlyAtAsync(reader, 0, mbr, cancellationToken);
        if (!HasMbrSignature(mbr))
            return null;

        var primaryEntries = ParseMbrEntries(mbr);
        if (primaryEntries.Count == 0)
            return null;

        if (primaryEntries.Any(entry => entry.Type == 0xEE))
        {
            foreach (var sectorSize in CandidateSectorSizes)
            {
                var gpt = await TryReadGptAsync(reader, sectorSize, cancellationToken);
                if (gpt is not null)
                    return gpt;
            }

            throw new InvalidDataException("Protective MBR found in guest bytes, but no supported GPT header was found.");
        }

        var partitions = new List<PartitionInfo>();
        foreach (var entry in primaryEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ExtendedPartitionTypes.Contains(entry.Type))
            {
                partitions.AddRange(await ReadExtendedPartitionsAsync(
                    reader,
                    entry,
                    5,
                    cancellationToken));
                continue;
            }

            partitions.Add(ToMbrPartition(entry, entry.Slot, reader.Length));
        }

        if (partitions.Count == 0)
            return null;

        return new PartitionTableInfo(
            PartitionTableScheme.Mbr,
            MbrSectorSize,
            partitions.OrderBy(partition => partition.Index).ToArray());
    }

    private static bool HasMbrSignature(ReadOnlySpan<byte> mbr)
        => mbr.Length >= MbrSectorSize && mbr[510] == 0x55 && mbr[511] == 0xAA;

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

            if (status is not 0x00 and not 0x80)
                throw new InvalidDataException($"Guest MBR partition slot {slot + 1} has an invalid boot-status byte 0x{status:X2}.");

            entries.Add(new MbrEntry(slot + 1, status, type, firstLba, sectorCount));
        }

        return entries;
    }

    private static PartitionInfo ToMbrPartition(MbrEntry entry, int index, ulong guestLength)
    {
        ValidateRange(entry.FirstLba, entry.SectorCount, MbrSectorSize, guestLength);
        return new PartitionInfo(
            index,
            entry.FirstLba,
            entry.SectorCount,
            ToLongBytes(entry.FirstLba, MbrSectorSize),
            ToLongBytes(entry.SectorCount, MbrSectorSize),
            $"0x{entry.Type:X2}",
            MbrTypeNames.GetValueOrDefault(entry.Type, "Unknown MBR type"),
            string.Empty,
            entry.Status == 0x80);
    }

    private static async ValueTask<IReadOnlyList<PartitionInfo>> ReadExtendedPartitionsAsync(
        IGuestByteReader reader,
        MbrEntry extendedContainer,
        int firstLogicalIndex,
        CancellationToken cancellationToken)
    {
        ValidateRange(extendedContainer.FirstLba, extendedContainer.SectorCount, MbrSectorSize, reader.Length);

        var results = new List<PartitionInfo>();
        var baseExtendedLba = (ulong)extendedContainer.FirstLba;
        var extendedEndLbaExclusive = checked(baseExtendedLba + extendedContainer.SectorCount);
        var currentEbrLba = baseExtendedLba;
        var visited = new HashSet<ulong>();

        for (var count = 0; count < MaxExtendedPartitions; count++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(currentEbrLba))
                throw new InvalidDataException("The guest MBR extended-partition chain contains a loop.");
            if (currentEbrLba < baseExtendedLba || currentEbrLba >= extendedEndLbaExclusive)
                throw new InvalidDataException("The guest EBR chain escapes its declared extended-partition container.");

            ValidateRange(currentEbrLba, 1, MbrSectorSize, reader.Length);
            var ebr = new byte[MbrSectorSize];
            await ReadExactlyAtAsync(
                reader,
                checked(currentEbrLba * MbrSectorSize),
                ebr,
                cancellationToken);

            if (!HasMbrSignature(ebr))
                throw new InvalidDataException("The guest EBR does not contain a valid MBR signature.");

            var entries = ParseMbrEntries(ebr);
            if (entries.Count == 0)
                break;

            var logical = entries.FirstOrDefault(entry => !ExtendedPartitionTypes.Contains(entry.Type));
            if (logical is not null)
            {
                var absoluteFirstLba = checked(currentEbrLba + logical.FirstLba);
                var logicalEndLbaExclusive = checked(absoluteFirstLba + logical.SectorCount);
                if (absoluteFirstLba < baseExtendedLba || logicalEndLbaExclusive > extendedEndLbaExclusive)
                    throw new InvalidDataException("A guest logical partition escapes its declared extended-partition container.");

                ValidateRange(absoluteFirstLba, logical.SectorCount, MbrSectorSize, reader.Length);
                results.Add(new PartitionInfo(
                    firstLogicalIndex + results.Count,
                    absoluteFirstLba,
                    logical.SectorCount,
                    ToLongBytes(absoluteFirstLba, MbrSectorSize),
                    ToLongBytes(logical.SectorCount, MbrSectorSize),
                    $"0x{logical.Type:X2}",
                    MbrTypeNames.GetValueOrDefault(logical.Type, "Unknown MBR type"),
                    string.Empty,
                    logical.Status == 0x80));
            }

            var link = entries.FirstOrDefault(entry => ExtendedPartitionTypes.Contains(entry.Type));
            if (link is null || link.SectorCount == 0)
                break;

            var nextEbrLba = checked(baseExtendedLba + link.FirstLba);
            if (nextEbrLba < baseExtendedLba || nextEbrLba >= extendedEndLbaExclusive)
                throw new InvalidDataException("The guest EBR link escapes its declared extended-partition container.");

            currentEbrLba = nextEbrLba;
        }

        if (results.Count >= MaxExtendedPartitions)
            throw new InvalidDataException("The guest MBR extended-partition chain reached the safety limit.");

        return results;
    }

    private static async ValueTask<PartitionTableInfo?> TryReadGptAsync(
        IGuestByteReader reader,
        int sectorSize,
        CancellationToken cancellationToken)
    {
        if (reader.Length < (ulong)sectorSize * 2)
            return null;

        var header = new byte[sectorSize];
        await ReadExactlyAtAsync(reader, (ulong)sectorSize, header, cancellationToken);
        if (!header.AsSpan(0, 8).SequenceEqual("EFI PART"u8))
            return null;

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
        if (headerSize < 92 || headerSize > sectorSize)
            throw new InvalidDataException("Guest GPT header size is outside the supported bounds.");

        var expectedHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16, 4));
        var headerForCrc = header.AsSpan(0, checked((int)headerSize)).ToArray();
        headerForCrc.AsSpan(16, 4).Clear();
        if (ComputeCrc32(headerForCrc) != expectedHeaderCrc)
            throw new InvalidDataException("Guest GPT primary-header CRC32 does not match the declared checksum.");

        var currentLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(24, 8));
        var backupLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(32, 8));
        var firstUsableLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(40, 8));
        var lastUsableLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(48, 8));
        var entryLba = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(72, 8));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80, 4));
        var entrySize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(84, 4));
        var expectedEntryArrayCrc = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(88, 4));
        var guestSectors = reader.Length / (ulong)sectorSize;

        if (currentLba != 1)
            throw new InvalidDataException("Guest GPT primary header is not located at LBA 1.");
        if (backupLba == currentLba || backupLba >= guestSectors)
            throw new InvalidDataException("Guest GPT backup-header LBA is outside the guest address space.");
        if (firstUsableLba > lastUsableLba)
            throw new InvalidDataException("Guest GPT usable LBA range is invalid.");
        if (firstUsableLba < 2 || lastUsableLba >= guestSectors || backupLba <= lastUsableLba)
            throw new InvalidDataException("Guest GPT usable LBA range conflicts with the guest geometry or backup header.");
        if (entryCount == 0 || entryCount > MaxGptEntries)
            throw new InvalidDataException("Guest GPT partition-entry count exceeds the safety limit.");
        if (entrySize < 128 || entrySize > MaxGptEntrySize || entrySize % 8 != 0)
            throw new InvalidDataException("Guest GPT partition-entry size is invalid.");
        if (entryLba < 2 || entryLba >= guestSectors)
            throw new InvalidDataException("Guest GPT partition-entry array begins outside the supported primary metadata region.");

        var tableBytes = checked((ulong)entryCount * entrySize);
        var tableOffset = checked(entryLba * (ulong)sectorSize);
        EnsureByteRange(tableOffset, tableBytes, reader.Length, "Guest GPT partition-entry array");
        var entryArrayEnd = checked(tableOffset + tableBytes);
        var firstUsableOffset = checked(firstUsableLba * (ulong)sectorSize);
        if (entryArrayEnd > firstUsableOffset)
            throw new InvalidDataException("Guest GPT partition-entry array overlaps the declared usable guest area.");

        var actualEntryArrayCrc = await ComputeCrc32Async(reader, tableOffset, tableBytes, cancellationToken);
        if (actualEntryArrayCrc != expectedEntryArrayCrc)
            throw new InvalidDataException("Guest GPT partition-entry-array CRC32 does not match the declared checksum.");

        var partitions = new List<PartitionInfo>();
        var entryBuffer = new byte[checked((int)entrySize)];
        for (var i = 0u; i < entryCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = checked(tableOffset + ((ulong)i * entrySize));
            await ReadExactlyAtAsync(reader, offset, entryBuffer, cancellationToken);

            var typeGuid = new Guid(entryBuffer.AsSpan(0, 16));
            if (typeGuid == Guid.Empty)
                continue;

            var firstLba = BinaryPrimitives.ReadUInt64LittleEndian(entryBuffer.AsSpan(32, 8));
            var lastLba = BinaryPrimitives.ReadUInt64LittleEndian(entryBuffer.AsSpan(40, 8));
            var attributes = BinaryPrimitives.ReadUInt64LittleEndian(entryBuffer.AsSpan(48, 8));
            if (firstLba > lastLba)
                throw new InvalidDataException($"Guest GPT partition {i + 1} has an invalid LBA range.");
            if (firstLba < firstUsableLba || lastLba > lastUsableLba)
                throw new InvalidDataException($"Guest GPT partition {i + 1} lies outside the usable GPT LBA range.");

            var sectorCount = checked(lastLba - firstLba + 1);
            ValidateRange(firstLba, sectorCount, sectorSize, reader.Length);
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
            throw new InvalidDataException("Guest GPT header is supported, but the partition table contains no partitions.");

        return new PartitionTableInfo(
            PartitionTableScheme.Gpt,
            sectorSize,
            partitions.ToArray());
    }

    private static async ValueTask ReadExactlyAtAsync(
        IGuestByteReader reader,
        ulong offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        EnsureByteRange(offset, (ulong)buffer.Length, reader.Length, "Requested guest range");
        await reader.ReadExactlyAsync(offset, buffer, cancellationToken);
    }

    private static void ValidateRange(
        ulong firstLba,
        ulong sectorCount,
        int sectorSize,
        ulong guestLength)
    {
        if (sectorCount == 0)
            throw new InvalidDataException("Guest partition has zero sectors.");

        var offset = checked(firstLba * (ulong)sectorSize);
        var length = checked(sectorCount * (ulong)sectorSize);
        EnsureByteRange(offset, length, guestLength, "Guest partition");
    }

    private static void EnsureByteRange(ulong offset, ulong length, ulong totalLength, string description)
    {
        if (offset > totalLength || length > totalLength - offset)
            throw new InvalidDataException($"{description} extends beyond the guest address space.");
    }

    private static long ToLongBytes(ulong value, int sectorSize)
    {
        var bytes = checked(value * (ulong)sectorSize);
        if (bytes > long.MaxValue)
            throw new InvalidDataException("Guest partition byte range exceeds the supported size.");
        return (long)bytes;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
            crc = Crc32Table[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
        return ~crc;
    }

    private static async ValueTask<uint> ComputeCrc32Async(
        IGuestByteReader reader,
        ulong offset,
        ulong length,
        CancellationToken cancellationToken)
    {
        var crc = 0xFFFFFFFFu;
        var remaining = length;
        var currentOffset = offset;
        var buffer = new byte[CrcBufferSize];

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = checked((int)Math.Min((ulong)buffer.Length, remaining));
            await ReadExactlyAtAsync(reader, currentOffset, buffer.AsMemory(0, count), cancellationToken);
            for (var i = 0; i < count; i++)
                crc = Crc32Table[(int)((crc ^ buffer[i]) & 0xFF)] ^ (crc >> 8);
            currentOffset = checked(currentOffset + (ulong)count);
            remaining -= (ulong)count;
        }

        return ~crc;
    }

    private static uint[] CreateCrc32Table()
    {
        const uint polynomial = 0xEDB88320u;
        var table = new uint[256];
        for (var i = 0; i < table.Length; i++)
        {
            var value = (uint)i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? polynomial ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }

    private sealed record MbrEntry(
        int Slot,
        byte Status,
        byte Type,
        uint FirstLba,
        uint SectorCount);
}
