using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-ImageIntel-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await VerifyPartitionFilesystemAggregationAsync(root);
    await VerifyNtfsBackupHealthAsync(root);
    await VerifyExtHealthAsync(root);
    await VerifyContainerIdentityAsync(root);
    await VerifyFfuPlatformIdentityAsync(root);
    await VerifyMetadataOnlyByteMappingBoundaryAsync(root);
    await VerifyCancellationAsync(root);
    Console.WriteLine("Dragon DiskForge image-intelligence smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task VerifyPartitionFilesystemAggregationAsync(string root)
{
    var path = Path.Combine(root, "health.img");
    const int sectorSize = 512;
    const int partitionSectors = 64;
    var bytes = new byte[(partitionSectors + 1) * sectorSize];
    var boot = bytes.AsSpan(sectorSize, sectorSize);

    "EXFAT   "u8.CopyTo(boot.Slice(3, 8));
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(72, 8), partitionSectors);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(80, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(84, 4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(88, 4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(92, 4), 10);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(96, 4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(boot.Slice(100, 4), 0x1234ABCD);
    BinaryPrimitives.WriteUInt16LittleEndian(boot.Slice(106, 2), 0x0006); // dirty + media failure
    boot[108] = 9;
    boot[109] = 0;
    boot[110] = 1;
    boot[510] = 0x55;
    boot[511] = 0xAA;
    await File.WriteAllBytesAsync(path, bytes);

    var table = SinglePartitionTable(
        sectorSize,
        1,
        partitionSectors,
        "DataVolume",
        "EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");

    var info = await AnalyzePartitionImageAsync(path, table, "fake-raw");
    Expect(info.ProviderId == "fake-raw", "provider identity is preserved.");
    Expect(info.PartitionLayout is not null && info.PartitionLayout.Partitions.Count == 1,
        "partition intelligence is included when the capability exists.");
    Expect(info.FileSystems is not null
        && info.FileSystems.Detections.Count == 1
        && info.FileSystems.Detections[0].Kind == FileSystemKind.ExFat,
        "filesystem recognition is aggregated over the validated partition range.");
    Expect(info.Identity.Any(x => x.Kind == "partition-name" && x.Value == "DataVolume" && x.PartitionIndex == 1),
        "partition names become bounded identity evidence.");
    Expect(info.Identity.Any(x => x.Kind == "filesystem-id" && x.Value == "1234ABCD" && x.PartitionIndex == 1),
        "filesystem identifiers become bounded identity evidence.");
    Expect(info.HealthFindings.Any(x => x.Code == "EXFAT_VOLUME_DIRTY" && x.Severity == ImageHealthSeverity.Warning),
        "exFAT dirty state becomes a health warning.");
    Expect(info.HealthFindings.Any(x => x.Code == "EXFAT_MEDIA_FAILURE" && x.Severity == ImageHealthSeverity.Error),
        "exFAT media-failure state becomes a health error.");
    Expect(info.HasWarnings && info.HasErrors, "health summary reflects warning and error evidence.");
}

static async Task VerifyNtfsBackupHealthAsync(string root)
{
    var path = Path.Combine(root, "ntfs.img");
    const int sectorSize = 512;
    const int partitionSectors = 16;
    var bytes = new byte[(partitionSectors + 1) * sectorSize];
    var boot = bytes.AsSpan(sectorSize, sectorSize);

    boot[0] = 0xEB;
    boot[1] = 0x52;
    boot[2] = 0x90;
    "NTFS    "u8.CopyTo(boot.Slice(3, 8));
    BinaryPrimitives.WriteUInt16LittleEndian(boot.Slice(11, 2), sectorSize);
    boot[13] = 1;
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(40, 8), partitionSectors);
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(48, 8), 4);
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(72, 8), 0x1122334455667788UL);
    boot[510] = 0x55;
    boot[511] = 0xAA;
    // The bounded backup boot sector at the end of the volume intentionally remains zeroed.
    await File.WriteAllBytesAsync(path, bytes);

    var table = SinglePartitionTable(sectorSize, 1, partitionSectors, "Windows", "0x07");
    var info = await AnalyzePartitionImageAsync(path, table, "fake-ntfs");

    Expect(info.FileSystems?.Detections.Single().Kind == FileSystemKind.Ntfs,
        "NTFS boot metadata is recognized before health analysis.");
    Expect(info.Identity.Any(x => x.Kind == "filesystem-id" && x.Value == "1122334455667788"),
        "NTFS volume serial is aggregated as filesystem identity.");
    Expect(info.HealthFindings.Any(x => x.Code == "NTFS_BACKUP_BOOT_MISMATCH" && x.Severity == ImageHealthSeverity.Warning),
        "NTFS primary/backup boot metadata mismatch is reported without attempting repair.");
}

