using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class ImageDetectionService
{
    public async Task<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("Disk image was not found.", path);

        var signature = await DetectSignatureAsync(path, file.Length, cancellationToken);
        var declared = SupportedFormats.FromPath(path);
        var format = signature ?? declared?.Name ?? "Unknown image";
        var method = signature is not null ? "Signature" : declared is not null ? "Extension" : "Unknown";

        var nativeMount = format is "ISO" or "VHD" or "VHDX";


        return new DiskImageInfo(
            path,
            file.Name,
            format,
            file.Length,
            method,
            CanExplore: nativeMount,
            CanMount: nativeMount,
            CanConvert: false,
            CanVerify: true);
    }

    private static async Task<string?> DetectSignatureAsync(string path, long length, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var head = new byte[(int)Math.Min(64 * 1024, Math.Max(0, length))];
        if (head.Length > 0)
            await stream.ReadExactlyAsync(head, cancellationToken);

        if (StartsWithAscii(head, "vhdxfile")) return "VHDX";
        if (StartsWithAscii(head, "MSWIM\0\0\0")) return "WIM/ESD";
        if (head.Length >= 16 && StartsWithAscii(head.AsSpan(4), "SignedImage ")) return "FFU";
        if (head.Length >= 4 && head[0] == 0x51 && head[1] == 0x46 && head[2] == 0x49 && head[3] == 0xFB) return "QCOW/QCOW2";

        // ISO-9660 primary volume descriptor starts at sector 16 + 1 byte.
        if (length >= 0x8006)
        {
            stream.Seek(0x8001, SeekOrigin.Begin);
            var iso = new byte[5];
            await stream.ReadExactlyAsync(iso, cancellationToken);
            if (Encoding.ASCII.GetString(iso) == "CD001") return "ISO";
        }

        // Fixed/dynamic VHD footer contains the "conectix" cookie in the final 512 bytes.
        if (length >= 512)
        {
            stream.Seek(-512, SeekOrigin.End);
            var footer = new byte[512];
            await stream.ReadExactlyAsync(footer, cancellationToken);
            if (StartsWithAscii(footer, "conectix")) return "VHD";

            // Apple UDIF/DMG trailer begins with "koly".
            if (StartsWithAscii(footer, "koly")) return "DMG";
        }

        return null;
    }

    private static bool StartsWithAscii(ReadOnlySpan<byte> data, string value)
    {
        var expected = Encoding.ASCII.GetBytes(value.Replace("\\0", "\0"));
        return data.Length >= expected.Length && data[..expected.Length].SequenceEqual(expected);
    }
}
