using System.IO.Compression;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Transactional whole-file gzip transport compression for disk-image bytes.
/// This does not claim support for format-internal compressed clusters or block maps.
/// </summary>
public sealed class GzipImagePipelineService
{
    private const int BufferSize = 1024 * 1024;
    private const int MinimumGzipLength = 18;
    private const int FixedHeaderLength = 10;
    private const double DataStageProgressCeiling = 0.99d;

    private readonly SafeOutputService _safeOutputService;

    public GzipImagePipelineService(SafeOutputService? safeOutputService = null)
    {
        _safeOutputService = safeOutputService ?? new SafeOutputService();
    }

    public async Task<OutputCommitInfo> CompressAsync(
        string sourcePath,
        string destinationPath,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDistinctPaths(sourcePath, destinationPath);
        if (!Enum.IsDefined(compressionLevel))
            throw new ArgumentOutOfRangeException(nameof(compressionLevel));

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
            throw new FileNotFoundException("Compression source file was not found.", fullSourcePath);

        var sourceLength = checked((ulong)new FileInfo(fullSourcePath).Length);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0d);

        var result = await _safeOutputService.WriteAsync(
            destinationPath,
            async (output, token) =>
            {
                await using var input = new FileStream(
                    fullSourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: BufferSize,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var gzip = new GZipStream(output, compressionLevel, leaveOpen: true);
                var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
                ulong processed = 0;

                while (processed < sourceLength)
                {
                    token.ThrowIfCancellationRequested();
                    var chunk = checked((int)Math.Min((ulong)buffer.Length, sourceLength - processed));
                    var read = await input.ReadAsync(buffer.AsMemory(0, chunk), token);
                    if (read == 0)
                        throw new EndOfStreamException("Compression source ended before its captured length.");

                    await gzip.WriteAsync(buffer.AsMemory(0, read), token);
                    processed = checked(processed + (ulong)read);
                    progress?.Report(ToDataStageProgress(processed, sourceLength));
                }

                if (sourceLength > 0 && input.ReadByte() >= 0)
                    throw new InvalidDataException("Compression source grew after its length was captured.");

                token.ThrowIfCancellationRequested();
                await gzip.FlushAsync(token);
                progress?.Report(DataStageProgressCeiling);
            },
            overwritePolicy,
            cancellationToken);

        progress?.Report(1d);
        return result;
    }

    public async Task<OutputCommitInfo> DecompressAsync(
        string sourcePath,
        string destinationPath,
        ulong maxOutputBytes,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDistinctPaths(sourcePath, destinationPath);
        if (maxOutputBytes > long.MaxValue)
            throw new NotSupportedException("Maximum decompressed length exceeds the current output file domain.");

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
            throw new FileNotFoundException("Gzip source file was not found.", fullSourcePath);
        await ValidateGzipHeaderAsync(fullSourcePath, cancellationToken);

        var sourceLength = checked((ulong)new FileInfo(fullSourcePath).Length);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0d);
        ulong produced = 0;

        var result = await _safeOutputService.WriteAsync(
            destinationPath,
            async (output, token) =>
            {
                await using var input = new FileStream(
                    fullSourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: BufferSize,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
                var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);

                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var read = await gzip.ReadAsync(buffer.AsMemory(), token);
                    if (read == 0)
                        break;

                    var readSize = checked((ulong)read);
                    if (readSize > maxOutputBytes - produced)
                        throw new InvalidDataException("Decompressed output exceeds the caller-provided maximum size.");

                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    produced = checked(produced + readSize);

                    if (sourceLength > 0)
                    {
                        var consumed = checked((ulong)Math.Min(input.Position, checked((long)sourceLength)));
                        progress?.Report(ToDataStageProgress(consumed, sourceLength));
                    }
                }

                token.ThrowIfCancellationRequested();
                progress?.Report(DataStageProgressCeiling);
            },
            overwritePolicy,
            cancellationToken);

        if ((ulong)result.SizeBytes != produced)
            throw new InvalidDataException("Committed decompressed output length does not match the produced byte count.");

        progress?.Report(1d);
        return result;
    }

    private static async Task ValidateGzipHeaderAsync(string path, CancellationToken cancellationToken)
    {
        var fileLength = new FileInfo(path).Length;
        if (fileLength < MinimumGzipLength)
            throw new InvalidDataException("Gzip stream is shorter than the minimum header and trailer length.");

        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[FixedHeaderLength];
        var totalRead = 0;
        while (totalRead < header.Length)
        {
            var read = await input.ReadAsync(header.AsMemory(totalRead), cancellationToken);
            if (read == 0)
                throw new InvalidDataException("Gzip fixed header is truncated.");
            totalRead += read;
        }

        if (header[0] != 0x1F || header[1] != 0x8B)
            throw new InvalidDataException("Input is not a gzip stream.");
        if (header[2] != 8)
            throw new InvalidDataException("Gzip compression method is unsupported.");
        if ((header[3] & 0xE0) != 0)
            throw new InvalidDataException("Gzip header uses reserved flag bits.");
    }

    private static void ValidateDistinctPaths(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(source, destination, comparison))
            throw new IOException("Source and destination paths must be different.");
    }

    private static double ToDataStageProgress(ulong processed, ulong total)
    {
        if (total == 0)
            return DataStageProgressCeiling;
        return Math.Min(1d, (double)processed / total) * DataStageProgressCeiling;
    }
}
