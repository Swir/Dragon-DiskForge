using System.Buffers.Binary;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-IntelHardening-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await VerifyNtfsMftAndMirrorDepthAsync(root);
    VerifyArchitectureReconciliation();
    await VerifyCancellationAsync(root);
    Console.WriteLine("Dragon DiskForge intelligence-hardening smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task VerifyNtfsMftAndMirrorDepthAsync(string root)
{
    const int sectorSize = 512;
    const int sectorsPerCluster = 8;
    const int clusterSize = sectorSize * sectorsPerCluster;
    const int totalSectors = 256;
    const ulong mftCluster = 4;
    const ulong mirrorCluster = 8;
    const int recordSize = 1024;

    var path = Path.Combine(root, "ntfs-depth.img");
    var bytes = new byte[totalSectors * sectorSize];
    var boot = bytes.AsSpan(0, sectorSize);
    boot[0] = 0xEB;
    boot[1] = 0x52;
    boot[2] = 0x90;
    "NTFS    "u8.CopyTo(boot.Slice(3, 8));
    BinaryPrimitives.WriteUInt16LittleEndian(boot.Slice(11, 2), sectorSize);
    boot[13] = sectorsPerCluster;
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(40, 8), totalSectors);
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(48, 8), mftCluster);
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(56, 8), mirrorCluster);
    boot[64] = unchecked((byte)-10); // 2^10 = 1024-byte FILE records
    boot[68] = 1; // one cluster per index buffer
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(72, 8), 0x1234567890ABCDEFUL);
    boot[510] = 0x55;
    boot[511] = 0xAA;
    boot.CopyTo(bytes.AsSpan((totalSectors - 1) * sectorSize, sectorSize));

    var mftOffset = checked((int)(mftCluster * clusterSize));
    var mirrorOffset = checked((int)(mirrorCluster * clusterSize));
    WriteValidFileRecord(bytes.AsSpan(mftOffset, recordSize), 0xA55A);
    bytes.AsSpan(mftOffset, recordSize).CopyTo(bytes.AsSpan(mirrorOffset, recordSize));
    await File.WriteAllBytesAsync(path, bytes);

    var healthy = await AnalyzeWholeImageAsync(path, "ntfs-depth");
    Require(healthy.Analysis?.FileSystems?.Detections.Single().Kind == FileSystemKind.Ntfs,
        "NTFS must be recognized before deeper metadata checks.");
    Require(healthy.Analysis!.HealthFindings.All(x => x.Code is not "NTFS_MFT_RECORD_INVALID"
        and not "NTFS_MFTMIRR_RECORD_INVALID"
        and not "NTFS_MFT_MIRROR_RECORD_MISMATCH"
        and not "NTFS_MFTMIRR_CLUSTER_OUT_OF_RANGE"),
        "matching valid MFT/MFTMirr FILE records must pass the bounded depth checks.");

    bytes[mirrorOffset + sectorSize - 2] ^= 0x01;
    await File.WriteAllBytesAsync(path, bytes);
    var badFixup = await AnalyzeWholeImageAsync(path, "ntfs-depth");
    Require(badFixup.Analysis!.HealthFindings.Any(x => x.Code == "NTFS_MFTMIRR_RECORD_INVALID"
        && x.Severity == ImageHealthSeverity.Error),
        "a broken MFTMirr update-sequence trailer must fail closed.");

    bytes.AsSpan(mftOffset, recordSize).CopyTo(bytes.AsSpan(mirrorOffset, recordSize));
    bytes[mirrorOffset + 96] ^= 0x5A;
    await File.WriteAllBytesAsync(path, bytes);
    var divergent = await AnalyzeWholeImageAsync(path, "ntfs-depth");
    Require(divergent.Analysis!.HealthFindings.Any(x => x.Code == "NTFS_MFT_MIRROR_RECORD_MISMATCH"
        && x.Severity == ImageHealthSeverity.Warning),
        "logically valid but divergent first MFT/MFTMirr records must be preserved as a warning.");

    bytes.AsSpan(mftOffset, recordSize).CopyTo(bytes.AsSpan(mirrorOffset, recordSize));
    BinaryPrimitives.WriteUInt64LittleEndian(boot.Slice(56, 8), 99);
    boot.CopyTo(bytes.AsSpan((totalSectors - 1) * sectorSize, sectorSize));
    await File.WriteAllBytesAsync(path, bytes);
    var outOfRange = await AnalyzeWholeImageAsync(path, "ntfs-depth");
    Require(outOfRange.Analysis!.HealthFindings.Any(x => x.Code == "NTFS_MFTMIRR_CLUSTER_OUT_OF_RANGE"
        && x.Severity == ImageHealthSeverity.Error),
        "an MFTMirr LCN outside the bounded NTFS volume must be rejected.");
}

