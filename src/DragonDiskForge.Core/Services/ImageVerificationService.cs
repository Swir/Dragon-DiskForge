using System.Security.Cryptography;

namespace DragonDiskForge.Core.Services;

public sealed class ImageVerificationService
{
    private const int BufferSize = 1024 * 1024;

    public Task<string> ComputeSha256Async(string path, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) => ComputeHashAsync(path, "sha256", progress, cancellationToken);

    public async Task<string> ComputeHashAsync(
        string path, string algorithmName = "sha256",
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var algorithmId = algorithmName.Replace("-", "").ToUpperInvariant() switch
        {
            "SHA256" => HashAlgorithmName.SHA256,
            "SHA512" => HashAlgorithmName.SHA512,
            _ => throw new ArgumentException("Supported hashes: SHA-256 and SHA-512.", nameof(algorithmName))
        };
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

        using var algorithm = IncrementalHash.CreateHash(algorithmId);
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
