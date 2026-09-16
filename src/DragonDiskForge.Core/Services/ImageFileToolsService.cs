using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace DragonDiskForge.Core.Services;

public sealed record ImagePart(string Name, long Length, string Sha256);
public sealed record SplitImageManifest(int Version, string OriginalName, long Length, List<ImagePart> Parts);

public sealed class ImageFileToolsService
{
    private const int BufferSize = 1024 * 1024;
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? 0xEDB88320u : 0);
            table[i] = value;
        }
        return table;
    }

    public Task CreateRawAsync(string destination, long size, CancellationToken token = default)
    {
        if (size <= 0 || size % 512 != 0) throw new ArgumentOutOfRangeException(nameof(size), "Size must be a positive multiple of 512 bytes.");
        return AtomicFileOutput.WriteAsync(destination, stream => { token.ThrowIfCancellationRequested(); stream.SetLength(size); return Task.CompletedTask; }, token);
    }

    public Task CompressAsync(string source, string destination, IProgress<double>? progress = null, CancellationToken token = default)
        => AtomicFileOutput.WriteAsync(destination, async output =>
        {
            await using var input = OpenInput(source);
            await using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
            await CopyAsync(input, gzip, input.Length, progress, token);
        }, token);

    public Task DecompressAsync(string source, string destination, long maximumOutputBytes,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (maximumOutputBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
        return AtomicFileOutput.WriteAsync(destination, async output =>
        {
            await using var input = OpenInput(source);
            if (input.Length < 18) throw new InvalidDataException("Truncated GZip image.");
            var trailer = new byte[8];
            input.Seek(-8, SeekOrigin.End);
            await input.ReadExactlyAsync(trailer, token);
            input.Position = 0;
            var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(trailer);
            var expectedSize = BinaryPrimitives.ReadUInt32LittleEndian(trailer.AsSpan(4));
            uint crc = uint.MaxValue;
            await using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
            var buffer = new byte[BufferSize];
            long written = 0;
            int count;
            while ((count = await gzip.ReadAsync(buffer, token)) != 0)
            {
                if (count > maximumOutputBytes - written) throw new InvalidDataException("Decompressed image exceeds the configured output limit.");
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                written += count;
                for (var i = 0; i < count; i++) crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
                progress?.Report(Math.Min(1d, (double)input.Position / Math.Max(1, input.Length)));
            }
            if (~crc != expectedCrc || unchecked((uint)written) != expectedSize)
                throw new InvalidDataException("Incomplete GZip image, checksum mismatch, or unsupported multiple-member archive.");
            progress?.Report(1);
        }, token);
    }

    public async Task<string> SplitAsync(string source, string destinationDirectory, long partSize,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (partSize <= 0) throw new ArgumentOutOfRangeException(nameof(partSize));
        var destination = Path.GetFullPath(destinationDirectory);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new folder for the split image.");
        var parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        var stage = Path.Combine(parent, $".split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            await using var input = OpenInput(source);
            if ((input.Length - 1) / partSize >= 10000) throw new ArgumentOutOfRangeException(nameof(partSize), "At most 10,000 parts are supported.");
            var manifest = new SplitImageManifest(1, Path.GetFileName(source), input.Length, []);
            var buffer = new byte[BufferSize];
            long processed = 0;
            do
            {
                token.ThrowIfCancellationRequested();
                var name = $"part-{manifest.Parts.Count + 1:D5}.ddfpart";
                long partLength = 0;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = new FileStream(Path.Combine(stage, name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    while (partLength < partSize)
                    {
                        var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, partSize - partLength)), token);
                        if (count == 0) break;
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                        hash.AppendData(buffer, 0, count);
                        processed += count;
                        partLength += count;
                        progress?.Report((double)processed / Math.Max(1, input.Length));
                    }
                    await output.FlushAsync(token);
                }
                manifest.Parts.Add(new(name, partLength, Convert.ToHexString(hash.GetHashAndReset())));
            } while (processed < input.Length);
            await File.WriteAllTextAsync(Path.Combine(stage, "image.ddfparts.json"),
                JsonSerializer.Serialize(manifest, ImageReportService.JsonOptions), token);
            token.ThrowIfCancellationRequested();
            Directory.Move(stage, destination);
            progress?.Report(1);
            return Path.Combine(destination, "image.ddfparts.json");
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }

    public async Task JoinAsync(string manifestPath, string destination, IProgress<double>? progress = null, CancellationToken token = default)
    {
        if (new FileInfo(manifestPath).Length > 4 * 1024 * 1024) throw new InvalidDataException("Split manifest is too large.");
        var manifest = JsonSerializer.Deserialize<SplitImageManifest>(await File.ReadAllTextAsync(manifestPath, token), ImageReportService.JsonOptions)
            ?? throw new InvalidDataException("Invalid split manifest.");
        if (manifest.Version != 1 || manifest.Length < 0 || manifest.Parts is null || manifest.Parts.Count is < 1 or > 10000)
            throw new InvalidDataException("Unsupported split manifest.");
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long sum = 0;
        foreach (var part in manifest.Parts)
        {
            if (part is null || string.IsNullOrWhiteSpace(part.Name) || part.Name != Path.GetFileName(part.Name)
                || part.Name.Contains(':') || part.Name.IndexOfAny(['/', '\\']) >= 0 || !names.Add(part.Name)
                || part.Length < 0 || part.Sha256 is null || part.Sha256.Length != 64 || !part.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Unsafe or invalid split part.");
            sum = checked(sum + part.Length);
            var info = new FileInfo(Path.Combine(root, part.Name));
            if (!info.Exists || info.Length != part.Length || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Missing or modified part: {part.Name}");
        }
        if (sum != manifest.Length) throw new InvalidDataException("Part lengths do not match the manifest.");
        await AtomicFileOutput.WriteAsync(destination, async output =>
        {
            var buffer = new byte[BufferSize];
            long processed = 0;
            foreach (var part in manifest.Parts)
            {
                await using var input = OpenInput(Path.Combine(root, part.Name));
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long countRead = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) != 0)
                {
                    countRead += count;
                    if (countRead > part.Length) throw new InvalidDataException("Split part changed during reading.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    processed += count;
                    progress?.Report((double)processed / Math.Max(1, manifest.Length));
                }
                if (countRead != part.Length || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), part.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SHA-256 mismatch: {part.Name}");
            }
            progress?.Report(1);
        }, token);
    }

    private static FileStream OpenInput(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task CopyAsync(Stream input, Stream output, long length, IProgress<double>? progress, CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        long processed = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            processed += count;
            progress?.Report((double)processed / Math.Max(1, length));
        }
        progress?.Report(1);
    }
}