static async Task VerifyExtHealthAsync(string root)
{
    var path = Path.Combine(root, "ext.img");
    const int sectorSize = 512;
    const int partitionSectors = 32;
    var bytes = new byte[(partitionSectors + 1) * sectorSize];
    var super = bytes.AsSpan(sectorSize + 1024, 1024);

    BinaryPrimitives.WriteUInt32LittleEndian(super.Slice(4, 4), 8); // blocks
    BinaryPrimitives.WriteUInt32LittleEndian(super.Slice(24, 4), 0); // 1024-byte blocks
    BinaryPrimitives.WriteUInt32LittleEndian(super.Slice(32, 4), 8);
    BinaryPrimitives.WriteUInt32LittleEndian(super.Slice(40, 4), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(super.Slice(56, 2), 0xEF53);
    BinaryPrimitives.WriteUInt16LittleEndian(super.Slice(58, 2), 0x0002); // errors detected
    var uuid = Enumerable.Range(1, 16).Select(x => (byte)x).ToArray();
    uuid.CopyTo(super.Slice(104, 16));
    Encoding.UTF8.GetBytes("ROOTFS").CopyTo(super.Slice(120, 16));
    await File.WriteAllBytesAsync(path, bytes);

    var table = SinglePartitionTable(sectorSize, 1, partitionSectors, "Linux root", "0x83");
    var info = await AnalyzePartitionImageAsync(path, table, "fake-ext");

    Expect(info.FileSystems?.Detections.Single().Kind == FileSystemKind.Ext2,
        "ext superblock is recognized before health analysis.");
    Expect(info.Identity.Any(x => x.Kind == "filesystem-label" && x.Value == "ROOTFS"),
        "ext label is aggregated as cross-source identity evidence.");
    Expect(info.HealthFindings.Any(x => x.Code == "EXT_ERRORS_RECORDED" && x.Severity == ImageHealthSeverity.Error),
        "ext error state is surfaced as evidence-backed health error.");
}

static async Task VerifyContainerIdentityAsync(string root)
{
    var path = Path.Combine(root, "container.wim");
    await File.WriteAllBytesAsync(path, new byte[4096]);

    const string guid = "01234567-89AB-CDEF-0123-456789ABCDEF";
    var provider = new FakeWimProvider("fake-wim", [".wim"], guid);
    var registry = new ProviderRegistry([new ProviderRegistration(provider, Priority: 10)]);
    var info = await new ImageIntelligenceService(registry).AnalyzeAsync(path);

    Expect(info.FileSystems is null, "container metadata does not pretend physical guest filesystem mapping.");
    Expect(info.Identity.Any(x => x.Kind == "container-guid" && x.Value == guid),
        "WIM container GUID is aggregated as container identity evidence.");
}

static async Task VerifyFfuPlatformIdentityAsync(string root)
{
    var path = Path.Combine(root, "phone.ffu");
    await File.WriteAllBytesAsync(path, new byte[8192]);

    const string platform = "Contoso.Phone.Reference";
    var provider = new FakeFfuProvider("fake-ffu", [".ffu"], platform);
    var registry = new ProviderRegistry([new ProviderRegistration(provider, Priority: 10)]);
    var info = await new ImageIntelligenceService(registry).AnalyzeAsync(path);

    Expect(info.FileSystems is null, "FFU container metadata does not imply raw filesystem byte mapping.");
    Expect(info.Identity.Any(x => x.Kind == "platform-id" && x.Value == platform),
        "FFU PlatformID is aggregated as bounded platform identity evidence.");
}

static async Task VerifyMetadataOnlyByteMappingBoundaryAsync(string root)
{
    var path = Path.Combine(root, "metadata.vmdk");
    var bytes = new byte[4096];
    "EXFAT   "u8.CopyTo(bytes.AsSpan(3, 8));
    bytes[510] = 0x55;
    bytes[511] = 0xAA;
    await File.WriteAllBytesAsync(path, bytes);

    var provider = new FakeProvider("metadata-only", [".vmdk"]);
    var registry = new ProviderRegistry([new ProviderRegistration(provider, Priority: 10)]);
    var info = await new ImageIntelligenceService(registry).AnalyzeAsync(path);

    Expect(info.FileSystems is null,
        "metadata-only providers do not gain filesystem probing from coincidental physical bytes.");
    Expect(info.PartitionLayout is null && info.BootInstaller is null,
        "unsupported intelligence surfaces stay absent rather than guessed.");
}

static async Task VerifyCancellationAsync(string root)
{
    var path = Path.Combine(root, "cancel.img");
    await File.WriteAllBytesAsync(path, new byte[4096]);
    var provider = new FakeProvider("cancel", [".img"]);
    var registry = new ProviderRegistry([new ProviderRegistration(provider)]);
    var service = new ImageIntelligenceService(registry);

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try
    {
        await service.AnalyzeAsync(path, cts.Token);
        throw new InvalidOperationException("cancellation must be a hard stop.");
    }
    catch (OperationCanceledException)
    {
    }
}

static PartitionTableInfo SinglePartitionTable(
    int sectorSize,
    ulong firstLba,
    ulong sectorCount,
    string name,
    string typeId)
    => new(
        PartitionTableScheme.Gpt,
        sectorSize,
        [new PartitionInfo(
            1,
            firstLba,
            sectorCount,
            checked((long)(firstLba * (ulong)sectorSize)),
            checked((long)(sectorCount * (ulong)sectorSize)),
            typeId,
            "Test partition",
            name,
            false)]);

static async Task<ImageIntelligenceInfo> AnalyzePartitionImageAsync(
    string path,
    PartitionTableInfo table,
    string providerId)
{
    var provider = new FakePartitionProvider(providerId, [Path.GetExtension(path)], table);
    var registry = new ProviderRegistry([new ProviderRegistration(provider, Priority: 10)]);
    return await new ImageIntelligenceService(registry).AnalyzeAsync(path);
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class FakeProvider : IDiskImageProvider
{
    public FakeProvider(string id, IReadOnlyCollection<string> extensions)
    {
        Id = id;
        Extensions = extensions;
    }

    public string Id { get; }
    public IReadOnlyCollection<string> Extensions { get; }

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName,
            file.Name,
            "FAKE",
            file.Length,
            "fake metadata-only image",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true));
    }
}

