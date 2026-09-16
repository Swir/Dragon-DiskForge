using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class SafeOutputService
{
    private const int BufferSize = 1024 * 1024;
    private const string TemporarySuffix = ".dragon-tmp";

    public async Task<OutputCommitInfo> WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writer,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(writer);

        if (!Enum.IsDefined(overwritePolicy))
            throw new ArgumentOutOfRangeException(nameof(overwritePolicy));

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new ArgumentException("Destination must include a parent directory.", nameof(destinationPath));

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Destination directory was not found: {directory}");

        if (Directory.Exists(fullDestinationPath))
            throw new IOException("Destination path points to a directory.");

        if (overwritePolicy == OutputOverwritePolicy.FailIfExists && File.Exists(fullDestinationPath))
            throw new IOException("Destination file already exists.");

        cancellationToken.ThrowIfCancellationRequested();

        var fileName = Path.GetFileName(fullDestinationPath);
        var temporaryPath = Path.Combine(
            directory,
            $".{fileName}.{Guid.NewGuid():N}{TemporarySuffix}");

        var committed = false;

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: BufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await writer(output, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var sizeBytes = new FileInfo(temporaryPath).Length;
            var replacedExisting = CommitTemporaryFile(
                temporaryPath,
                fullDestinationPath,
                overwritePolicy);

            committed = true;
            return new OutputCommitInfo(fullDestinationPath, sizeBytes, replacedExisting);
        }
        finally
        {
            if (!committed)
                TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static bool CommitTemporaryFile(
        string temporaryPath,
        string destinationPath,
        OutputOverwritePolicy overwritePolicy)
    {
        if (overwritePolicy == OutputOverwritePolicy.FailIfExists)
        {
            File.Move(temporaryPath, destinationPath, overwrite: false);
            return false;
        }

        if (File.Exists(destinationPath))
        {
            try
            {
                File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return true;
            }
            catch (FileNotFoundException)
            {
                // The destination disappeared between the existence check and replacement.
            }
        }

        try
        {
            File.Move(temporaryPath, destinationPath, overwrite: false);
            return false;
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return true;
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        catch
        {
            // Preserve the original failure/cancellation. A later cleanup pass may remove the temp file.
        }
    }
}
