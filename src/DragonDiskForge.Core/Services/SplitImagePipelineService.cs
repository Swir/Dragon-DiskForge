using System.Security.Cryptography;
using System.Text.Json;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Transactional split/join pipeline for flat image bytes. Split sets are published as one
/// directory only after every part and the integrity manifest are complete.
/// </summary>
public sealed class SplitImagePipelineService
{
    public const string ManifestFileName = "dragon-split-manifest.json";

    private const int BufferSize = 1024 * 1024;
    private const int MaxPartCount = 10_000;
    private const double DataStageProgressCeiling = 0.99d;
    private const string TemporaryDirectorySuffix = ".dragon-split-tmp";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SafeOutputService _safeOutputService;

    public SplitImagePipelineService(SafeOutputService? safeOutputService = null)
    {
        _safeOutputService = safeOutputService ?? new SafeOutputService();
    }

    public async Task<SplitImageSetInfo> SplitToDirectoryAsync(
        string sourcePath,
        string destinationDirectoryPath,
        long partSizeBytes,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectoryPath);
        if (partSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(partSizeBytes), "Part size must be positive.");

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullDestinationDirectory = NormalizeDirectoryPath(destinationDirectoryPath);
        if (!File.Exists(fullSourcePath))
            throw new FileNotFoundException("Split source file was not found.", fullSourcePath);
        if (Directory.Exists(fullSourcePath))
            throw new IOException("Split source path points to a directory.");
        if (File.Exists(fullDestinationDirectory) || Directory.Exists(fullDestinationDirectory))
            throw new IOException("Split destination already exists.");

        var parentDirectory = Path.GetDirectoryName(fullDestinationDirectory)
            ?? throw new ArgumentException("Split destination must include a parent directory.", nameof(destinationDirectoryPath));
        if (!Directory.Exists(parentDirectory))
            throw new DirectoryNotFoundException($"Split destination parent was not found: {parentDirectory}");

        var destinationName = Path.GetFileName(fullDestinationDirectory);
        if (string.IsNullOrWhiteSpace(destinationName))
            throw new ArgumentException("Split destination must include a directory name.", nameof(destinationDirectoryPath));

