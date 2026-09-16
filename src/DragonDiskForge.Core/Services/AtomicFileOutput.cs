namespace DragonDiskForge.Core.Services;

/// <summary>Commits a new output only after success. Never replaces an existing file.</summary>
public static class AtomicFileOutput
{
    public static async Task WriteAsync(string destination, Func<Stream, Task> write, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var fullPath = Path.GetFullPath(destination);
        if (File.Exists(fullPath) || Directory.Exists(fullPath)) throw new IOException("The output already exists.");
        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.partial");
        try
        {
            token.ThrowIfCancellationRequested();
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await write(stream);
                token.ThrowIfCancellationRequested();
                await stream.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
