using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed class Iso9660DirectImageExplorer : IDirectImageExplorer
{
    private const int SectorSize = 2048;
    private const int FirstDescriptorSector = 16;
    private const int MaxDescriptorSectors = 64;
    private const byte DirectoryFlag = 0x02;
    private const byte MultiExtentFlag = 0x80;

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IReadOnlyList<IsoNode>> _directoryCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _imagePath;

    private IsoNode? _root;
    private Encoding _nameEncoding = Encoding.ASCII;
    private bool _initialized;

    public Iso9660DirectImageExplorer(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("ISO image path cannot be empty.", nameof(imagePath));

        _imagePath = Path.GetFullPath(imagePath);
    }

    public string ProviderId => "iso9660";
    public string ImagePath => _imagePath;
    public string RootPath => "/";

    public static async ValueTask<bool> CanOpenAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return false;

        try
        {
            await using var stream = OpenImage(imagePath);
            var buffer = new byte[SectorSize];
            for (var index = 0; index < MaxDescriptorSectors; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = (long)(FirstDescriptorSector + index) * SectorSize;
                var read = await ReadExactlyOrLessAsync(stream, buffer, SectorSize, cancellationToken);
                if (read < SectorSize)
                    return false;

                if (!HasStandardIdentifier(buffer))
                    continue;

                var type = buffer[0];
                if (type is 1 or 2)
                    return true;
                if (type == 255)
                    return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }

        return false;
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            if (!File.Exists(_imagePath))
                throw new FileNotFoundException("The ISO image is no longer available.", _imagePath);

            await using var stream = OpenImage(_imagePath);
            var buffer = new byte[SectorSize];
            IsoNode? primaryRoot = null;
            IsoNode? jolietRoot = null;

            for (var index = 0; index < MaxDescriptorSectors; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = (long)(FirstDescriptorSector + index) * SectorSize;
                var read = await ReadExactlyOrLessAsync(stream, buffer, SectorSize, cancellationToken);
                if (read < SectorSize)
                    break;

                if (!HasStandardIdentifier(buffer))
                    continue;

                var type = buffer[0];
                if (type == 1)
                {
                    primaryRoot = ParseRootRecord(buffer, Encoding.ASCII);
                }
                else if (type == 2 && IsJolietDescriptor(buffer))
                {
                    jolietRoot = ParseRootRecord(buffer, Encoding.BigEndianUnicode);
                }
                else if (type == 255)
                {
                    break;
                }
            }

            if (jolietRoot is not null)
            {
                _nameEncoding = Encoding.BigEndianUnicode;
                _root = jolietRoot;
            }
            else if (primaryRoot is not null)
            {
                _nameEncoding = Encoding.ASCII;
                _root = primaryRoot;
            }
            else
            {
                throw new InvalidDataException("The image does not contain a supported ISO9660/Joliet volume descriptor.");
            }

            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task<IReadOnlyList<ExplorerEntry>> ListAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var directory = await ResolveNodeAsync(directoryPath, cancellationToken);
        if (!directory.IsDirectory)
            throw new InvalidOperationException("The requested direct-browse path is not a directory.");

        var nodes = await ReadDirectoryAsync(directory, cancellationToken);
        return nodes
            .OrderByDescending(node => node.IsDirectory)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToExplorerEntry)
            .ToArray();
    }

    public async Task<IReadOnlyList<ExplorerEntry>> SearchAsync(
        string startPath,
        string query,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (maxResults <= 0)
            return Array.Empty<ExplorerEntry>();

        query = query?.Trim() ?? string.Empty;
        if (query.Length == 0)
            return Array.Empty<ExplorerEntry>();

        var start = await ResolveNodeAsync(startPath, cancellationToken);
        if (!start.IsDirectory)
            throw new InvalidOperationException("Search must start from an ISO directory.");

        var results = new List<ExplorerEntry>(Math.Min(maxResults, 256));
        var pending = new Stack<IsoNode>();
        pending.Push(start);

        while (pending.Count > 0 && results.Count < maxResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            var children = await ReadDirectoryAsync(directory, cancellationToken);

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (child.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(ToExplorerEntry(child));
                    if (results.Count >= maxResults)
                        break;
                }

                if (child.IsDirectory)
                    pending.Push(child);
            }
        }

        return results;
    }

    public async Task CopyOutAsync(
        string sourcePath,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Destination directory cannot be empty.", nameof(destinationDirectory));
        if (!Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException($"Destination directory does not exist: {destinationDirectory}");

        var source = await ResolveNodeAsync(sourcePath, cancellationToken);
        if (source.VirtualPath == RootPath)
            throw new InvalidOperationException("Copy out the root by selecting one or more entries instead of the virtual ISO root.");
        if (source.IsMultiExtent)
            throw new NotSupportedException("Multi-extent ISO files are not supported by the first direct-browse provider slice.");

        var safeName = GetSafeWindowsName(source.Name);
        var destinationPath = Path.Combine(destinationDirectory, safeName);
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            throw new IOException($"Destination already exists: {destinationPath}");

        if (!source.IsDirectory)
        {
            await CopySingleFileAsync(source, destinationPath, progress, cancellationToken);
            return;
        }

        var plan = new List<ExtractionItem>();
        var seenRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await BuildExtractionPlanAsync(source, safeName, plan, seenRelativePaths, cancellationToken);
        var totalBytes = plan.Where(item => !item.Node.IsDirectory).Sum(item => (long)item.Node.DataLength);
        long completedBytes = 0;
        var createdRoot = false;

        try
        {
            Directory.CreateDirectory(destinationPath);
            createdRoot = true;

            foreach (var item in plan.Where(item => item.Node.IsDirectory)
                         .OrderBy(item => item.RelativePath.Count(c => c == Path.DirectorySeparatorChar)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(destinationDirectory, item.RelativePath));
            }

            foreach (var item in plan.Where(item => !item.Node.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(destinationDirectory, item.RelativePath);
                await CopyNodeDataAsync(item.Node, target, bytes =>
                {
                    completedBytes += bytes;
                    progress?.Report(totalBytes <= 0 ? 1d : Math.Clamp((double)completedBytes / totalBytes, 0d, 1d));
                }, cancellationToken);
            }

            progress?.Report(1d);
        }
        catch
        {
            if (createdRoot)
            {
                try { Directory.Delete(destinationPath, recursive: true); } catch { }
            }
            throw;
        }
    }

    private async Task CopySingleFileAsync(
        IsoNode source,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Could not resolve the destination directory.");
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.dragon-{Guid.NewGuid():N}.partial");
        long completed = 0;

        try
        {
            await CopyNodeDataAsync(source, temporary, bytes =>
            {
                completed += bytes;
                progress?.Report(source.DataLength == 0 ? 1d : Math.Clamp((double)completed / source.DataLength, 0d, 1d));
            }, cancellationToken);

            File.Move(temporary, destinationPath, overwrite: false);
            progress?.Report(1d);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }

    private async Task CopyNodeDataAsync(
        IsoNode node,
        string targetPath,
        Action<int> onBytesWritten,
        CancellationToken cancellationToken)
    {
        if (node.IsDirectory)
            throw new InvalidOperationException("A directory cannot be copied as file data.");
        if (node.IsMultiExtent)
            throw new NotSupportedException($"Multi-extent ISO file is not supported: {node.Name}");

        await using var input = OpenImage(_imagePath);
        await using var output = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        input.Position = checked((long)node.ExtentLba * SectorSize);
        long remaining = node.DataLength;
        var buffer = new byte[128 * 1024];

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (read <= 0)
                throw new EndOfStreamException($"ISO ended before file data was complete: {node.VirtualPath}");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
            onBytesWritten(read);
        }

        await output.FlushAsync(cancellationToken);
    }

    private async Task BuildExtractionPlanAsync(
        IsoNode node,
        string relativePath,
        List<ExtractionItem> plan,
        HashSet<string> seenRelativePaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!seenRelativePaths.Add(relativePath))
            throw new IOException($"Two ISO entries would map to the same Windows path: {relativePath}");

        plan.Add(new ExtractionItem(node, relativePath));
        if (!node.IsDirectory)
            return;

        var children = await ReadDirectoryAsync(node, cancellationToken);
        foreach (var child in children)
        {
            if (child.IsMultiExtent)
                throw new NotSupportedException($"Multi-extent ISO file is not supported: {child.VirtualPath}");

            var childRelative = Path.Combine(relativePath, GetSafeWindowsName(child.Name));
            await BuildExtractionPlanAsync(child, childRelative, plan, seenRelativePaths, cancellationToken);
        }
    }

    private async Task<IsoNode> ResolveNodeAsync(string virtualPath, CancellationToken cancellationToken)
    {
        var normalized = NormalizeVirtualPath(virtualPath);
        var root = _root ?? throw new InvalidOperationException("ISO provider has not been initialized.");
        if (normalized == RootPath)
            return root;

        var current = root;
        foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!current.IsDirectory)
                throw new DirectoryNotFoundException($"ISO path is not a directory: {current.VirtualPath}");

            var children = await ReadDirectoryAsync(current, cancellationToken);
            current = children.FirstOrDefault(child => child.Name.Equals(part, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"ISO entry was not found: {normalized}");
        }

        return current;
    }

    private async Task<IReadOnlyList<IsoNode>> ReadDirectoryAsync(
        IsoNode directory,
        CancellationToken cancellationToken)
    {
        if (!directory.IsDirectory)
            throw new InvalidOperationException("Cannot enumerate a non-directory ISO record.");
        if (_directoryCache.TryGetValue(directory.VirtualPath, out var cached))
            return cached;

        var nodes = new List<IsoNode>();
        await using var stream = OpenImage(_imagePath);
        var sectorBuffer = new byte[SectorSize];
        long remaining = directory.DataLength;
        long sectorIndex = 0;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytesThisSector = (int)Math.Min(SectorSize, remaining);
            stream.Position = checked((long)directory.ExtentLba * SectorSize + sectorIndex * SectorSize);
            var read = await ReadExactlyOrLessAsync(stream, sectorBuffer, bytesThisSector, cancellationToken);
            if (read < bytesThisSector)
                throw new EndOfStreamException($"ISO directory data ended early: {directory.VirtualPath}");

            var offset = 0;
            while (offset < bytesThisSector)
            {
                var recordLength = sectorBuffer[offset];
                if (recordLength == 0)
                    break;
                if (recordLength < 34 || offset + recordLength > bytesThisSector)
                    throw new InvalidDataException($"Invalid ISO directory record in {directory.VirtualPath}.");

                var record = sectorBuffer.AsSpan(offset, recordLength);
                var identifierLength = record[32];
                if (identifierLength == 1 && (record[33] == 0 || record[33] == 1))
                {
                    offset += recordLength;
                    continue;
                }

                var name = DecodeIdentifier(record.Slice(33, identifierLength), _nameEncoding);
                if (name.Length > 0)
                {
                    var flags = record[25];
                    var node = new IsoNode(
                        name,
                        CombineVirtualPath(directory.VirtualPath, name),
                        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(2, 4)),
                        BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(10, 4)),
                        (flags & DirectoryFlag) != 0,
                        (flags & MultiExtentFlag) != 0,
                        ParseRecordingTime(record));
                    nodes.Add(node);
                }

                offset += recordLength;
            }

            remaining -= bytesThisSector;
            sectorIndex++;
        }

        var immutable = nodes
            .GroupBy(node => node.VirtualPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        _directoryCache.TryAdd(directory.VirtualPath, immutable);
        return immutable;
    }

    private static IsoNode ParseRootRecord(byte[] descriptor, Encoding encoding)
    {
        const int rootOffset = 156;
        var recordLength = descriptor[rootOffset];
        if (recordLength < 34 || rootOffset + recordLength > descriptor.Length)
            throw new InvalidDataException("ISO volume descriptor contains an invalid root directory record.");

        var record = descriptor.AsSpan(rootOffset, recordLength);
        var flags = record[25];
        return new IsoNode(
            "/",
            "/",
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(2, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(10, 4)),
            (flags & DirectoryFlag) != 0,
            false,
            ParseRecordingTime(record));
    }

    private static ExplorerEntry ToExplorerEntry(IsoNode node)
        => new(
            node.Name,
            node.VirtualPath,
            node.IsDirectory ? ExplorerEntryKind.Directory : ExplorerEntryKind.File,
            node.IsDirectory ? null : node.DataLength,
            node.LastWriteTimeUtc,
            IsReparsePoint: false);

    private static string DecodeIdentifier(ReadOnlySpan<byte> identifier, Encoding encoding)
    {
        if (identifier.Length == 0)
            return string.Empty;

        if (ReferenceEquals(encoding, Encoding.BigEndianUnicode) && identifier.Length % 2 != 0)
            identifier = identifier[..^1];

        var name = encoding.GetString(identifier).TrimEnd('\0');
        var version = name.LastIndexOf(';');
        if (version > 0 && name[(version + 1)..].All(char.IsDigit))
            name = name[..version];

        return name.TrimEnd('.').Trim();
    }

    private static DateTimeOffset ParseRecordingTime(ReadOnlySpan<byte> record)
    {
        try
        {
            var year = 1900 + record[18];
            var month = record[19];
            var day = record[20];
            var hour = record[21];
            var minute = record[22];
            var second = record[23];
            var quarterHours = unchecked((sbyte)record[24]);
            var offset = TimeSpan.FromMinutes(quarterHours * 15);
            return new DateTimeOffset(year, month, day, hour, minute, second, offset).ToUniversalTime();
        }
        catch
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    private static bool HasStandardIdentifier(byte[] sector)
        => sector.Length >= 7
            && sector[1] == (byte)'C'
            && sector[2] == (byte)'D'
            && sector[3] == (byte)'0'
            && sector[4] == (byte)'0'
            && sector[5] == (byte)'1'
            && sector[6] == 1;

    private static bool IsJolietDescriptor(byte[] sector)
    {
        if (sector.Length < 91)
            return false;

        return sector[88] == (byte)'%'
            && sector[89] == (byte)'/'
            && sector[90] is (byte)'@' or (byte)'C' or (byte)'E';
    }

    private static string NormalizeVirtualPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return "/";

        var parts = path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var clean = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part == ".")
                continue;
            if (part == "..")
                throw new InvalidOperationException("Parent traversal is not allowed in provider-backed ISO paths.");
            clean.Add(part);
        }

        return clean.Count == 0 ? "/" : "/" + string.Join('/', clean);
    }

    private static string CombineVirtualPath(string parent, string name)
        => parent == "/" ? "/" + name : parent.TrimEnd('/') + "/" + name;

    private static string GetSafeWindowsName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var safe = new string(chars).Trim().TrimEnd('.', ' ');
        if (safe.Length == 0)
            safe = "_";

        var stem = Path.GetFileNameWithoutExtension(safe);
        if (ReservedWindowsNames.Contains(stem))
            safe = "_" + safe;

        return safe;
    }

    private static FileStream OpenImage(string path)
        => new(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.RandomAccess);

    private static async Task<int> ReadExactlyOrLessAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken);
            if (read <= 0)
                break;
            total += read;
        }

        return total;
    }

    private sealed record IsoNode(
        string Name,
        string VirtualPath,
        uint ExtentLba,
        uint DataLength,
        bool IsDirectory,
        bool IsMultiExtent,
        DateTimeOffset LastWriteTimeUtc);

    private sealed record ExtractionItem(IsoNode Node, string RelativePath);
}
