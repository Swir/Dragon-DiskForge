using System.Buffers.Binary;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

const int SectorSize = 2048;
var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-BootInstaller-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    await ValidateHybridWindowsAsync(root);
    await ValidateLinuxCasperAsync(root);
    await ValidateInvalidCatalogChecksumAsync(root);
    await ValidateOutOfBoundsBootImageAsync(root);
    await ValidateDirectBrowseGateAsync(root);
    await ValidateCancellationAsync(root);

    Console.WriteLine("Dragon DiskForge boot/installer intelligence smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task ValidateHybridWindowsAsync(string root)
{
    var path = await CreateIsoSkeletonAsync(root, "windows-hybrid.iso", includeCatalog: true, invalidChecksum: false, bootImageLba: 30);
    var tree = new Dictionary<string, IReadOnlyList<ExplorerEntry>>(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] =
        [
            File("setup.exe", "/setup.exe"),
            Dir("sources", "/sources"),
            Dir("efi", "/efi")
        ],
        ["/sources"] =
        [
            File("boot.wim", "/sources/boot.wim"),
            File("install.wim", "/sources/install.wim")
        ],
        ["/efi"] = [Dir("boot", "/efi/boot")],
        ["/efi/boot"] = [File("bootx64.efi", "/efi/boot/bootx64.efi")]
    };

    var service = CreateService(path, tree);
    var info = await service.AnalyzeAsync(path);

    Expect(info.HasElToritoCatalog && info.BootCatalogLba == 20, "El Torito catalog is found and bounded.");
    Expect(info.IsBootable, "hybrid image is reported bootable from catalog evidence.");
    Expect(info.SupportsBiosBoot, "x86 BIOS boot entry is detected.");
    Expect(info.SupportsUefiBoot, "EFI boot entry is detected.");
    Expect(info.BootEntries.Count == 2, "default BIOS + EFI section boot entries are preserved.");
    Expect(info.BootEntries.All(x => x.Bootable), "both synthetic catalog entries are bootable.");
    Expect(info.ArchitectureHints.SequenceEqual(["x86_64"]), "EFI fallback filename yields only a bounded x86_64 architecture hint.");

    var windows = info.Installers.SingleOrDefault(x => x.Family == InstallerFamily.Windows);
    Expect(windows is not null, "Windows installer media is recognized from required file markers.");
    Expect(windows!.ArchitectureHint == "x86_64", "single EFI architecture hint is attached to installer evidence.");
    Expect(windows.EvidencePaths.Contains("sources/boot.wim") && windows.EvidencePaths.Contains("sources/install.wim"),
        "Windows installer evidence keeps the decisive WIM paths.");
}

static async Task ValidateLinuxCasperAsync(string root)
{
    var path = await CreateIsoSkeletonAsync(root, "linux-casper.iso", includeCatalog: false, invalidChecksum: false, bootImageLba: 30);
    var tree = new Dictionary<string, IReadOnlyList<ExplorerEntry>>(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = [Dir("casper", "/casper"), Dir("efi", "/efi")],
        ["/casper"] =
        [
            File("vmlinuz", "/casper/vmlinuz"),
            File("initrd", "/casper/initrd"),
            File("filesystem.squashfs", "/casper/filesystem.squashfs")
        ],
        ["/efi"] = [Dir("boot", "/efi/boot")],
        ["/efi/boot"] = [File("bootaa64.efi", "/efi/boot/bootaa64.efi")]
    };

    var info = await CreateService(path, tree).AnalyzeAsync(path);
    Expect(!info.HasElToritoCatalog && !info.IsBootable,
        "filesystem markers alone never fabricate El Torito bootability.");
    Expect(info.ArchitectureHints.SequenceEqual(["ARM64"]), "ARM64 EFI fallback filename is surfaced as a hint.");
    var linux = info.Installers.SingleOrDefault(x => x.Family == InstallerFamily.Linux);
    Expect(linux is not null && linux.Variant.Contains("casper", StringComparison.OrdinalIgnoreCase),
        "casper kernel/initrd/squashfs trio is recognized as Linux live/install media.");
}

