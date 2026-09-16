using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

const int Sector = 2048;
var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-BootIntel-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    await HybridWindows();
    await LinuxCasperWithoutCatalog();
    await RejectBadChecksum();
    await RejectBootRangeOutsideImage();
    await RejectProviderWithoutDirectBrowse();
    await PreserveCancellation();
    Console.WriteLine("Dragon DiskForge boot/installer intelligence smoke tests passed.");
}
finally { try { Directory.Delete(root, true); } catch { } }

async Task HybridWindows()
{
    var path = await Image("windows.iso", catalog: true, badChecksum: false, biosLba: 30);
    var provider = new FakeBrowse(new Dictionary<string, IReadOnlyList<ExplorerEntry>>(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = [F("setup.exe", "/setup.exe"), D("sources", "/sources"), D("efi", "/efi")],
        ["/sources"] = [F("boot.wim", "/sources/boot.wim"), F("install.wim", "/sources/install.wim")],
        ["/efi"] = [D("boot", "/efi/boot")],
        ["/efi/boot"] = [F("bootx64.efi", "/efi/boot/bootx64.efi")]
    });
    var info = await Analyze(path, provider);
    Expect(info.HasElToritoCatalog && info.BootCatalogLba == 20, "El Torito catalog must be detected.");
    Expect(info.IsBootable && info.SupportsBiosBoot && info.SupportsUefiBoot, "Hybrid BIOS/UEFI evidence must come from catalog entries.");
    Expect(info.BootEntries.Count == 2 && info.BootEntries.All(x => x.Bootable), "Default and EFI entries must be preserved.");
    Expect(info.ArchitectureHints.SequenceEqual(["x86_64"]), "EFI fallback path must yield x86_64 hint.");
    var installer = info.Installers.Single(x => x.Family == InstallerFamily.Windows);
    Expect(installer.ArchitectureHint == "x86_64", "Single architecture hint must flow to Windows evidence.");
    Expect(installer.EvidencePaths.Contains("sources/boot.wim") && installer.EvidencePaths.Contains("sources/install.wim"), "Windows evidence must include boot and install payloads.");
}

async Task LinuxCasperWithoutCatalog()
{
    var path = await Image("linux.iso", catalog: false, badChecksum: false, biosLba: 30);
    var provider = new FakeBrowse(new Dictionary<string, IReadOnlyList<ExplorerEntry>>(StringComparer.OrdinalIgnoreCase)
    {
        ["/"] = [D("casper", "/casper"), D("efi", "/efi")],
        ["/casper"] = [F("vmlinuz", "/casper/vmlinuz"), F("initrd", "/casper/initrd"), F("filesystem.squashfs", "/casper/filesystem.squashfs")],
        ["/efi"] = [D("boot", "/efi/boot")],
        ["/efi/boot"] = [F("bootaa64.efi", "/efi/boot/bootaa64.efi")]
    });
    var info = await Analyze(path, provider);
    Expect(!info.HasElToritoCatalog && !info.IsBootable, "Filesystem markers must never fabricate bootability.");
    Expect(info.ArchitectureHints.SequenceEqual(["ARM64"]), "EFI fallback path must yield ARM64 hint.");
    Expect(info.Installers.Any(x => x.Family == InstallerFamily.Linux && x.Variant.Contains("casper", StringComparison.OrdinalIgnoreCase)), "casper marker trio must be recognized.");
}

async Task RejectBadChecksum()
{
    var path = await Image("bad-checksum.iso", catalog: true, badChecksum: true, biosLba: 30);
    await Throws<InvalidDataException>(() => Analyze(path, new FakeBrowse(Empty())), "Bad El Torito checksum must fail closed.");
}

async Task RejectBootRangeOutsideImage()
{
    var path = await Image("bad-range.iso", catalog: true, badChecksum: false, biosLba: 100);
    await Throws<InvalidDataException>(() => Analyze(path, new FakeBrowse(Empty())), "Out-of-file boot load range must fail closed.");
}

async Task RejectProviderWithoutDirectBrowse()
{
    var path = await Image("inspect-only.iso", catalog: false, badChecksum: false, biosLba: 30);
    var service = new BootInstallerIntelligenceService(new ProviderRegistry([new InspectOnly()]));
    await Throws<NotSupportedException>(() => service.AnalyzeAsync(path), "DirectBrowse capability is required.");
}

async Task PreserveCancellation()
{
    var path = await Image("cancel.iso", catalog: false, badChecksum: false, biosLba: 30);
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    try { await Analyze(path, new FakeBrowse(Empty()), cts.Token); }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Cancellation must remain a hard stop.");
}

