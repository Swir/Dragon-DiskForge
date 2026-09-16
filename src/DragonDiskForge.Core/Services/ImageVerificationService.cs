using System.Security.Cryptography;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class ImageVerificationService
{
    private const int BufferSize = 1024 * 1024;

    public async Task<ImageVerificationInfo> ComputeAsync(
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Disk image was not found.", path);

        cancellationToken.ThrowIfCancellationRequested();

        await using var stream = OpenSequentialRead(path);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
        var totalLength = stream.Length;
        long processed = 0;

        progress?.Report(0);

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            sha256.AppendData(buffer, 0, read);
            sha512.AppendData(buffer, 0, read);
            processed += read;
            progress?.Report(ToProgress(processed, totalLength));
        }

        progress?.Report(1);
        return new ImageVerificationInfo(
            Convert.ToHexString(sha256.GetHashAndReset()),
            Convert.ToHexString(sha512.GetHashAndReset()),
            processed);
    }

    public Task<string> ComputeSha256Async(
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => ComputeSingleHashAsync(path, HashAlgorithmName.SHA256, progress, cancellationToken);

    public Task<string> ComputeSha512Async(
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
        => ComputeSingleHashAsync(path, HashAlgorithmName.SHA512, progress, cancellationToken);

    private static async Task<string> ComputeSingleHashAsync(
        string path,
        HashAlgorithmName algorithmName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Disk image was not found.", path);

        cancellationToken.ThrowIfCancellationRequested();

        await using var stream = OpenSequentialRead(path);
        using var algorithm = IncrementalHash.CreateHash(algorithmName);
        var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
        var totalLength = stream.Length;
        long processed = 0;

        progress?.Report(0);

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            algorithm.AppendData(buffer, 0, read);
            processed += read;
            progress?.Report(ToProgress(processed, totalLength));
        }

        progress?.Report(1);
        return Convert.ToHexString(algorithm.GetHashAndReset());
    }

    private static FileStream OpenSequentialRead(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static double ToProgress(long processed, long totalLength)
        => totalLength == 0
            ? 1d
            : Math.Min(1d, (double)processed / totalLength);
}
