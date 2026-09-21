using System.Globalization;
using System.Management;

namespace DragonDiskForge.Windows.Services;

public sealed record WindowsDiskImageState(
    string ImagePath,
    bool Attached,
    string? DevicePath,
    IReadOnlyList<string> DriveLetters);

/// <summary>
/// Native Windows Storage WMI bridge for ISO/VHD/VHDX state and mount operations.
/// This deliberately avoids spawning PowerShell from the shipped desktop application.
/// </summary>
public sealed class WindowsDiskImageManager
{
    private const string StorageNamespace = @"\\.\root\Microsoft\Windows\Storage";
    private const ushort AccessReadWrite = 2;
    private const ushort AccessReadOnly = 3;

    public WindowsDiskImageState? TryGetState(string imagePath)
    {
        var fullPath = NormalizePath(imagePath);
        EnsureWindows();

        var scope = CreateScope();
        foreach (var image in QueryDiskImages(scope))
        {
            using (image)
            {
                var candidate = Convert.ToString(image["ImagePath"], CultureInfo.InvariantCulture);
                if (!string.Equals(candidate, fullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                return ToState(scope, image);
            }
        }

        return null;
    }

    public IReadOnlyList<WindowsDiskImageState> GetMounted()
    {
        EnsureWindows();
        var scope = CreateScope();
        var result = new List<WindowsDiskImageState>();

        foreach (var image in QueryDiskImages(scope))
        {
            using (image)
            {
                if (!ReadBoolean(image, "Attached"))
                    continue;

                var path = Convert.ToString(image["ImagePath"], CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                result.Add(ToState(scope, image));
            }
        }

        return result
            .OrderBy(x => x.ImagePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Mount(string imagePath, bool readOnly, bool noDriveLetter)
    {
        var fullPath = NormalizePath(imagePath);
        EnsureWindows();

        var scope = CreateScope();
        using var image = CreateDiskImage(scope, fullPath);
        using var input = image.GetMethodParameters("Mount");
        input["Access"] = readOnly ? AccessReadOnly : AccessReadWrite;
        input["NoDriveLetter"] = noDriveLetter;

        using var output = image.InvokeMethod("Mount", input, null);
        EnsureSuccess(output, "mount");
    }

    public void Dismount(string imagePath)
    {
        var fullPath = NormalizePath(imagePath);
        EnsureWindows();

        var scope = CreateScope();
        using var image = CreateDiskImage(scope, fullPath);
        using var output = image.InvokeMethod("Dismount", null, null);
        EnsureSuccess(output, "dismount");
    }

    private static ManagementScope CreateScope()
    {
        var scope = new ManagementScope(StorageNamespace);
        scope.Connect();
        return scope;
    }

    private static IEnumerable<ManagementObject> QueryDiskImages(ManagementScope scope)
    {
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT ImagePath, StorageType, DevicePath, Attached FROM MSFT_DiskImage"));

        foreach (ManagementObject image in searcher.Get())
            yield return image;
    }

    private static ManagementObject CreateDiskImage(ManagementScope scope, string fullPath)
    {
        var storageType = GetStorageType(fullPath);
        var escapedPath = EscapeManagementPathKey(fullPath);
        var path = new ManagementPath(
            $"MSFT_DiskImage.ImagePath=\"{escapedPath}\",StorageType={storageType}");
        return new ManagementObject(scope, path, null);
    }

    private static WindowsDiskImageState ToState(ManagementScope scope, ManagementObject image)
    {
        var path = Convert.ToString(image["ImagePath"], CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException("Windows Storage returned a disk image without ImagePath.");
        var attached = ReadBoolean(image, "Attached");
        var devicePath = Convert.ToString(image["DevicePath"], CultureInfo.InvariantCulture);

        return new WindowsDiskImageState(
            Path.GetFullPath(path),
            attached,
            string.IsNullOrWhiteSpace(devicePath) ? null : devicePath,
            attached ? ReadDriveLetters(scope, image) : Array.Empty<string>());
    }

    private static IReadOnlyList<string> ReadDriveLetters(ManagementScope scope, ManagementObject image)
    {
        var relativePath = image.Path.RelativePath;
        if (string.IsNullOrWhiteSpace(relativePath))
            return Array.Empty<string>();

        var query = new RelatedObjectQuery(
            $"ASSOCIATORS OF {{{relativePath}}} WHERE AssocClass=MSFT_DiskImageToVolume ResultClass=MSFT_Volume");
        using var searcher = new ManagementObjectSearcher(scope, query);
        var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ManagementObject volume in searcher.Get())
        {
            using (volume)
            {
                var driveLetter = Convert.ToString(volume["DriveLetter"], CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(driveLetter))
                    continue;

                letters.Add(driveLetter.EndsWith(':') ? driveLetter : $"{driveLetter}:");
            }
        }

        return letters.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ReadBoolean(ManagementBaseObject value, string propertyName)
        => value[propertyName] is bool boolean && boolean;

    private static uint GetStorageType(string fullPath)
        => Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".iso" => 1u,
            ".vhd" => 2u,
            ".vhdx" => 3u,
            _ => throw new NotSupportedException("Native Windows disk-image management supports ISO, VHD and VHDX only.")
        };

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Disk image path cannot be empty.", nameof(path));
        if (path.IndexOf('\0') >= 0)
            throw new ArgumentException("Disk image path contains an invalid NUL character.", nameof(path));

        return Path.GetFullPath(path);
    }

    private static string EscapeManagementPathKey(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void EnsureSuccess(ManagementBaseObject? output, string operation)
    {
        var raw = output?["ReturnValue"];
        var code = raw is null ? 0u : Convert.ToUInt32(raw, CultureInfo.InvariantCulture);
        if (code == 0)
            return;

        var signed = unchecked((int)code);
        if (signed == unchecked((int)0x80070005) || code == 5u)
            throw new UnauthorizedAccessException($"Windows denied permission to {operation} the disk image.");

        throw new InvalidOperationException(
            $"Windows Storage could not {operation} the disk image (0x{code:X8}).");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native disk-image management is available only on Windows.");
    }
}
