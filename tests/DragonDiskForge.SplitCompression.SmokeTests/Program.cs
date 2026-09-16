using System.IO.Compression;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-SplitCompression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var splitService = new SplitImagePipelineService();
    var gzipService = new GzipImagePipelineService();
    var sourcePath = Path.Combine(root, "source.raw");
    var sourceBytes = CreatePattern(2_500_123);
    await File.WriteAllBytesAsync(sourcePath, sourceBytes);

    var splitProgress = new CaptureProgress();
    var splitDirectory = Path.Combine(root, "source.parts");
    var split = await splitService.SplitToDirectoryAsync(
        sourcePath,
        splitDirectory,
        partSizeBytes: 1_000_000,
        progress: splitProgress);

    Require(split.SourceSizeBytes == (ulong)sourceBytes.Length, "Split result must report the exact source size.");
    Require(split.PartCount == 3, "Split result should contain three parts for this fixture.");
    Require(split.PartSizeBytes == 1_000_000, "Split result must report the configured part size.");
    Require(File.Exists(Path.Combine(splitDirectory, SplitImagePipelineService.ManifestFileName)), "Split manifest must be published with the part set.");
    var partPaths = Directory.GetFiles(splitDirectory, "part-*.bin").OrderBy(path => path, StringComparer.Ordinal).ToArray();
    Require(partPaths.Length == 3, "Split directory should contain exactly three part files.");
    Require(new FileInfo(partPaths[0]).Length == 1_000_000, "First split part length is incorrect.");
    Require(new FileInfo(partPaths[1]).Length == 1_000_000, "Second split part length is incorrect.");
    Require(new FileInfo(partPaths[2]).Length == 500_123, "Final split part length is incorrect.");
    RequireProgress(splitProgress.Values, "Split progress should be monotonic and commit only at 1.0.");
    RequireNoStaging(root, "Successful split must not leave staging artifacts.");

    var joinProgress = new CaptureProgress();
    var joinedPath = Path.Combine(root, "joined.raw");
    var joined = await splitService.JoinFromDirectoryAsync(splitDirectory, joinedPath, progress: joinProgress);
    Require(joined.SizeBytes == sourceBytes.Length, "Join result must report the exact source size.");
    Require((await File.ReadAllBytesAsync(joinedPath)).SequenceEqual(sourceBytes), "Join must reconstruct exact original bytes.");
    RequireProgress(joinProgress.Values, "Join progress should be monotonic and commit only at 1.0.");

    var zeroSource = Path.Combine(root, "zero.raw");
    await File.WriteAllBytesAsync(zeroSource, Array.Empty<byte>());
    var zeroSet = Path.Combine(root, "zero.parts");
    var zeroSplit = await splitService.SplitToDirectoryAsync(zeroSource, zeroSet, 4096);
    Require(zeroSplit.PartCount == 1 && zeroSplit.SourceSizeBytes == 0, "Zero-length split should publish one deterministic empty part.");
    var zeroJoined = Path.Combine(root, "zero-joined.raw");
    await splitService.JoinFromDirectoryAsync(zeroSet, zeroJoined);
    Require(new FileInfo(zeroJoined).Length == 0, "Zero-length split set should join back to an empty file.");

    var conflictDirectory = Path.Combine(root, "existing.parts");
    Directory.CreateDirectory(conflictDirectory);
    await ExpectThrowsAsync<IOException>(
        () => splitService.SplitToDirectoryAsync(sourcePath, conflictDirectory, 1_000_000),
        "Split must reject an existing destination directory before mutation.");

    var cancelledDirectory = Path.Combine(root, "cancelled.parts");
    using (var cts = new CancellationTokenSource())
    {
        var cancelProgress = new CancelOnPositiveProgress(cts);
        await ExpectCanceledAsync(
            () => splitService.SplitToDirectoryAsync(
                sourcePath,
                cancelledDirectory,
                1_000_000,
                cancelProgress,
                cts.Token),
            "Cancellation during split staging must abort before directory commit.");
    }
    Require(!Directory.Exists(cancelledDirectory), "Cancelled split must not publish a partial part set.");
    RequireNoStaging(root, "Cancelled split must clean staging artifacts when possible.");

    var firstPartBytes = await File.ReadAllBytesAsync(partPaths[0]);
    firstPartBytes[0] ^= 0xFF;
    await File.WriteAllBytesAsync(partPaths[0], firstPartBytes);
    var protectedJoinPath = Path.Combine(root, "protected-join.raw");
    await File.WriteAllTextAsync(protectedJoinPath, "preserve-me");
    await ExpectThrowsAsync<InvalidDataException>(
        () => splitService.JoinFromDirectoryAsync(
            splitDirectory,
            protectedJoinPath,
            OutputOverwritePolicy.ReplaceExisting),
        "Join must reject a part whose SHA-256 no longer matches the manifest.");
    Require(await File.ReadAllTextAsync(protectedJoinPath) == "preserve-me", "Failed join must preserve an existing destination.");
    RequireNoStaging(root, "Failed join must clean transaction files when possible.");

    var gzipPath = Path.Combine(root, "source.raw.gz");
    var gzipProgress = new CaptureProgress();
    var compressed = await gzipService.CompressAsync(
        sourcePath,
        gzipPath,
        CompressionLevel.Optimal,
        progress: gzipProgress);
    Require(compressed.SizeBytes > 0, "Gzip compression should produce a non-empty stream.");
    RequireProgress(gzipProgress.Values, "Compression progress should be monotonic and commit only at 1.0.");

    var decompressedPath = Path.Combine(root, "decompressed.raw");
    var decompressProgress = new CaptureProgress();
    var decompressed = await gzipService.DecompressAsync(
        gzipPath,
        decompressedPath,
        maxOutputBytes: (ulong)sourceBytes.Length,
        progress: decompressProgress);
    Require(decompressed.SizeBytes == sourceBytes.Length, "Gzip decompression must report the exact output size.");
    Require((await File.ReadAllBytesAsync(decompressedPath)).SequenceEqual(sourceBytes), "Gzip round trip must preserve exact bytes.");
    RequireProgress(decompressProgress.Values, "Decompression progress should be monotonic and commit only at 1.0.");

    var cappedDestination = Path.Combine(root, "capped.raw");
    await File.WriteAllTextAsync(cappedDestination, "keep-this");
    await ExpectThrowsAsync<InvalidDataException>(
        () => gzipService.DecompressAsync(
            gzipPath,
            cappedDestination,
            maxOutputBytes: (ulong)sourceBytes.Length - 1UL,
            overwritePolicy: OutputOverwritePolicy.ReplaceExisting),
        "Gzip decompression must fail closed when output exceeds the caller-provided cap.");
    Require(await File.ReadAllTextAsync(cappedDestination) == "keep-this", "Capped decompression failure must preserve an existing destination.");

    var malformedGzip = Path.Combine(root, "malformed.gz");
    await File.WriteAllBytesAsync(malformedGzip, new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x01, 0x02, 0x03 });
    var malformedDestination = Path.Combine(root, "malformed-output.raw");
    await File.WriteAllTextAsync(malformedDestination, "untouched");
    await ExpectThrowsAsync<InvalidDataException>(
        () => gzipService.DecompressAsync(
            malformedGzip,
            malformedDestination,
            maxOutputBytes: 1024,
            overwritePolicy: OutputOverwritePolicy.ReplaceExisting),
        "Malformed gzip input must fail without publishing output.");
    Require(await File.ReadAllTextAsync(malformedDestination) == "untouched", "Malformed gzip failure must preserve existing destination.");

    var cancelledGzip = Path.Combine(root, "cancelled.gz");
    using (var cts = new CancellationTokenSource())
    {
        var cancelProgress = new CancelOnPositiveProgress(cts);
        await ExpectCanceledAsync(
            () => gzipService.CompressAsync(
                sourcePath,
                cancelledGzip,
                CompressionLevel.Fastest,
                progress: cancelProgress,
                cancellationToken: cts.Token),
            "Cancellation during gzip compression must abort before commit.");
    }
    Require(!File.Exists(cancelledGzip), "Cancelled compression must not publish a partial destination.");

    await ExpectThrowsAsync<IOException>(
        () => gzipService.CompressAsync(sourcePath, sourcePath, overwritePolicy: OutputOverwritePolicy.ReplaceExisting),
        "Compression must reject identical source and destination paths.");

    RequireNoStaging(root, "All split/compression operations must leave no transaction artifacts.");
    Console.WriteLine("Dragon DiskForge split/join and gzip pipeline smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static byte[] CreatePattern(int length)
{
    var bytes = new byte[length];
    for (var index = 0; index < bytes.Length; index++)
        bytes[index] = checked((byte)((index * 31 + index / 257) & 0xFF));
    return bytes;
}

static void RequireProgress(IReadOnlyList<double> values, string message)
{
    Require(values.Count >= 2, message + " No progress samples were reported.");
    Require(values[0] == 0d, message + " Progress must begin at 0.");
    Require(values[^1] == 1d, message + " Progress must reach 1 only after commit.");
    for (var index = 1; index < values.Count; index++)
    {
        Require(values[index] >= values[index - 1], message + " Progress regressed.");
        Require(values[index] >= 0d && values[index] <= 1d, message + " Progress escaped the 0..1 range.");
    }
    Require(values.Take(values.Count - 1).All(value => value < 1d), message + " Progress reached 1 before commit.");
}

static void RequireNoStaging(string directory, string message)
{
    if (Directory.EnumerateFiles(directory, "*.dragon-tmp", SearchOption.AllDirectories).Any()
        || Directory.EnumerateDirectories(directory, "*.dragon-split-tmp", SearchOption.AllDirectories).Any())
    {
        throw new InvalidOperationException(message);
    }
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

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class CaptureProgress : IProgress<double>
{
    public List<double> Values { get; } = new();
    public void Report(double value) => Values.Add(value);
}

sealed class CancelOnPositiveProgress : IProgress<double>
{
    private readonly CancellationTokenSource _cts;
    private bool _cancelled;

    public CancelOnPositiveProgress(CancellationTokenSource cts) => _cts = cts;

    public void Report(double value)
    {
        if (!_cancelled && value > 0d && value < 1d)
        {
            _cancelled = true;
            _cts.Cancel();
        }
    }
}
