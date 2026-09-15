using System.Buffers.Binary;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class Iso9660DirectBrowseProvider : IDirectBrowseProvider
{
    private const int SectorSize = 2048;
    private const int FirstVolumeDescriptorLba = 16;
    private const int MaxVolumeDescriptorLba = 63;
    private const int RootDirectoryRecordOffset = 156;
    private const int MaxDirectoryBytes = 64 * 1024 * 1024;
    private const int MaxDirectoryCount = 100_000;
    private const int CopyBufferSize = 128 * 1024;

    public string Id => "iso9660";
    public string DisplayName => "ISO9660 / Joliet";
    public IReadOnlyCollection<string> Extensions { get; } = [".iso"];

    public async ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            await using var stream = OpenRead(path);
            _ = await ReadVolumeContextAsync(stream, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            return false;
        }
    }

    public async ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!await CanHandleAsync(path, cancellationToken))
            throw new InvalidDataException("The image is not a supported ISO9660/Joliet filesystem image.");

        var file = new FileInfo(path);
        return new DiskImageInfo(
            file.FullName,
            file.Name,
            "ISO",
            file.Length,
            "ISO9660/Joliet volume descriptor",
            CanExplore: true,
            CanMount: true,
            CanConvert: false,
            CanVerify: true);
    }

    public async Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string imagePath,
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = OpenRead(imagePath);
        var volume = await ReadVolumeContextAsync(stream, cancellationToken);
        var normalized = NormalizeVirtualPath(directoryPath);
        var directory = await ResolveAsync(stream, volume, normalized, cancellationToken);
        if (!directory.IsDirectory)
            throw new IOException("The requested direct-browse path is not a directory.");

        var records = await ReadDirectoryAsync(stream, volume, directory, normalized, cancellationToken);
        return records
            .Select(ToExplorerEntry)
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ExplorerEntry>> SearchAsync(
        string imagePath,
        string startPath,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResults));

        var needle = query?.Trim() ?? string.Empty;
        if (needle.Length == 0)
            return [];

        await using var stream = OpenRead(imagePath);
        var volume = await ReadVolumeContextAsync(stream, cancellationToken);
        var normalizedStart = NormalizeVirtualPath(startPath);
        var start = await ResolveAsync(stream, volume, normalizedStart, cancellationToken);
        if (!start.IsDirectory)
            throw new IOException("The direct-browse search root is not a directory.");

        var results = new List<ExplorerEntry>(Math.Min(maxResults, 256));
        var stack = new Stack<(IsoRecord Directory, string Path)>();
        var visited = new HashSet<(uint Extent, uint Length)>();
        stack.Push((start, normalizedStart));

        var scannedDirectories = 0;
        while (stack.Count > 0 && results.Count < maxResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++scannedDirectories > MaxDirectoryCount)
                throw new InvalidDataException("ISO directory tree exceeds the direct-browse safety limit.");

            var current = stack.Pop();
            if (!visited.Add((current.Directory.ExtentLba, current.Directory.DataLength)))
                continue;

            var children = await ReadDirectoryAsync(stream, volume, current.Directory, current.Path, cancellationToken);
            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(ToExplorerEntry(child));
                    if (results.Count >= maxResults)
                        break;
                }

                if (child.IsDirectory)
                    stack.Push((child, child.VirtualPath));
            }
        }

        return results;
    }

    public async Task CopyOutAsync(
        string imagePath,
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));

        var destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(destinationRoot))
            throw new DirectoryNotFoundException($"Destination directory does not exist: {destinationRoot}");

        await using var stream = OpenRead(imagePath);
        var volume = await ReadVolumeContextAsync(stream, cancellationToken);
        var normalizedSource = NormalizeVirtualPath(sourcePath);
        if (normalizedSource == "/")
            throw new InvalidOperationException("Copy the contents you need rather than exporting the entire ISO root in one action.");

        var source = await ResolveAsync(stream, volume, normalizedSource, cancellationToken);
        ValidateDestinationName(source.Name);
        var destinationPath = Path.Combine(destinationRoot, source.Name);
        EnsureDestinationAvailable(destinationPath);

        if (!source.IsDirectory)
        {
            await CopyFileAsync(stream, source, destinationPath, progress, cancellationToken);
            progress?.Report(1d);
            return;
        }

        var tree = new List<(IsoRecord Record, string RelativePath)>();
        await CollectTreeAsync(stream, volume, source, normalizedSource, string.Empty, tree, cancellationToken);
        var totalBytes = tree.Where(x => !x.Record.IsDirectory).Sum(x => (long)x.Record.DataLength);
        long copiedBytes = 0;

        Directory.CreateDirectory(destinationPath);
        try
        {
            foreach (var item in tree)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = CombineDestination(destinationPath, item.RelativePath);
                EnsureDestinationAvailable(outputPath);

                if (item.Record.IsDirectory)
                {
                    Directory.CreateDirectory(outputPath);
                    continue;
                }

                await CopyFileAsync(
                    stream,
                    item.Record,
                    outputPath,
                    progress: null,
                    cancellationToken,
                    bytesCopied =>
                    {
                        copiedBytes += bytesCopied;
                        progress?.Report(totalBytes == 0 ? 1d : Math.Clamp(copiedBytes / (double)totalBytes, 0d, 1d));
                    });
            }

            progress?.Report(1d);
        }
        catch
        {
            throw;
        }
    }

    private static FileStream OpenRead(string path)
        => new(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

    private static async Task<VolumeContext> ReadVolumeContextAsync(FileStream stream, CancellationToken cancellationToken)
    {
        if (stream.Length < (FirstVolumeDescriptorLba + 1L) * SectorSize)
            throw new InvalidDataException("ISO image is too small to contain a volume descriptor.");

        byte[]? primary = null;
        byte[]? joliet = null;

        for (var lba = FirstVolumeDescriptorLba; lba <= MaxVolumeDescriptorLba; lba++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = (long)lba * SectorSize;
            if (offset + SectorSize > stream.Length)
                break;

            var sector = new byte[SectorSize];
            stream.Position = offset;
            await stream.ReadExactlyAsync(sector, cancellationToken);

            if (!sector.AsSpan(1, 5).SequenceEqual("CD001"u8))
                continue;

            var type = sector[0];
            if (type == 1 && primary is null)
                primary = sector;
            else if (type == 2 && joliet is null && IsJolietDescriptor(sector))
                joliet = sector;
            else if (type == 255)
                break;
        }

        var descriptor = joliet ?? primary
            ?? throw new InvalidDataException("ISO9660 primary/supplementary volume descriptor was not found.");
        var isJoliet = ReferenceEquals(descriptor, joliet);
        var root = ParseDirectoryRecord(descriptor, RootDirectoryRecordOffset, isJoliet, "/", rootRecord: true)
            ?? throw new InvalidDataException("ISO root directory record is invalid.");

        return new VolumeContext(root, isJoliet);
    }

    private static bool IsJolietDescriptor(byte[] descriptor)
    {
        if (descriptor.Length < 91)
            return false;

        return descriptor[88] == (byte)'%'
            && descriptor[89] == (byte)'/'
            && descriptor[90] is (byte)'@' or (byte)'C' or (byte)'E';
    }

    private static async Task<IsoRecord> ResolveAsync(
        FileStream stream,
        VolumeContext volume,
        string virtualPath,
        CancellationToken cancellationToken)
    {
        if (virtualPath == "/")
            return volume.Root;

        var segments = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = volume.Root;
        var currentPath = "/";

        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!current.IsDirectory)
                throw new DirectoryNotFoundException($"ISO path is not a directory: {currentPath}");

            var children = await ReadDirectoryAsync(stream, volume, current, currentPath, cancellationToken);
            var next = children.FirstOrDefault(x => x.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (next is null)
                throw new FileNotFoundException($"ISO entry was not found: {virtualPath}");

            current = next;
            currentPath = current.VirtualPath;
        }

        return current;
    }

    private static async Task<IReadOnlyList<IsoRecord>> ReadDirectoryAsync(
        FileStream stream,
        VolumeContext volume,
        IsoRecord directory,
        string parentPath,
        CancellationToken cancellationToken)
    {
        if (!directory.IsDirectory)
            throw new IOException("ISO record is not a directory.");
        if (directory.DataLength > MaxDirectoryBytes)
            throw new InvalidDataException("ISO directory metadata exceeds the direct-browse safety limit.");

        var start = checked((long)directory.ExtentLba * SectorSize);
        var length = checked((int)directory.DataLength);
        if (start < 0 || start + length > stream.Length)
            throw new InvalidDataException("ISO directory extent points outside the image.");

        var data = new byte[length];
        stream.Position = start;
        await stream.ReadExactlyAsync(data, cancellationToken);

        var records = new List<IsoRecord>();
        var offset = 0;
        while (offset < data.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recordLength = data[offset];
            if (recordLength == 0)
            {
                offset = ((offset / SectorSize) + 1) * SectorSize;
                continue;
            }

            if (recordLength < 34 || offset + recordLength > data.Length)
                throw new InvalidDataException("ISO directory record is truncated or invalid.");

            var record = ParseDirectoryRecord(data, offset, volume.Joliet, parentPath, rootRecord: false);
            if (record is not null && record.Name is not "." and not "..")
                records.Add(record);

            offset += recordLength;
        }

        return records;
    }

    private static IsoRecord? ParseDirectoryRecord(
        ReadOnlySpan<byte> data,
        int offset,
        bool joliet,
        string parentPath,
        bool rootRecord)
    {
        if (offset < 0 || offset + 34 > data.Length)
            return null;

        var recordLength = data[offset];
        if (recordLength < 34 || offset + recordLength > data.Length)
            return null;

        var extent = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 2, 4));
        var length = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 10, 4));
        var flags = data[offset + 25];
        var nameLength = data[offset + 32];
        if (offset + 33 + nameLength > offset + recordLength)
            return null;

        string name;
        if (rootRecord)
        {
            name = "/";
        }
        else if (nameLength == 1 && data[offset + 33] == 0)
        {
            name = ".";
        }
        else if (nameLength == 1 && data[offset + 33] == 1)
        {
            name = "..";
        }
        else
        {
            var rawName = data.Slice(offset + 33, nameLength);
            name = joliet
                ? Encoding.BigEndianUnicode.GetString(rawName[..(rawName.Length - (rawName.Length % 2))])
                : Encoding.ASCII.GetString(rawName);
            name = NormalizeIsoName(name);
            if (name.Length == 0 || name.IndexOfAny(['/', '\\', '\0']) >= 0)
                throw new InvalidDataException("ISO contains an unsafe file identifier.");
        }

        var virtualPath = rootRecord ? "/" : CombineVirtual(parentPath, name);
        return new IsoRecord(
            name,
            virtualPath,
            extent,
            length,
            IsDirectory: (flags & 0x02) != 0,
            ParseRecordingTime(data.Slice(offset + 18, 7)));
    }

    private static string NormalizeIsoName(string name)
    {
        var value = name.TrimEnd('\0');
        var version = value.LastIndexOf(';');
        if (version > 0 && value[(version + 1)..].All(char.IsDigit))
            value = value[..version];
        return value.EndsWith(".", StringComparison.Ordinal) ? value[..^1] : value;
    }

    private static DateTimeOffset ParseRecordingTime(ReadOnlySpan<byte> value)
    {
        try
        {
            if (value.Length < 7)
                return DateTimeOffset.UnixEpoch;

            var year = 1900 + value[0];
            var offsetQuarterHours = unchecked((sbyte)value[6]);
            var offset = TimeSpan.FromMinutes(offsetQuarterHours * 15);
            return new DateTimeOffset(year, value[1], value[2], value[3], value[4], value[5], offset).ToUniversalTime();
        }
        catch
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    private static ExplorerEntry ToExplorerEntry(IsoRecord record)
        => new(
            record.Name,
            record.VirtualPath,
            record.IsDirectory ? ExplorerEntryKind.Directory : ExplorerEntryKind.File,
            record.IsDirectory ? null : record.DataLength,
            record.ModifiedUtc,
            IsReparsePoint: false);

    private static string NormalizeVirtualPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return "/";

        if (path.Contains('\\'))
            throw new InvalidOperationException("Direct-browse paths use '/' separators only.");

        var segments = path.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.IndexOf('\0') >= 0))
            throw new InvalidOperationException("Direct-browse path contains an unsafe segment.");

        return "/" + string.Join('/', segments);
    }

    private static string CombineVirtual(string parent, string name)
        => parent == "/" ? "/" + name : parent.TrimEnd('/') + "/" + name;

    private static async Task CollectTreeAsync(
        FileStream stream,
        VolumeContext volume,
        IsoRecord directory,
        string directoryPath,
        string relativePath,
        List<(IsoRecord Record, string RelativePath)> output,
        CancellationToken cancellationToken)
    {
        if (output.Count > MaxDirectoryCount)
            throw new InvalidDataException("ISO directory tree exceeds the copy-out safety limit.");

        var children = await ReadDirectoryAsync(stream, volume, directory, directoryPath, cancellationToken);
        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDestinationName(child.Name);
            var childRelative = string.IsNullOrEmpty(relativePath)
                ? child.Name
                : relativePath + "/" + child.Name;
            output.Add((child, childRelative));

            if (child.IsDirectory)
                await CollectTreeAsync(stream, volume, child, child.VirtualPath, childRelative, output, cancellationToken);
        }
    }

    private static async Task CopyFileAsync(
        FileStream stream,
        IsoRecord source,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        Action<int>? bytesWritten = null)
    {
        if (source.IsDirectory)
            throw new IOException("ISO record is a directory, not a file.");

        var start = checked((long)source.ExtentLba * SectorSize);
        var length = (long)source.DataLength;
        if (start < 0 || start + length > stream.Length)
            throw new InvalidDataException("ISO file extent points outside the image.");

        await using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous);

        stream.Position = start;
        var buffer = new byte[CopyBufferSize];
        long remaining = length;
        long copied = 0;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, request), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("ISO file extent ended unexpectedly.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
            copied += read;
            bytesWritten?.Invoke(read);
            progress?.Report(length == 0 ? 1d : Math.Clamp(copied / (double)length, 0d, 1d));
        }

        await output.FlushAsync(cancellationToken);
    }

    private static void ValidateDestinationName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.EndsWith(' ')
            || name.EndsWith('.'))
        {
            throw new InvalidDataException($"ISO entry cannot be safely exported as a Windows filename: {name}");
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"ISO entry uses a Windows-reserved filename and cannot be exported safely: {name}");
    }

    private static void EnsureDestinationAvailable(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException($"Destination already exists and will not be overwritten: {path}");
    }

    private static string CombineDestination(string root, string relativeVirtualPath)
    {
        var segments = relativeVirtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = root;
        foreach (var segment in segments)
        {
            ValidateDestinationName(segment);
            path = Path.Combine(path, segment);
        }
        return path;
    }

    private sealed record VolumeContext(IsoRecord Root, bool Joliet);

    private sealed record IsoRecord(
        string Name,
        string VirtualPath,
        uint ExtentLba,
        uint DataLength,
        bool IsDirectory,
        DateTimeOffset ModifiedUtc);
}
