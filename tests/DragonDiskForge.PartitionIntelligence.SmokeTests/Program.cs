using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-PartitionIntel-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var imagePath = Path.Combine(root, "sample.img");
await File.WriteAllBytesAsync(imagePath, new byte[64 * 512]);

try
{
    var cleanTable = new PartitionTableInfo(
        PartitionTableScheme.Mbr,
        512,
        [
            new PartitionInfo(1, 1, 10, 512, 10 * 512, "0x0C", "FAT32 LBA", "System", true),
            new PartitionInfo(2, 20, 5, 20 * 512, 5 * 512, "0x07", "NTFS/exFAT", "Data", false)
        ]);

    var provider = new FakePartitionProvider("fake-partitions", [".img"], cleanTable);
    var registry = new ProviderRegistry([new ProviderRegistration(provider, Priority: 10)]);
    var service = new PartitionIntelligenceService(registry);

    var clean = await service.AnalyzeAsync(imagePath);
    Expect(clean.ProviderId == "fake-partitions", "service resolves partition intelligence through provider capability.");
    Expect(clean.Scheme == PartitionTableScheme.Mbr && clean.SectorSize == 512, "partition scheme and sector size are preserved.");
    Expect(clean.Partitions.Count == 2 && clean.BootablePartitionCount == 1, "partition and bootable counts are summarized.");
    Expect(!clean.HasErrors && !clean.HasWarnings, "clean layout has no structural errors or warnings.");
    Expect(registry.Providers.Single().Capabilities.HasFlag(ProviderCapabilities.PartitionTable),
        "fake provider advertises PartitionTable capability without format-specific code.");

    var overlap = PartitionIntelligenceService.AnalyzeLayout(
        new PartitionTableInfo(
            PartitionTableScheme.Gpt,
            512,
            [
                new PartitionInfo(1, 10, 20, 10 * 512, 20 * 512, "A", "Type A", "A", false),
                new PartitionInfo(2, 25, 10, 25 * 512, 10 * 512, "B", "Type B", "B", false)
            ]),
        64 * 512,
        "test",
        "Test provider");
    Expect(overlap.Findings.Any(x => x.Code == "OVERLAP" && x.PartitionIndex == 2),
        "overlapping LBA ranges are reported structurally.");
    Expect(overlap.HasErrors, "overlap marks the layout as erroneous.");

    var badGeometry = PartitionIntelligenceService.AnalyzeLayout(
        new PartitionTableInfo(
            PartitionTableScheme.Mbr,
            512,
            [
                new PartitionInfo(1, 2, 8, 999, 1234, "0x07", "NTFS/exFAT", "Mismatch", false),
                new PartitionInfo(2, 60, 10, 60 * 512, 10 * 512, "0x0C", "FAT32 LBA", "Beyond", false)
            ]),
        64 * 512,
        "test",
        "Test provider");
    Expect(badGeometry.Findings.Any(x => x.Code == "OFFSET_MISMATCH" && x.PartitionIndex == 1),
        "offset mismatch is detected.");
    Expect(badGeometry.Findings.Any(x => x.Code == "SIZE_MISMATCH" && x.PartitionIndex == 1),
        "size mismatch is detected.");
    Expect(badGeometry.Findings.Any(x => x.Code == "OUT_OF_BOUNDS" && x.PartitionIndex == 2),
        "partition ranges beyond the physical image are detected.");

    var duplicateZero = PartitionIntelligenceService.AnalyzeLayout(
        new PartitionTableInfo(
            PartitionTableScheme.Mbr,
            512,
            [
                new PartitionInfo(7, 1, 1, 512, 512, "x", "x", "one", false),
                new PartitionInfo(7, 2, 0, 1024, 0, "y", "y", "two", false)
            ]),
        64 * 512,
        "test",
        "Test provider");
    Expect(duplicateZero.Findings.Any(x => x.Code == "DUPLICATE_INDEX"),
        "duplicate partition indexes are rejected by structural intelligence.");
    Expect(duplicateZero.Findings.Any(x => x.Code == "ZERO_LENGTH"),
        "zero-length partition entries are reported.");

    var empty = PartitionIntelligenceService.AnalyzeLayout(
        new PartitionTableInfo(PartitionTableScheme.Gpt, 4096, []),
        4096,
        "test",
        "Test provider");
    Expect(empty.Findings.Count == 1
        && empty.Findings[0].Code == "NO_PARTITIONS"
        && empty.Findings[0].Severity == PartitionFindingSeverity.Info,
        "empty tables are represented as informational rather than fake corruption.");

    ExpectThrows<InvalidDataException>(
        () => PartitionIntelligenceService.AnalyzeLayout(
            new PartitionTableInfo(PartitionTableScheme.Mbr, 0, []),
            0,
            "test",
            "Test provider"),
        "non-positive sector size is rejected.");

    var nonPartitionRegistry = new ProviderRegistry([
        new ProviderRegistration(new FakeProvider("metadata-only", [".img"]), Priority: 100)
    ]);
    var nonPartitionService = new PartitionIntelligenceService(nonPartitionRegistry);
    await ExpectThrowsAsync<NotSupportedException>(
        () => nonPartitionService.AnalyzeAsync(imagePath),
        "recognized providers without PartitionTable do not gain fake partition intelligence.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => service.AnalyzeAsync(imagePath, cts.Token),
        "partition intelligence preserves cancellation as a hard stop.");

    Console.WriteLine("Dragon DiskForge partition-intelligence smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void ExpectThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
        throw new InvalidOperationException(message);
    }
    catch (TException)
    {
    }
}

static async Task ExpectThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
        throw new InvalidOperationException(message);
    }
    catch (TException)
    {
    }
}

static async Task ExpectCanceledAsync(Func<Task> action, string message)
{
    try
    {
        await action();
        throw new InvalidOperationException(message);
    }
    catch (OperationCanceledException)
    {
    }
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
            "fake",
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
            "fake-partition-provider",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true));
    }
}
