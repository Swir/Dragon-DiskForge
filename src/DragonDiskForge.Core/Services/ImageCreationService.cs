using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Streams;

namespace DragonDiskForge.Core.Services;

public sealed class ImageCreationService
{
    public async Task CreateIsoAsync(string sourceDirectory, string destination, string label = "DRAGON_DISKFORGE",
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        var root = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Symbolic links and junctions are not accepted.");
        var target = Path.GetFullPath(destination);
        if (target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The ISO output must be outside the source folder.");
        if (string.IsNullOrWhiteSpace(label) || label.Length > 32 || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("ISO label must use 1–32 letters, numbers or underscores.");
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = label.ToUpperInvariant() };
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));
        int count = 0;
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Dequeue();
            if (depth > 64) throw new IOException("Source folder nesting is too deep.");
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested();
                if (++count > 100000) throw new IOException("An ISO build is limited to 100,000 entries.");
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Remove symbolic links or junctions before creating an ISO.");
                var relative = Path.GetRelativePath(root, entry.FullName);
                if (entry is DirectoryInfo)
                {
                    builder.AddDirectory(relative);
                    pending.Enqueue((entry.FullName, depth + 1));
                }
                else
                {
                    if (((FileInfo)entry).Length > uint.MaxValue) throw new IOException("This ISO writer supports individual files up to 4 GiB minus one byte.");
                    builder.AddFile(relative, entry.FullName);
                }
            }
        }
        await AtomicFileOutput.WriteAsync(destination, async output =>
        {
            using var image = builder.Build();
            await CopyAsync(image, output, progress, token, skipZeroBlocks: false);
        }, token);
    }

    public Task ConvertAsync(string source, string destination, string targetFormat,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        var format = targetFormat.ToLowerInvariant();
        if (format is not ("raw" or "vhd" or "vhdx")) throw new ArgumentException("Output format must be raw, vhd or vhdx.");
        return AtomicFileOutput.WriteAsync(destination, async output =>
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var disk = OpenVirtualDisk(input, Path.GetExtension(source));
            Stream content = disk is null ? input : disk.Content;
            if (content.Length <= 0 || content.Length % 512 != 0) throw new InvalidDataException("Disk capacity must be a positive multiple of 512 bytes.");
            if (format == "vhd" && content.Length > 2040L * 1024 * 1024 * 1024)
                throw new NotSupportedException("VHD output is limited to 2040 GiB.");
            if (format == "raw") await CopyAsync(content, output, progress, token, skipZeroBlocks: false);
            else
            {
                using VirtualDisk converted = format == "vhd"
                    ? DiscUtils.Vhd.Disk.InitializeDynamic(output, Ownership.None, content.Length)
                    : DiscUtils.Vhdx.Disk.InitializeDynamic(output, Ownership.None, content.Length, 2 * 1024 * 1024);
                await CopyAsync(content, converted.Content, progress, token, skipZeroBlocks: true);
            }
        }, token);
    }

    public Task CreateVirtualDiskAsync(string destination, long capacity, string format, CancellationToken token = default)
    {
        if (capacity <= 0 || capacity % 512 != 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (format is not ("vhd" or "vhdx")) throw new ArgumentException("Use vhd or vhdx.");
        if (format == "vhd" && capacity > 2040L * 1024 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(capacity));
        return AtomicFileOutput.WriteAsync(destination, output =>
        {
            token.ThrowIfCancellationRequested();
            using VirtualDisk disk = format == "vhd"
                ? DiscUtils.Vhd.Disk.InitializeDynamic(output, Ownership.None, capacity)
                : DiscUtils.Vhdx.Disk.InitializeDynamic(output, Ownership.None, capacity);
            return Task.CompletedTask;
        }, token);
    }

    internal static VirtualDisk? OpenVirtualDisk(Stream input, string extension) => extension.ToLowerInvariant() switch
    {
        ".vhd" => new DiscUtils.Vhd.Disk(input, Ownership.None),
        ".vhdx" => new DiscUtils.Vhdx.Disk(input, Ownership.None),
        ".raw" or ".img" or ".ima" or ".dd" or ".flp" => null,
        _ => throw new NotSupportedException("Conversion currently accepts standalone RAW/IMG/IMA, VHD and VHDX images. Differencing images require their parent and are rejected.")
    };

    private static async Task CopyAsync(Stream source, Stream destination, IProgress<double>? progress, CancellationToken token, bool skipZeroBlocks)
    {
        source.Position = 0;
        var buffer = new byte[1024 * 1024];
        long processed = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            if (!skipZeroBlocks || buffer.AsSpan(0, count).ContainsAnyExcept((byte)0))
                await destination.WriteAsync(buffer.AsMemory(0, count), token);
            else destination.Seek(count, SeekOrigin.Current);
            processed += count;
            progress?.Report((double)processed / Math.Max(1, source.Length));
        }
        token.ThrowIfCancellationRequested();
        progress?.Report(1);
    }
}
