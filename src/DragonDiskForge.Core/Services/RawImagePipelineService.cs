using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Safe Core pipelines for producing raw disk images. All destinations are committed through
/// <see cref="SafeOutputService"/> so incomplete work is never intentionally published.
/// </summary>
public sealed class RawImagePipelineService
{
    private const int BufferSize = 1024 * 1024;
    private const double DataStageProgressCeiling = 0.99d;

    private readonly SafeOutputService _safeOutputService;

    public RawImagePipelineService(SafeOutputService? safeOutputService = null)
    {
        _safeOutputService = safeOutputService ?? new SafeOutputService();
    }

    public async Task<OutputCommitInfo> CreateBlankAsync(
        string destinationPath,
        ulong sizeBytes,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        EnsureSupportedLength(sizeBytes, "RAW image size");
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0d);

        var result = await _safeOutputService.WriteAsync(
            destinationPath,
            (output, token) =>
            {
                token.ThrowIfCancellationRequested();
                output.SetLength(checked((long)sizeBytes));
                token.ThrowIfCancellationRequested();
                progress?.Report(DataStageProgressCeiling);
                return Task.CompletedTask;
            },
            overwritePolicy,
            cancellationToken);

        progress?.Report(1d);
        return result;
    }

    public async Task<OutputCommitInfo> ExportGuestToRawAsync(
        IGuestByteReader source,
        string destinationPath,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var sourceLength = source.Length;
        EnsureSupportedLength(sourceLength, "Guest image length");
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0d);

        var result = await _safeOutputService.WriteAsync(
            destinationPath,
            async (output, token) =>
            {
                var buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
                ulong processed = 0;

                while (processed < sourceLength)
                {
                    token.ThrowIfCancellationRequested();
                    var chunk = checked((int)Math.Min((ulong)buffer.Length, sourceLength - processed));
                    var memory = buffer.AsMemory(0, chunk);

                    await source.ReadExactlyAsync(processed, memory, token);
                    token.ThrowIfCancellationRequested();
                    await output.WriteAsync(memory, token);

                    processed = checked(processed + (ulong)chunk);
                    progress?.Report(ToDataStageProgress(processed, sourceLength));
                }

                if (sourceLength == 0)
                    progress?.Report(DataStageProgressCeiling);
            },
            overwritePolicy,
            cancellationToken);

        if ((ulong)result.SizeBytes != sourceLength)
            throw new InvalidDataException("Committed RAW output length does not match the guest-visible source length.");

        progress?.Report(1d);
        return result;
    }

    public async Task<OutputCommitInfo> ConvertQcow2ToRawAsync(
        string sourcePath,
        string destinationPath,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDistinctPaths(sourcePath, destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        await using var reader = await Qcow2GuestByteReader.OpenAsync(sourcePath, cancellationToken);
        return await ExportGuestToRawAsync(
            reader,
            destinationPath,
            overwritePolicy,
            progress,
            cancellationToken);
    }

    public async Task<OutputCommitInfo> ConvertVmdkSparseToRawAsync(
        string sourcePath,
        string destinationPath,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateDistinctPaths(sourcePath, destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        await using var reader = await VmdkSparseGuestByteReader.OpenAsync(sourcePath, cancellationToken);
        return await ExportGuestToRawAsync(
            reader,
            destinationPath,
            overwritePolicy,
            progress,
            cancellationToken);
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
            throw new IOException("Source and destination paths must be different for image conversion.");
    }

    private static void EnsureSupportedLength(ulong length, string description)
    {
        if (length > long.MaxValue)
            throw new NotSupportedException($"{description} exceeds the maximum file length supported by the current output pipeline.");
    }

    private static double ToDataStageProgress(ulong processed, ulong total)
    {
        if (total == 0)
            return DataStageProgressCeiling;

        var ratio = Math.Min(1d, (double)processed / total);
        return ratio * DataStageProgressCeiling;
    }
}