sealed class FakePartitionProvider : IPartitionTableProvider
{
    private readonly PartitionTableInfo _table;

    public FakePartitionProvider(string id, IReadOnlyCollection<string> extensions, PartitionTableInfo table)
    {
        Id = id;
        Extensions = extensions;
        _table = table;
    }

    public string Id { get; }
    public string DisplayName => "Fake Partition Provider";
    public IReadOnlyCollection<string> Extensions { get; }

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    public ValueTask<PartitionTableInfo> ReadPartitionTableAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_table);
    }

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName,
            file.Name,
            "FAKE-PARTITION",
            file.Length,
            "fake physical partition image",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true));
    }
}

sealed class FakeWimProvider : IWimMetadataProvider
{
    private readonly string _guid;

    public FakeWimProvider(string id, IReadOnlyCollection<string> extensions, string guid)
    {
        Id = id;
        Extensions = extensions;
        _guid = guid;
    }

    public string Id { get; }
    public string DisplayName => "Fake WIM Provider";
    public IReadOnlyCollection<string> Extensions { get; }

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    public ValueTask<WimMetadataInfo> ReadWimMetadataAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var empty = new WimResourceInfo("none", 0, 0, 0, 0);
        return ValueTask.FromResult(new WimMetadataInfo(
            imagePath,
            208,
            WimMetadataInfo.StandardVersion,
            0,
            32768,
            _guid,
            1,
            1,
            1,
            empty,
            empty,
            empty,
            0,
            empty));
    }

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName,
            file.Name,
            "WIM",
            file.Length,
            "fake WIM container",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true));
    }
}

sealed class FakeFfuProvider : IFfuMetadataProvider
{
    private readonly string _platformId;

    public FakeFfuProvider(string id, IReadOnlyCollection<string> extensions, string platformId)
    {
        Id = id;
        Extensions = extensions;
        _platformId = platformId;
    }

    public string Id { get; }
    public string DisplayName => "Fake FFU Provider";
    public IReadOnlyCollection<string> Extensions { get; }

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    public ValueTask<FfuMetadataInfo> ReadFfuMetadataAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = new FileInfo(imagePath).Length;
        return ValueTask.FromResult(new FfuMetadataInfo(
            imagePath,
            new FfuSecurityMetadata(32, 128, 0x800C, 0, 0, 4096),
            new FfuImageMetadata(24, 0, 128, 4096, 4096),
            new FfuStoreMetadata(_platformId, 131072, 0, 0, 0, 0, 8192, 0),
            length));
    }

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName,
            file.Name,
            "FFU",
            file.Length,
            "fake FFU container",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true));
    }
}
