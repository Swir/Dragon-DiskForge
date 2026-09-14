using System.Security.Cryptography;

namespace DragonDiskForge.Core.Services;

public sealed class ImageVerificationService
{
    private const int BufferSize = 1024 * 1024;

    public async Task<string> ComputeSha256Async(
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Disk image was not found.", path);

        cancellationToken.ThrowIfCancellationRequested();

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var algorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
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

            var ratio = totalLength == 0
                ? 1d
                : Math.Min(1d, (double)processed / totalLength);
            progress?.Report(ratio);
        }

        progress?.Report(1);
        return Convert.ToHexString(algorithm.GetHashAndReset());
    }
}