        var sourceLength = checked((ulong)new FileInfo(fullSourcePath).Length);
        var partSize = checked((ulong)partSizeBytes);
        var partCount64 = sourceLength == 0
            ? 1UL
            : checked((sourceLength + partSize - 1UL) / partSize);
        if (partCount64 > MaxPartCount)
            throw new NotSupportedException($"Split would create more than {MaxPartCount} parts.");
        var partCount = checked((int)partCount64);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0d);

        var stagingDirectory = Path.Combine(
            parentDirectory,
            $".{destinationName}.{Guid.NewGuid():N}{TemporaryDirectorySuffix}");
        Directory.CreateDirectory(stagingDirectory);
        var committed = false;

        try
        {
            var manifestParts = new List<SplitManifestPart>(partCount);
            var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
            ulong processed = 0;

            await using var source = new FileStream(
                fullSourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: BufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            for (var index = 0; index < partCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var partName = $"part-{index + 1:D6}.bin";
                var partPath = Path.Combine(stagingDirectory, partName);
                var expectedPartSize = sourceLength == 0
                    ? 0UL
                    : Math.Min(partSize, sourceLength - processed);
                ulong written = 0;

                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = new FileStream(
                    partPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: BufferSize,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    while (written < expectedPartSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var chunk = checked((int)Math.Min((ulong)buffer.Length, expectedPartSize - written));
                        var read = await source.ReadAsync(buffer.AsMemory(0, chunk), cancellationToken);
                        if (read == 0)
                            throw new EndOfStreamException("Split source ended before the captured source length.");

                        hasher.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        written = checked(written + (ulong)read);
                        processed = checked(processed + (ulong)read);
                        progress?.Report(ToDataStageProgress(processed, sourceLength));
                    }

                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }

                manifestParts.Add(new SplitManifestPart(
                    partName,
                    written,
                    Convert.ToHexString(hasher.GetHashAndReset())));
            }

            if (sourceLength > 0)
            {
                var trailingByte = source.ReadByte();
                if (trailingByte >= 0)
                    throw new InvalidDataException("Split source grew after its length was captured.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(DataStageProgressCeiling);

            var manifest = new SplitManifest(1, sourceLength, partSizeBytes, manifestParts);
            await WriteManifestAsync(
                Path.Combine(stagingDirectory, ManifestFileName),
                manifest,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullDestinationDirectory) || Directory.Exists(fullDestinationDirectory))
                throw new IOException("Split destination appeared before final commit.");

            // Staging lives beside the destination, so the final directory move stays on-volume.
            // Once commit begins, cancellation is intentionally not observed mid-rename.
            Directory.Move(stagingDirectory, fullDestinationDirectory);
            committed = true;
            progress?.Report(1d);

            return new SplitImageSetInfo(
                fullDestinationDirectory,
                sourceLength,
                partSizeBytes,
                partCount);
        }
        finally
        {
            if (!committed)
                TryDeleteDirectory(stagingDirectory);
        }
    }

    public async Task<OutputCommitInfo> JoinFromDirectoryAsync(
        string sourceDirectoryPath,
        string destinationPath,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullSourceDirectory = NormalizeDirectoryPath(sourceDirectoryPath);
        if (!Directory.Exists(fullSourceDirectory))
            throw new DirectoryNotFoundException($"Split set directory was not found: {fullSourceDirectory}");

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (IsWithinDirectory(fullDestinationPath, fullSourceDirectory))
            throw new IOException("Join destination must be outside the split source directory.");

        var manifestPath = Path.Combine(fullSourceDirectory, ManifestFileName);
        var manifest = await ReadAndValidateManifestAsync(manifestPath, fullSourceDirectory, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0d);

        ulong processed = 0;
        var result = await _safeOutputService.WriteAsync(
            fullDestinationPath,
            async (output, token) =>
            {
                var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);

                foreach (var part in manifest.Parts)
                {
                    token.ThrowIfCancellationRequested();
                    var partPath = Path.Combine(fullSourceDirectory, part.FileName);
                    await using var input = new FileStream(
                        partPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: BufferSize,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                    ulong copied = 0;
                    while (copied < part.SizeBytes)
                    {
                        token.ThrowIfCancellationRequested();
                        var chunk = checked((int)Math.Min((ulong)buffer.Length, part.SizeBytes - copied));
                        var read = await input.ReadAsync(buffer.AsMemory(0, chunk), token);
                        if (read == 0)
                            throw new EndOfStreamException($"Split part ended early: {part.FileName}");

                        hasher.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        copied = checked(copied + (ulong)read);
                        processed = checked(processed + (ulong)read);
                        progress?.Report(ToDataStageProgress(processed, manifest.SourceSizeBytes));
                    }

                    if (input.ReadByte() >= 0)
                        throw new InvalidDataException($"Split part grew after validation: {part.FileName}");

                    var actualHash = Convert.ToHexString(hasher.GetHashAndReset());
                    if (!string.Equals(actualHash, part.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Split part SHA-256 mismatch: {part.FileName}");
                }

                if (manifest.SourceSizeBytes == 0)
                    progress?.Report(DataStageProgressCeiling);
            },
            overwritePolicy,
            cancellationToken);

        if ((ulong)result.SizeBytes != manifest.SourceSizeBytes)
            throw new InvalidDataException("Joined output length does not match the split manifest source length.");

        progress?.Report(1d);
        return result;
    }

    private static async Task<SplitManifest> ReadAndValidateManifestAsync(
        string manifestPath,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"Split manifest is missing: {ManifestFileName}");

        SplitManifest? manifest;
        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            manifest = await JsonSerializer.DeserializeAsync<SplitManifest>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Split manifest JSON is invalid.", exception);
        }

        if (manifest is null || manifest.Version != 1)
            throw new InvalidDataException("Split manifest version is unsupported.");
        if (manifest.SourceSizeBytes > long.MaxValue)
            throw new NotSupportedException("Split manifest source length exceeds the current output file domain.");
        if (manifest.PartSizeBytes <= 0)
            throw new InvalidDataException("Split manifest part size is invalid.");
        if (manifest.Parts.Count is < 1 or > MaxPartCount)
            throw new InvalidDataException("Split manifest part count is invalid.");

        var expectedPartCount = manifest.SourceSizeBytes == 0
            ? 1UL
            : checked((manifest.SourceSizeBytes + (ulong)manifest.PartSizeBytes - 1UL) / (ulong)manifest.PartSizeBytes);
        if (expectedPartCount != (ulong)manifest.Parts.Count)
            throw new InvalidDataException("Split manifest part count does not match source geometry.");

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var names = new HashSet<string>(comparer);
        ulong total = 0;

        for (var index = 0; index < manifest.Parts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = manifest.Parts[index];
            if (string.IsNullOrWhiteSpace(part.FileName)
                || Path.IsPathRooted(part.FileName)
                || !string.Equals(part.FileName, Path.GetFileName(part.FileName), StringComparison.Ordinal)
                || !names.Add(part.FileName))
            {
                throw new InvalidDataException("Split manifest contains an unsafe or duplicate part filename.");
            }

            var expectedSize = manifest.SourceSizeBytes == 0
                ? 0UL
                : Math.Min((ulong)manifest.PartSizeBytes, manifest.SourceSizeBytes - total);
            if (part.SizeBytes != expectedSize)
                throw new InvalidDataException($"Split part size metadata is inconsistent: {part.FileName}");

            try
            {
                var hashBytes = Convert.FromHexString(part.Sha256);
                if (hashBytes.Length != 32)
                    throw new InvalidDataException($"Split part SHA-256 is invalid: {part.FileName}");
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"Split part SHA-256 is invalid: {part.FileName}", exception);
            }

            var partPath = Path.Combine(sourceDirectory, part.FileName);
            if (!File.Exists(partPath) || Directory.Exists(partPath))
                throw new InvalidDataException($"Split part is missing: {part.FileName}");
            if ((ulong)new FileInfo(partPath).Length != part.SizeBytes)
                throw new InvalidDataException($"Split part physical size mismatch: {part.FileName}");

            total = checked(total + part.SizeBytes);
        }

        if (total != manifest.SourceSizeBytes)
            throw new InvalidDataException("Split manifest total size does not match the declared source size.");

        return manifest;
    }

    private static async Task WriteManifestAsync(
        string path,
        SplitManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, manifest, ManifestJsonOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(full))
            throw new ArgumentException("Directory path is invalid.", nameof(path));
        return full;
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static double ToDataStageProgress(ulong processed, ulong total)
    {
        if (total == 0)
            return DataStageProgressCeiling;
        return Math.Min(1d, (double)processed / total) * DataStageProgressCeiling;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Preserve the original failure/cancellation. A later cleanup pass may remove staging.
        }
    }

    private sealed record SplitManifest(
        int Version,
        ulong SourceSizeBytes,
        long PartSizeBytes,
        List<SplitManifestPart> Parts);

    private sealed record SplitManifestPart(
        string FileName,
        ulong SizeBytes,
        string Sha256);
}
