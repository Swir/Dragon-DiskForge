using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-FileSystemDepth-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await VerifyExFatBootRegionsThroughReportAsync(root);
    await VerifyFat32FsInfoThroughReportAsync(root);
    await VerifyUdfDescriptorDepthThroughReportAsync(root);
    await VerifyCancellationAsync(root);
    Console.WriteLine("Dragon DiskForge filesystem-depth smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task VerifyExFatBootRegionsThroughReportAsync(string root)
{
    const int sectorSize = 512;
    const int totalSectors = 64;
    var path = Path.Combine(root, "healthy-exfat.img");
    var bytes = new byte[totalSectors * sectorSize];

    var main = bytes.AsSpan(0, 12 * sectorSize);
    var boot = main[..sectorSize];
    "EXFAT   "u8.CopyTo(boot.Slice(3, 8));
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(72, 8), totalSectors);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(80, 4), 24);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(84, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(88, 4), 25);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(92, 4), 32);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(96, 4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(100, 4), 0xA1B2C3D4);
    boot[108] = 9;
    boot[109] = 0;
    boot[110] = 1;
    boot[112] = 25;
    boot[510] = 0x55;
    boot[511] = 0xAA;

    var mainChecksum = ComputeExFatBootChecksum(main[..(11 * sectorSize)]);
    FillChecksumSector(main.Slice(11 * sectorSize, sectorSize), mainChecksum);

    main[..(11 * sectorSize)].CopyTo(bytes.AsSpan(12 * sectorSize, 11 * sectorSize));
    var backup = bytes.AsSpan(12 * sectorSize, 12 * sectorSize);
    var backupChecksum = ComputeExFatBootChecksum(backup[..(11 * sectorSize)]);
    FillChecksumSector(backup.Slice(11 * sectorSize, sectorSize), backupChecksum);
    await File.WriteAllBytesAsync(path, bytes);

    var healthy = await AnalyzeWholeImageAsync(path, "exfat-depth");
    Expect(healthy.Analysis?.FileSystems?.Detections.Single().Kind == FileSystemKind.ExFat,
        "exFAT is recognized before deeper boot-region checks.");
    Expect(healthy.Analysis!.HealthFindings.All(x => !x.Code.StartsWith("EXFAT_", StringComparison.Ordinal)
        || x.Code is "EXFAT_VOLUME_DIRTY" or "EXFAT_MEDIA_FAILURE"),
        "matching exFAT main/backup checksums do not create corruption findings.");

    bytes[sectorSize + 17] ^= 0x5A;
    await File.WriteAllBytesAsync(path, bytes);
    var damaged = await AnalyzeWholeImageAsync(path, "exfat-depth");
    Expect(damaged.Analysis!.HealthFindings.Any(x => x.Code == "EXFAT_MAIN_BOOT_CHECKSUM_MISMATCH"),
        "a changed byte in the main exFAT boot region is caught by the boot checksum.");
    Expect(damaged.Analysis.HealthFindings.Any(x => x.Code == "EXFAT_BACKUP_BOOT_MISMATCH"),
        "main/backup exFAT divergence is reported without attempting repair.");
}