static void VerifyArchitectureReconciliation()
{
    var service = new ArchitectureReconciliationService();
    var conflicting = new BootInstallerIntelligenceInfo(
        "iso",
        "ISO",
        1024,
        false,
        null,
        Array.Empty<BootCatalogEntryInfo>(),
        new[]
        {
            new InstallerDetectionInfo(
                InstallerFamily.Linux,
                "Debian-style installer media",
                "x86_64",
                new[] { "install.amd/vmlinuz", "install.amd/initrd.gz" })
        },
        new[] { "ARM64" });

    var conflict = service.Analyze(conflicting);
    Require(conflict.ArchitectureHints.SequenceEqual(new[] { "ARM64", "x86_64" }, StringComparer.OrdinalIgnoreCase),
        "independent EFI and installer architecture evidence must both survive reconciliation.");
    Require(conflict.HealthFindings.Any(x => x.Code == "ARCHITECTURE_EVIDENCE_CONFLICT"
        && x.Severity == ImageHealthSeverity.Warning),
        "one-to-one disagreement between independent architecture sources must be explicit.");

    var multiArch = conflicting with
    {
        ArchitectureHints = new[] { "ARM64", "x86_64" }
    };
    var reconciledMulti = service.Analyze(multiArch);
    Require(reconciledMulti.ArchitectureHints.Count == 2,
        "intentional multi-architecture boot evidence must remain multi-architecture.");
    Require(reconciledMulti.HealthFindings.All(x => x.Code != "ARCHITECTURE_EVIDENCE_CONFLICT"),
        "multi-architecture boot evidence must not be mislabeled as a direct conflict.");
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
        await new NtfsMetadataDepthService().AnalyzeAsync(path, recognition, cts.Token);
        throw new InvalidOperationException("cancellation must stop NTFS depth analysis.");
    }
    catch (OperationCanceledException)
    {
    }
}

static async Task<ImageReport> AnalyzeWholeImageAsync(string path, string providerId)
{
    var provider = new FakeMediaProvider(providerId, new[] { Path.GetExtension(path) });
    var registry = new ProviderRegistry(new[] { new ProviderRegistration(provider, Priority: 10) });
    return await new ImageReportService(registry).AnalyzeAsync(path);
}

static void WriteValidFileRecord(Span<byte> record, ushort updateSequenceNumber)
{
    record.Clear();
    "FILE"u8.CopyTo(record[..4]);
    const ushort usaOffset = 0x30;
    const ushort usaCount = 3;
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(4, 2), usaOffset);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(6, 2), usaCount);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(16, 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(18, 2), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(20, 2), 0x38);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(22, 2), 0x0001);
    BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(24, 4), 128);
    BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(28, 4), (uint)record.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(usaOffset, 2), updateSequenceNumber);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(usaOffset + 2, 2), 0x1111);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(usaOffset + 4, 2), 0x2222);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(510, 2), updateSequenceNumber);
    BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(1022, 2), updateSequenceNumber);
}

static void Require(bool condition, string message)
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
