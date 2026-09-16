using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Builds bounded, read-only boot and installer intelligence from a direct-browse physical image.
/// The current implementation uses ISO9660/Joliet direct-browse evidence plus a validated El Torito
/// boot catalog. It never mounts, extracts, executes or writes image content.
/// </summary>
public sealed class BootInstallerIntelligenceService
{
    private const int IsoSectorSize = 2048;
    private const int FirstVolumeDescriptorLba = 16;
    private const int LastVolumeDescriptorLba = 63;
    private const int MaxBootCatalogBytes = 64 * 1024;
    private const int MaxBrowseDirectories = 4096;
    private const int MaxBrowseEntries = 50_000;
    private const int MaxVirtualDepth = 64;

    private readonly ProviderRegistry _registry;

    public BootInstallerIntelligenceService(ProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<BootInstallerIntelligenceInfo> AnalyzeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Disk image was not found.", fullPath);

        var resolution = await _registry.ResolveAsync(fullPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolution.Provider is not IDirectBrowseProvider browseProvider
            || resolution.Descriptor is null
            || !resolution.Descriptor.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse))
        {
            throw new NotSupportedException(
                "Boot/installer intelligence currently requires a provider with truthful DirectBrowse support.");
        }

        var bootCatalog = await ReadElToritoAsync(fullPath, file.Length, cancellationToken);
        var paths = await SnapshotPathsAsync(browseProvider, fullPath, cancellationToken);
        var architectures = DetectArchitectureHints(paths);
        var installers = DetectInstallers(paths, architectures);