static async Task VerifyFat32FsInfoThroughReportAsync(string root)
{
    const int sectorSize = 512;
    const uint totalSectors = 70_000;
    var path = Path.Combine(root, "fat32.img");

    await using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
    {
        stream.SetLength((long)totalSectors * sectorSize);
        var boot = new byte[sectorSize];
        boot[0] = 0xEB;
        boot[1] = 0x58;
        boot[2] = 0x90;
        "MSWIN4.1"u8.CopyTo(boot.AsSpan(3, 8));
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(11, 2), sectorSize);
        boot[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(14, 2), 32);
        boot[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(17, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(19, 2), 0);
        boot[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(22, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(32, 4), totalSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(36, 4), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(44, 4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(48, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(boot.AsSpan(50, 2), 6);
        boot[66] = 0x29;
        BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(67, 4), 0x55667788);
        Encoding.ASCII.GetBytes("DRAGONFAT  ").CopyTo(boot.AsSpan(71, 11));
        "FAT32   "u8.CopyTo(boot.AsSpan(82, 8));
        boot[510] = 0x55;
        boot[511] = 0xAA;
        await stream.WriteAsync(boot);

        stream.Position = 6L * sectorSize;
        await stream.WriteAsync(boot);

        var fsInfo = new byte[sectorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(fsInfo.AsSpan(0, 4), 0x41615252);
        BinaryPrimitives.WriteUInt32LittleEndian(fsInfo.AsSpan(484, 4), 0x61417272);
        BinaryPrimitives.WriteUInt32LittleEndian(fsInfo.AsSpan(488, 4), 90_000);
        BinaryPrimitives.WriteUInt32LittleEndian(fsInfo.AsSpan(492, 4), 90_001);
        BinaryPrimitives.WriteUInt32LittleEndian(fsInfo.AsSpan(508, 4), 0xAA550000);
        stream.Position = sectorSize;
        await stream.WriteAsync(fsInfo);
    }

    var report = await AnalyzeWholeImageAsync(path, "fat32-depth");
    Expect(report.Analysis?.FileSystems?.Detections.Single().Kind == FileSystemKind.Fat32,
        "FAT32 is recognized before bounded FSInfo validation.");
    Expect(report.Analysis!.HealthFindings.Any(x => x.Code == "FAT32_FSINFO_FREE_COUNT_INVALID"),
        "FSInfo free-cluster counts larger than the bounded data region are reported.");
    Expect(report.Analysis.HealthFindings.Any(x => x.Code == "FAT32_FSINFO_NEXT_FREE_INVALID"),
        "FSInfo next-free hints outside the bounded cluster range are reported.");
}

static async Task VerifyUdfDescriptorDepthThroughReportAsync(string root)
{
    const int blockSize = 2048;
    const int blockCount = 400;
    var path = Path.Combine(root, "udf.img");
    var bytes = new byte[blockCount * blockSize];

    WriteVolumeRecognitionDescriptor(bytes.AsSpan(16 * blockSize, blockSize), "BEA01");
    WriteVolumeRecognitionDescriptor(bytes.AsSpan(17 * blockSize, blockSize), "NSR03");
    WriteVolumeRecognitionDescriptor(bytes.AsSpan(18 * blockSize, blockSize), "TEA01");

    const uint sequenceLocation = 260;
    const int sequenceBlocks = 3;
    var anchor = bytes.AsSpan(256 * blockSize, blockSize);
    BinaryPrimitives.WriteUInt32LittleEndian(anchor.Slice(16, 4), sequenceBlocks * blockSize);
    BinaryPrimitives.WriteUInt32LittleEndian(anchor.Slice(20, 4), sequenceLocation);
    FinalizeUdfDescriptorTag(anchor, 2, 256, 16);

    var primary = bytes.AsSpan((int)sequenceLocation * blockSize, blockSize);
    WriteOstaDString(primary.Slice(24, 32), "DRAGON_DISC");
    FinalizeUdfDescriptorTag(primary, 1, sequenceLocation, 64);

    var logical = bytes.AsSpan(((int)sequenceLocation + 1) * blockSize, blockSize);
    WriteOstaDString(logical.Slice(84, 128), "DRAGON UDF");
    BinaryPrimitives.WriteUInt32LittleEndian(logical.Slice(212, 4), blockSize);
    Encoding.ASCII.GetBytes("*OSTA UDF Compliant").CopyTo(logical.Slice(217, 23));
    FinalizeUdfDescriptorTag(logical, 6, sequenceLocation + 1, 424);

    var terminator = bytes.AsSpan(((int)sequenceLocation + 2) * blockSize, blockSize);
    FinalizeUdfDescriptorTag(terminator, 8, sequenceLocation + 2, 0);
    await File.WriteAllBytesAsync(path, bytes);

    var healthy = await AnalyzeWholeImageAsync(path, "udf-depth");
    Expect(healthy.Analysis?.FileSystems?.Detections.Any(x => x.Kind == FileSystemKind.Udf) == true,
        "UDF VRS is recognized before anchor/descriptor depth checks.");
    Expect(healthy.Analysis!.Identity.Any(x => x.Kind == "filesystem-label" && x.Value == "DRAGON UDF"),
        "validated UDF logical-volume d-string becomes filesystem-label identity evidence.");
    Expect(healthy.Analysis.Identity.Any(x => x.Kind == "udf-volume-id" && x.Value == "DRAGON_DISC"),
        "validated UDF primary-volume d-string becomes bounded volume identity evidence.");
    Expect(healthy.Analysis.HealthFindings.All(x => !x.Code.StartsWith("UDF_", StringComparison.Ordinal)),
        "a valid bounded UDF anchor and descriptor sequence produce no UDF corruption finding.");

    bytes[256 * blockSize + 4] ^= 0x01;
    await File.WriteAllBytesAsync(path, bytes);
    var damaged = await AnalyzeWholeImageAsync(path, "udf-depth");
    Expect(damaged.Analysis!.HealthFindings.Any(x => x.Code == "UDF_PRIMARY_ANCHOR_INVALID"),
        "a corrupted UDF anchor tag checksum is surfaced as an evidence-backed error.");
}

static async Task VerifyCancellationAsync(string root)
{
    var path = Path.Combine(root, "cancel.img");
    await File.WriteAllBytesAsync(path, new byte[4096]);
    var recognition = new FileSystemRecognitionInfo(
        "cancel",
        "Cancel",
        4096,
        Array.Empty<FileSystemDetectionInfo>());

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try
    {
        await new FileSystemDepthService().AnalyzeAsync(path, recognition, cts.Token);
        throw new InvalidOperationException("cancellation must stop filesystem-depth analysis.");
    }
    catch (OperationCanceledException)
    {
    }
}

static async Task<ImageReport> AnalyzeWholeImageAsync(string path, string providerId)
{
    var provider = new FakeMediaProvider(providerId, [Path.GetExtension(path)]);
    var registry = new ProviderRegistry([new ProviderRegistration(provider, Priority: 10)]);
    return await new ImageReportService(registry).AnalyzeAsync(path);
}

static void WriteVolumeRecognitionDescriptor(Span<byte> sector, string identifier)
{
    sector.Clear();
    sector[0] = 0;
    Encoding.ASCII.GetBytes(identifier).CopyTo(sector.Slice(1, 5));
    sector[6] = 1;
}

static void WriteOstaDString(Span<byte> field, string value)
{
    field.Clear();
    var encoded = Encoding.Latin1.GetBytes(value);
    if (encoded.Length + 1 > field.Length - 1)
        throw new InvalidOperationException("test d-string is too long.");
    field[0] = 8;
    encoded.CopyTo(field[1..]);
    field[^1] = checked((byte)(encoded.Length + 1));
}

static void FinalizeUdfDescriptorTag(Span<byte> descriptor, ushort tagId, uint location, ushort crcLength)
{
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.Slice(0, 2), tagId);
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.Slice(2, 2), 2);
    descriptor[4] = 0;
    descriptor[5] = 0;
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.Slice(6, 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(descriptor.Slice(10, 2), crcLength);
    BinaryPrimitives.WriteUInt32LittleEndian(descriptor.Slice(12, 4), location);
    BinaryPrimitives.WriteUInt16LittleEndian(
        descriptor.Slice(8, 2),
        crcLength == 0 ? (ushort)0 : ComputeCrc16(descriptor.Slice(16, crcLength)));

    byte checksum = 0;
    for (var index = 0; index < 16; index++)
    {
        if (index != 4)
            checksum = unchecked((byte)(checksum + descriptor[index]));
    }
    descriptor[4] = checksum;
}

static ushort ComputeCrc16(ReadOnlySpan<byte> data)
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

static uint ComputeExFatBootChecksum(ReadOnlySpan<byte> bootSectors)
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

static void FillChecksumSector(Span<byte> sector, uint checksum)
{
    for (var offset = 0; offset < sector.Length; offset += sizeof(uint))
        BinaryPrimitives.WriteUInt32LittleEndian(sector.Slice(offset, sizeof(uint)), checksum);
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class FakeMediaProvider : IMediaGeometryProvider
{
    public FakeMediaProvider(string id, IReadOnlyCollection<string> extensions)
    {
        Id = id;
        Extensions = extensions;
    }

    public string Id { get; }
    public string DisplayName => "Fake Physical Media Provider";
    public IReadOnlyCollection<string> Extensions { get; }

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    public ValueTask<MediaGeometryInfo> ReadMediaGeometryAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = new FileInfo(imagePath).Length;
        return ValueTask.FromResult(new MediaGeometryInfo(
            "test media",
            512,
            1,
            1,
            checked((int)Math.Min(int.MaxValue, Math.Max(1, length / 512))),
            checked((int)Math.Min(int.MaxValue, Math.Max(1, length / 512))),
            length,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            null));
    }

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName,
            file.Name,
            "TEST-PHYSICAL",
            file.Length,
            "generated physical-media fixture",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true));
    }
}
