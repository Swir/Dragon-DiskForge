using System.Security.Cryptography;

namespace DragonDiskForge.Core.Services;

public sealed class ImageVerificationService
{
    public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Disk image was not found.", path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var algorithm = SHA256.Create();
        var hash = await algorithm.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
