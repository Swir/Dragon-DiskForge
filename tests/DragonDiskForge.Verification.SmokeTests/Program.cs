using System.Security.Cryptography;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-Verify-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var verifier = new ImageVerificationService();
    var payload = new byte[(2 * 1024 * 1024) + 257];
    for (var i = 0; i < payload.Length; i++)
        payload[i] = unchecked((byte)(i * 31 + 17));

    var path = Path.Combine(root, "payload.img");
    await File.WriteAllBytesAsync(path, payload);

    var progress = new CaptureProgress();
    var result = await verifier.ComputeAsync(path, progress);
    Require(result.Sha256 == Convert.ToHexString(SHA256.HashData(payload)),
        "Combined verification should return the expected SHA-256 digest.");
    Require(result.Sha512 == Convert.ToHexString(SHA512.HashData(payload)),
        "Combined verification should return the expected SHA-512 digest.");
    Require(result.SizeBytes == payload.LongLength,
        "Combined verification should preserve the exact number of hashed bytes.");
    Require(progress.First <= 0.001d && progress.Last >= 0.999d,
        "Combined verification should report bounded start/completion progress.");
    Require(progress.Values.All(value => value is >= 0d and <= 1d),
        "Combined verification progress should remain in the 0..1 range.");
    Require(IsMonotonic(progress.Values),
        "Combined verification progress should never move backwards.");

    var sha256Only = await verifier.ComputeSha256Async(path);
    var sha512Only = await verifier.ComputeSha512Async(path);
    Require(sha256Only == result.Sha256,
        "The compatibility SHA-256 API should match combined verification.");
    Require(sha512Only == result.Sha512,
        "The dedicated SHA-512 API should match combined verification.");

    var empty = Path.Combine(root, "empty.img");
    await File.WriteAllBytesAsync(empty, Array.Empty<byte>());
    var emptyResult = await verifier.ComputeAsync(empty);
    Require(emptyResult.Sha256 == Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())),
        "Combined verification should hash an empty image correctly with SHA-256.");
    Require(emptyResult.Sha512 == Convert.ToHexString(SHA512.HashData(Array.Empty<byte>())),
        "Combined verification should hash an empty image correctly with SHA-512.");
    Require(emptyResult.SizeBytes == 0,
        "Empty verification should report zero hashed bytes.");

    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        await ExpectCanceledAsync(
            () => verifier.ComputeAsync(path, cancellationToken: cts.Token),
            "Combined verification must propagate pre-cancellation.");
    }

    await ExpectThrowsAsync<FileNotFoundException>(
        () => verifier.ComputeAsync(Path.Combine(root, "missing.img")),
        "Combined verification must fail clearly for a missing image.");

    Console.WriteLine("Dragon DiskForge SHA-256/SHA-512 verification smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static bool IsMonotonic(IReadOnlyList<double> values)
{
    for (var i = 1; i < values.Count; i++)
    {
        if (values[i] + double.Epsilon < values[i - 1])
            return false;
    }

    return true;
}

static async Task ExpectCanceledAsync(Func<Task> action, string message)
{
    try
    {
        await action();
    }
    catch (OperationCanceledException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static async Task ExpectThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

file sealed class CaptureProgress : IProgress<double>
{
    public List<double> Values { get; } = [];
    public double First => Values.Count == 0 ? double.NaN : Values[0];
    public double Last => Values.Count == 0 ? double.NaN : Values[^1];

    public void Report(double value) => Values.Add(value);
}