static async Task ValidateInvalidCatalogChecksumAsync(string root)
{
    var path = await CreateIsoSkeletonAsync(root, "bad-checksum.iso", includeCatalog: true, invalidChecksum: true, bootImageLba: 30);
    await ExpectThrowsAsync<InvalidDataException>(
        () => CreateService(path, EmptyTree()).AnalyzeAsync(path),
        "invalid El Torito validation checksum is rejected rather than guessed.");
}

static async Task ValidateOutOfBoundsBootImageAsync(string root)
{
    var path = await CreateIsoSkeletonAsync(root, "bad-range.iso", includeCatalog: true, invalidChecksum: false, bootImageLba: 95);
    await ExpectThrowsAsync<InvalidDataException>(
        () => CreateService(path, EmptyTree()).AnalyzeAsync(path),
        "El Torito load range outside the physical image is rejected.");
}

static async Task ValidateDirectBrowseGateAsync(string root)
{
    var path = await CreateIsoSkeletonAsync(root, "no-direct.iso", includeCatalog: false, invalidChecksum: false, bootImageLba: 30);
    var provider = new InspectOnlyProvider();
    var service = new BootInstallerIntelligenceService(new ProviderRegistry([provider]));
    await ExpectThrowsAsync<NotSupportedException>(
        () => service.AnalyzeAsync(path),
        "boot/installer intelligence requires truthful DirectBrowse support.");
}

static async Task ValidateCancellationAsync(string root)
{
    var path = await CreateIsoSkeletonAsync(root, "cancel.iso", includeCatalog: false, invalidChecksum: false, bootImageLba: 30);
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    await ExpectCanceledAsync(
        () => CreateService(path, EmptyTree()).AnalyzeAsync(path, cts.Token),
        "pre-cancelled analysis remains a hard stop.");
}

static BootInstallerIntelligenceService CreateService(
    string path,
    IReadOnlyDictionary<string, IReadOnlyList<ExplorerEntry>> tree)
{
    var provider = new FakeDirectBrowseProvider(tree);
    return new BootInstallerIntelligenceService(new ProviderRegistry([provider]));
}

static IReadOnlyDictionary<string, IReadOnlyList<ExplorerEntry>> EmptyTree()
    => new Dictionary<string, IReadOnlyList<ExplorerEntry>>(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = Array.Empty<ExplorerEntry>()
    };

static async Task<string> CreateIsoSkeletonAsync(
    string root,
    string name,
    bool includeCatalog,
    bool invalidChecksum,
    uint bootImageLba)
{
    var path = Path.Combine(root, name);
    await using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    stream.SetLength(100L * SectorSize);

    if (includeCatalog)
    {
        var bootRecord = new byte[SectorSize];
        bootRecord[0] = 0;
        WriteAscii(bootRecord, 1, "CD001");
        bootRecord[6] = 1;
        WriteAscii(bootRecord, 7, "EL TORITO SPECIFICATION");
        BinaryPrimitives.WriteUInt32LittleEndian(bootRecord.AsSpan(71, 4), 20);
        await WriteSectorAsync(stream, 16, bootRecord);

        var primary = new byte[SectorSize];
        primary[0] = 1;
        WriteAscii(primary, 1, "CD001");
        primary[6] = 1;
        await WriteSectorAsync(stream, 17, primary);

        var terminator = new byte[SectorSize];
        terminator[0] = 255;
        WriteAscii(terminator, 1, "CD001");
        terminator[6] = 1;
        await WriteSectorAsync(stream, 18, terminator);

        var catalog = BuildBootCatalog(bootImageLba);
        if (invalidChecksum)
            catalog[5] ^= 0x7F;
        await WriteSectorAsync(stream, 20, catalog);
    }
    else
    {
        var primary = new byte[SectorSize];
        primary[0] = 1;
        WriteAscii(primary, 1, "CD001");
        primary[6] = 1;
        await WriteSectorAsync(stream, 16, primary);

        var terminator = new byte[SectorSize];
        terminator[0] = 255;
        WriteAscii(terminator, 1, "CD001");
        terminator[6] = 1;
        await WriteSectorAsync(stream, 17, terminator);
    }

    await stream.FlushAsync();
    return path;
}