        return new BootInstallerIntelligenceInfo(
            resolution.Descriptor.Id,
            resolution.Descriptor.DisplayName,
            file.Length,
            bootCatalog.CatalogLba is not null,
            bootCatalog.CatalogLba,
            Array.AsReadOnly(bootCatalog.Entries.ToArray()),
            Array.AsReadOnly(installers.ToArray()),
            Array.AsReadOnly(architectures.ToArray()));
    }

    private static async Task<(uint? CatalogLba, IReadOnlyList<BootCatalogEntryInfo> Entries)> ReadElToritoAsync(
        string path,
        long fileLength,
        CancellationToken cancellationToken)
    {
        if (fileLength < (FirstVolumeDescriptorLba + 1L) * IsoSectorSize)
            return (null, Array.Empty<BootCatalogEntryInfo>());

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        var descriptor = new byte[IsoSectorSize];
        uint? catalogLba = null;
        for (var lba = FirstVolumeDescriptorLba; lba <= LastVolumeDescriptorLba; lba++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = checked((long)lba * IsoSectorSize);
            if (offset + IsoSectorSize > fileLength)
                break;

            await ReadExactlyAtAsync(stream, offset, descriptor, cancellationToken);
            if (!descriptor.AsSpan(1, 5).SequenceEqual("CD001"u8) || descriptor[6] != 1)
                continue;

            if (descriptor[0] == 255)
                break;

            if (descriptor[0] != 0)
                continue;

            var systemId = ReadAscii(descriptor.AsSpan(7, 32));
            if (!systemId.Equals("EL TORITO SPECIFICATION", StringComparison.Ordinal))
                continue;

            var candidate = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(71, 4));
            if (candidate == 0)
                throw new InvalidDataException("El Torito boot record declares a zero boot-catalog LBA.");

            catalogLba = candidate;
            break;
        }

        if (catalogLba is null)
            return (null, Array.Empty<BootCatalogEntryInfo>());

        var catalogOffset = checked((long)catalogLba.Value * IsoSectorSize);
        if (catalogOffset < 0 || catalogOffset + 64 > fileLength)
            throw new InvalidDataException("El Torito boot catalog lies outside the physical image.");

        var available = checked((int)Math.Min(MaxBootCatalogBytes, fileLength - catalogOffset));
        var catalog = new byte[available];
        await ReadExactlyAtAsync(stream, catalogOffset, catalog, cancellationToken);

        ValidateCatalogHeader(catalog);
        var entries = new List<BootCatalogEntryInfo>();
        entries.Add(ParseBootEntry(catalog.AsSpan(32, 32), catalog[1], "Default", fileLength));

        var cursor = 64;
        while (cursor + 32 <= catalog.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var indicator = catalog[cursor];
            if (indicator == 0)
                break;

            if (indicator is not (0x90 or 0x91))
                throw new InvalidDataException($"Unexpected El Torito section-header indicator 0x{indicator:X2}.");

            var platformId = catalog[cursor + 1];
            var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(catalog.AsSpan(cursor + 2, 2));
            var sectionId = ReadAscii(catalog.AsSpan(cursor + 4, 28));
            cursor += 32;

            if (entryCount == 0)
                throw new InvalidDataException("El Torito section header declares zero entries.");
            if ((long)cursor + ((long)entryCount * 32) > catalog.Length)
                throw new InvalidDataException("El Torito section entries exceed the bounded boot-catalog window.");

            for (var index = 0; index < entryCount; index++, cursor += 32)
            {
                var entry = catalog.AsSpan(cursor, 32);
                if (entry[0] == 0x44)
                    continue; // Section Entry Extension; evidence only, not a boot image entry.
                entries.Add(ParseBootEntry(entry, platformId, sectionId, fileLength));
            }

            if (indicator == 0x91)
                break;
        }

        return (catalogLba, entries);
    }

    private static void ValidateCatalogHeader(ReadOnlySpan<byte> catalog)
    {
        if (catalog.Length < 64
            || catalog[0] != 0x01
            || catalog[30] != 0x55
            || catalog[31] != 0xAA)
        {
            throw new InvalidDataException("El Torito validation entry is malformed.");
        }

        uint checksum = 0;
        for (var offset = 0; offset < 32; offset += 2)
            checksum += BinaryPrimitives.ReadUInt16LittleEndian(catalog.Slice(offset, 2));
        if ((checksum & 0xFFFF) != 0)
            throw new InvalidDataException("El Torito validation-entry checksum is invalid.");
    }

    private static BootCatalogEntryInfo ParseBootEntry(
        ReadOnlySpan<byte> entry,
        byte platformId,
        string sectionId,
        long fileLength)
    {
        var bootIndicator = entry[0];
        if (bootIndicator is not (0x00 or 0x88))
            throw new InvalidDataException($"Invalid El Torito boot indicator 0x{bootIndicator:X2}.");

        var bootable = bootIndicator == 0x88;
        var mediaType = (entry[1] & 0x0F) switch
        {
            0 => BootMediaType.NoEmulation,
            1 => BootMediaType.Floppy120Mb,
            2 => BootMediaType.Floppy144Mb,
            3 => BootMediaType.Floppy288Mb,
            4 => BootMediaType.HardDisk,
            _ => BootMediaType.Unknown
        };
        var loadSegment = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(2, 2));
        var systemType = entry[4];
        var sectorCount = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(6, 2));
        var imageLba = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));

        if (bootable && (sectorCount == 0 || imageLba == 0))
            throw new InvalidDataException("Bootable El Torito entry has an empty load range.");

        if (imageLba != 0 && sectorCount != 0)
        {
            var imageOffset = checked((long)imageLba * IsoSectorSize);
            var loadBytes = checked((long)sectorCount * 512L);
            if (imageOffset < 0 || imageOffset + loadBytes > fileLength)
                throw new InvalidDataException("El Torito boot-entry load range exceeds the physical image.");
        }

        var firmware = platformId switch
        {
            0x00 => BootFirmwareKind.Bios,
            0xEF => BootFirmwareKind.Uefi,
            _ => BootFirmwareKind.Other
        };
        var platformName = platformId switch
        {
            0x00 => "x86 BIOS",
            0x01 => "PowerPC",
            0x02 => "Mac",
            0xEF => "EFI",
            _ => $"Platform 0x{platformId:X2}"
        };

        return new BootCatalogEntryInfo(
            firmware,
            platformId,
            platformName,
            bootable,
            mediaType,
            loadSegment,
            systemType,
            sectorCount,
            imageLba,
            sectionId);
    }

    private static async Task<HashSet<string>> SnapshotPathsAsync(
        IDirectBrowseProvider provider,
        string imagePath,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/" };
        var queue = new Queue<string>();
        queue.Enqueue("/");
        var directoryCount = 0;
        var entryCount = 0;

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++directoryCount > MaxBrowseDirectories)
                throw new InvalidDataException("Direct-browse tree exceeds the bounded directory limit for installer intelligence.");

            var directory = queue.Dequeue();
            var entries = await provider.ListAsync(imagePath, directory, cancellationToken);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entryCount > MaxBrowseEntries)
                    throw new InvalidDataException("Direct-browse tree exceeds the bounded entry limit for installer intelligence.");

                var normalized = NormalizeVirtualPath(entry.FullPath);
                if (normalized.Length == 0)
                    continue;
                paths.Add(normalized);

                if (!entry.IsDirectory || entry.IsReparsePoint)
                    continue;
                if (normalized.Count(ch => ch == '/') >= MaxVirtualDepth)
                    throw new InvalidDataException("Direct-browse tree exceeds the bounded virtual depth for installer intelligence.");

                var providerPath = "/" + normalized;
                if (visitedDirectories.Add(providerPath))
                    queue.Enqueue(providerPath);
            }
        }

        return paths;
    }

    private static IReadOnlyList<string> DetectArchitectureHints(HashSet<string> paths)
    {
        var hints = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        AddArchitecture(paths, "efi/boot/bootx64.efi", "x86_64");
        AddArchitecture(paths, "efi/boot/bootia32.efi", "x86");
        AddArchitecture(paths, "efi/boot/bootaa64.efi", "ARM64");
        AddArchitecture(paths, "efi/boot/bootarm.efi", "ARM");
        AddArchitecture(paths, "efi/boot/bootriscv64.efi", "RISC-V 64");
        return hints.ToArray();

        void AddArchitecture(HashSet<string> snapshot, string path, string architecture)
        {
            if (snapshot.Contains(path))
                hints.Add(architecture);
        }
    }

    private static IReadOnlyList<InstallerDetectionInfo> DetectInstallers(
        HashSet<string> paths,
        IReadOnlyList<string> architectureHints)
    {
        var detections = new List<InstallerDetectionInfo>();
        var architecture = architectureHints.Count == 1 ? architectureHints[0] : string.Empty;

        var windowsPayload = FirstExisting(paths, "sources/install.wim", "sources/install.esd", "sources/install.swm");
        if (paths.Contains("setup.exe") && paths.Contains("sources/boot.wim") && windowsPayload is not null)
        {
            detections.Add(new InstallerDetectionInfo(
                InstallerFamily.Windows,
                "Windows installation media",
                architecture,
                Array.AsReadOnly(new[] { "setup.exe", "sources/boot.wim", windowsPayload })));
        }

        var casperInitrd = paths.FirstOrDefault(x => x.StartsWith("casper/initrd", StringComparison.OrdinalIgnoreCase));
        if (paths.Contains("casper/vmlinuz")
            && casperInitrd is not null
            && paths.Contains("casper/filesystem.squashfs"))
        {
            detections.Add(new InstallerDetectionInfo(
                InstallerFamily.Linux,
                "casper live/install media",
                architecture,
                Array.AsReadOnly(new[] { "casper/vmlinuz", casperInitrd, "casper/filesystem.squashfs" })));
        }

        var debianKernel = FirstExisting(paths, "install.amd/vmlinuz", "install.386/vmlinuz", "install.a64/vmlinuz", "install/vmlinuz");
        if (debianKernel is not null)
        {
            var directory = debianKernel[..debianKernel.LastIndexOf('/')];
            var initrd = FirstExisting(paths, directory + "/initrd.gz", directory + "/initrd");
            if (initrd is not null)
            {
                var debianArchitecture = debianKernel.StartsWith("install.amd/", StringComparison.OrdinalIgnoreCase) ? "x86_64"
                    : debianKernel.StartsWith("install.386/", StringComparison.OrdinalIgnoreCase) ? "x86"
                    : debianKernel.StartsWith("install.a64/", StringComparison.OrdinalIgnoreCase) ? "ARM64"
                    : architecture;
                detections.Add(new InstallerDetectionInfo(
                    InstallerFamily.Linux,
                    "Debian-style installer media",
                    debianArchitecture,
                    Array.AsReadOnly(new[] { debianKernel, initrd })));
            }
        }

        if (paths.Contains("images/pxeboot/vmlinuz")
            && paths.Contains("images/pxeboot/initrd.img")
            && (paths.Contains("images/install.img") || paths.Contains("liveos/squashfs.img")))
        {
            var payload = paths.Contains("images/install.img") ? "images/install.img" : "liveos/squashfs.img";
            detections.Add(new InstallerDetectionInfo(
                InstallerFamily.Linux,
                "Anaconda-style install/live media",
                architecture,
                Array.AsReadOnly(new[] { "images/pxeboot/vmlinuz", "images/pxeboot/initrd.img", payload })));
        }

        return detections;
    }

    private static string? FirstExisting(HashSet<string> paths, params string[] candidates)
        => candidates.FirstOrDefault(paths.Contains);

    private static string NormalizeVirtualPath(string value)
        => value.Replace('\\', '/').Trim('/').Trim().ToLowerInvariant();

    private static string ReadAscii(ReadOnlySpan<byte> value)
        => Encoding.ASCII.GetString(value).TrimEnd('\0', ' ');

    private static async Task ReadExactlyAtAsync(
        FileStream stream,
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(destination, cancellationToken);
    }
}