async Task<BootInstallerIntelligenceInfo> Analyze(string path, IDiskImageProvider provider, CancellationToken token = default)
    => await new BootInstallerIntelligenceService(new ProviderRegistry([provider])).AnalyzeAsync(path, token);

Dictionary<string, IReadOnlyList<ExplorerEntry>> Empty() => new(StringComparer.OrdinalIgnoreCase) { ["/"] = [] };
ExplorerEntry F(string name, string path) => new(name, path, ExplorerEntryKind.File, 1, DateTimeOffset.UnixEpoch);
ExplorerEntry D(string name, string path) => new(name, path, ExplorerEntryKind.Directory, null, DateTimeOffset.UnixEpoch);

async Task<string> Image(string name, bool catalog, bool badChecksum, uint biosLba)
{
    var path = Path.Combine(root, name);
    await using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    stream.SetLength(100L * Sector);
    if (catalog)
    {
        var boot = Descriptor(0); WriteAscii(boot, 7, "EL TORITO SPECIFICATION"); BinaryPrimitives.WriteUInt32LittleEndian(boot.AsSpan(71, 4), 20); await Put(stream, 16, boot);
        await Put(stream, 17, Descriptor(1));
        await Put(stream, 18, Descriptor(255));
        var bytes = Catalog(biosLba); if (badChecksum) bytes[5] ^= 0x7F; await Put(stream, 20, bytes);
    }
    else
    {
        await Put(stream, 16, Descriptor(1));
        await Put(stream, 17, Descriptor(255));
    }
    await stream.FlushAsync();
    return path;
}

byte[] Descriptor(byte type)
{
    var b = new byte[Sector]; b[0] = type; WriteAscii(b, 1, "CD001"); b[6] = 1; return b;
}

byte[] Catalog(uint biosLba)
{
    var b = new byte[Sector];
    b[0] = 1; b[1] = 0; WriteAscii(b, 4, "DRAGON DISKFORGE"); b[30] = 0x55; b[31] = 0xAA;
    BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(28, 2), 0);
    uint sum = 0; for (var i = 0; i < 32; i += 2) sum += BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i, 2));
    BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(28, 2), unchecked((ushort)(0 - (ushort)sum)));
    b[32] = 0x88; BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(38, 2), 4); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(40, 4), biosLba);
    b[64] = 0x91; b[65] = 0xEF; BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(66, 2), 1); WriteAscii(b, 68, "UEFI");
    b[96] = 0x88; BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(102, 2), 4); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(104, 4), 40);
    return b;
}

async Task Put(FileStream stream, int lba, byte[] bytes) { stream.Seek((long)lba * Sector, SeekOrigin.Begin); await stream.WriteAsync(bytes); }
void WriteAscii(byte[] target, int offset, string value) => Encoding.ASCII.GetBytes(value).CopyTo(target.AsSpan(offset));
void Expect(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
async Task Throws<T>(Func<Task> action, string message) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException(message); }

sealed class FakeBrowse(IReadOnlyDictionary<string, IReadOnlyList<ExplorerEntry>> tree) : IDirectBrowseProvider
{
    public string Id => "fake-direct-iso";
    public string DisplayName => "Fake direct ISO";
    public IReadOnlyCollection<string> Extensions { get; } = [".iso"];
    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(File.Exists(path));
    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var f = new FileInfo(path); return ValueTask.FromResult(new DiskImageInfo(f.FullName, f.Name, "ISO", f.Length, "fake", true, false, false, true));
    }
    public Task<IReadOnlyList<ExplorerEntry>> ListAsync(string imagePath, string directoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Norm(directoryPath); return Task.FromResult(tree.TryGetValue(key, out var entries) ? entries : (IReadOnlyList<ExplorerEntry>)Array.Empty<ExplorerEntry>());
    }
    public Task<IReadOnlyList<ExplorerEntry>> SearchAsync(string imagePath, string startPath, string query, int maxResults = 200, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExplorerEntry>>(tree.Values.SelectMany(x => x).Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(maxResults).ToArray());
    public Task CopyOutAsync(string imagePath, string sourcePath, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    static string Norm(string path) { var s = path.Replace('\\', '/'); if (!s.StartsWith('/')) s = "/" + s; s = s.TrimEnd('/'); return s.Length == 0 ? "/" : s; }
}

sealed class InspectOnly : IDiskImageProvider
{
    public string Id => "inspect-only";
    public IReadOnlyCollection<string> Extensions { get; } = [".iso"];
    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default) => ValueTask.FromResult(File.Exists(path));
    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        var f = new FileInfo(path); return ValueTask.FromResult(new DiskImageInfo(f.FullName, f.Name, "ISO", f.Length, "inspect-only", false, false, false, true));
    }
}