static byte[] BuildBootCatalog(uint biosImageLba)
{
    var catalog = new byte[SectorSize];
    catalog[0] = 0x01;
    catalog[1] = 0x00;
    WriteAscii(catalog, 4, "DRAGON DISKFORGE");
    catalog[30] = 0x55;
    catalog[31] = 0xAA;
    WriteValidationChecksum(catalog);

    catalog[32] = 0x88;
    catalog[33] = 0x00;
    BinaryPrimitives.WriteUInt16LittleEndian(catalog.AsSpan(38, 2), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(40, 4), biosImageLba);

    catalog[64] = 0x91;
    catalog[65] = 0xEF;
    BinaryPrimitives.WriteUInt16LittleEndian(catalog.AsSpan(66, 2), 1);
    WriteAscii(catalog, 68, "UEFI");

    catalog[96] = 0x88;
    catalog[97] = 0x00;
    BinaryPrimitives.WriteUInt16LittleEndian(catalog.AsSpan(102, 2), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(catalog.AsSpan(104, 4), 40);
    return catalog;
}

static void WriteValidationChecksum(byte[] catalog)
{
    BinaryPrimitives.WriteUInt16LittleEndian(catalog.AsSpan(28, 2), 0);
    uint sum = 0;
    for (var offset = 0; offset < 32; offset += 2)
        sum += BinaryPrimitives.ReadUInt16LittleEndian(catalog.AsSpan(offset, 2));
    var correction = unchecked((ushort)(0 - (ushort)sum));
    BinaryPrimitives.WriteUInt16LittleEndian(catalog.AsSpan(28, 2), correction);
}

static async Task WriteSectorAsync(FileStream stream, int lba, byte[] bytes)
{
    stream.Seek((long)lba * SectorSize, SeekOrigin.Begin);
    await stream.WriteAsync(bytes);
}

static ExplorerEntry File(string name, string path)
    => new(name, path, ExplorerEntryKind.File, 1, DateTimeOffset.UnixEpoch);

static ExplorerEntry Dir(string name, string path)
    => new(name, path, ExplorerEntryKind.Directory, null, DateTimeOffset.UnixEpoch);

static void WriteAscii(byte[] target, int offset, string value)
    => System.Text.Encoding.ASCII.GetBytes(value).CopyTo(target.AsSpan(offset));

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task ExpectThrowsAsync<T>(Func<Task> action, string message) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static async Task ExpectCanceledAsync(Func<Task> action, string message)
{
    try
    {
        await action();
    }
    catch (OperationCanceledException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

sealed class FakeDirectBrowseProvider : IDirectBrowseProvider
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ExplorerEntry>> _tree;

    public FakeDirectBrowseProvider(IReadOnlyDictionary<string, IReadOnlyList<ExplorerEntry>> tree)
    {
        _tree = tree;
    }

    public string Id => "fake-direct-iso";
    public string DisplayName => "Fake direct ISO";
    public IReadOnlyCollection<string> Extensions { get; } = [".iso"];

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(File.Exists(path));

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName, file.Name, "ISO", file.Length, "fake", true, false, false, true));
    }

    public Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string imagePath,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Normalize(directoryPath);
        return Task.FromResult(_tree.TryGetValue(key, out var entries)
            ? entries
            : (IReadOnlyList<ExplorerEntry>)Array.Empty<ExplorerEntry>());
    }

    public async Task<IReadOnlyList<ExplorerEntry>> SearchAsync(
        string imagePath,
        string startPath,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        return _tree.Values.SelectMany(x => x)
            .Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToArray();
    }

    public Task CopyOutAsync(
        string imagePath,
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        return normalized.TrimEnd('/') is { Length: > 0 } value ? value : "/";
    }
}

sealed class InspectOnlyProvider : IDiskImageProvider
{
    public string Id => "inspect-only";
    public IReadOnlyCollection<string> Extensions { get; } = [".iso"];

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(File.Exists(path));

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName, file.Name, "ISO", file.Length, "inspect-only", false, false, false, true));
    }
}
