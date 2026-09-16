using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

/// <summary>
/// Conservative read-only provider for flat raw-sector disk images.
/// It intentionally does not expose partition/filesystem browsing; that arrives in milestone 0.5.
/// </summary>
public sealed class RawDiskImageProvider : IDiskImageProvider
{
    private const int LogicalSectorBytes = 512;
    private const long IsoSignatureOffset = 0x8001;
    private static readonly string[] SupportedExtensions = [".img", ".raw"];

    public string Id => "raw-disk-image";

    public IReadOnlyCollection<string> Extensions => SupportedExtensions;

    public async ValueTask<bool> CanHandleAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fullPath = Path.GetFullPath(path);
        var extension = Path.GetExtension(fullPath);
        if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return false;

        var file = new FileInfo(fullPath);
        if (!file.Exists
            || file.Length < LogicalSectorBytes
            || file.Length % LogicalSectorBytes != 0)
            return false;

        // RAW has no universal magic. Reject known structured image signatures so a renamed
        // ISO/VHD/VHDX/QCOW2/DMG/WIM can continue through the registry fallback chain.
        return !await LooksLikeKnownStructuredImageAsync(fullPath, file.Length, cancellationToken);
    }

    public async ValueTask<DiskImageInfo> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Disk image was not found.", fullPath);

        if (!await CanHandleAsync(fullPath, cancellationToken))
            throw new InvalidDataException("The file is not a supported flat IMG/RAW disk image.");

        var extension = Path.GetExtension(fullPath);
        var format = extension.Equals(".raw", StringComparison.OrdinalIgnoreCase)
            ? "RAW disk image"
            : "IMG raw disk image";

        return new DiskImageInfo(
            file.FullName,
            file.Name,
            format,
            file.Length,
            "Provider / raw extension + 512-byte alignment + structured-signature guard",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true);
    }

    private static async Task<bool> LooksLikeKnownStructuredImageAsync(
        string path,
        long length,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var headLength = (int)Math.Min(16, length);
        var head = new byte[headLength];
        if (headLength > 0)
            await stream.ReadExactlyAsync(head, cancellationToken);

        if (StartsWithAscii(head, "vhdxfile"))
            return true;
        if (StartsWithAscii(head, "MSWIM\0\0\0"))
            return true;
        if (head.Length >= 4
            && head[0] == 0x51
            && head[1] == 0x46
            && head[2] == 0x49
            && head[3] == 0xFB)
            return true;

        if (length >= IsoSignatureOffset + 5)
        {
            stream.Seek(IsoSignatureOffset, SeekOrigin.Begin);
            var iso = new byte[5];
            await stream.ReadExactlyAsync(iso, cancellationToken);
            if (Encoding.ASCII.GetString(iso) == "CD001")
                return true;
        }

        if (length >= 512)
        {
            stream.Seek(-512, SeekOrigin.End);
            var footer = new byte[512];
            await stream.ReadExactlyAsync(footer, cancellationToken);
            if (StartsWithAscii(footer, "conectix") || StartsWithAscii(footer, "koly"))
                return true;
        }

        return false;
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> data, string value)
    {
        var expected = Encoding.ASCII.GetBytes(value.Replace("\\0", "\0"));
        return data.Length >= expected.Length && data[..expected.Length].SequenceEqual(expected);
    }
}
