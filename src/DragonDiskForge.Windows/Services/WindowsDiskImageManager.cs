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
    private const uint StorageTypeUnknown = 0;
    private const int MountedInventoryMaxAttempts = 4;
    private const int MountedInventoryRetryDelayMilliseconds = 100;

    public WindowsDiskImageState? TryGetState(string imagePath)
    {
        var fullPath = NormalizePath(imagePath);
        EnsureSupportedExtension(fullPath);
        EnsureWindows();

        var scope = CreateScope();
        ManagementObject image;
        try
        {
            image = OpenDiskImage(scope, fullPath);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound)
        {
            return null;
        }

        using (image)
            return ToState(image);
    }

    public IReadOnlyList<WindowsDiskImageState> GetMounted()
    {
        EnsureWindows();
        var scope = CreateScope();

        // StorageWMI can invalidate an MSFT_Volume/MSFT_DiskImage association while a
        // detach is settling. Retry the complete read-only snapshot on provider-level
        // NotFound only; persistent failures still surface instead of being hidden as
        // an empty mounted-image inventory.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return GetMountedSnapshot(scope);
            }
            catch (ManagementException ex) when (
                ex.ErrorCode == ManagementStatus.NotFound
                && attempt < MountedInventoryMaxAttempts - 1)
            {
                Thread.Sleep(MountedInventoryRetryDelayMilliseconds * (attempt + 1));
            }
        }
    }

    private static IReadOnlyList<WindowsDiskImageState> GetMountedSnapshot(ManagementScope scope)
    {
        var result = new Dictionary<string, WindowsDiskImageState>(StringComparer.OrdinalIgnoreCase);

        // MSFT_DiskImage is a dynamic provider class: class enumeration/WQL does not
        // enumerate image instances reliably. Discover attached images through the
        // provider's DiskImage<->Volume association without hand-building ASSOCIATORS
        // queries: System.Management's RelatedObjectQuery parser is stricter than the
        // Storage provider and rejected a valid provider query on the Windows CI host.
        using var volumeSearcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery("SELECT * FROM MSFT_Volume"));
        using var volumes = volumeSearcher.Get();

        foreach (ManagementObject volume in volumes)
        {
            using (volume)
            using (var images = volume.GetRelated("MSFT_DiskImage"))
            {
                foreach (ManagementObject image in images)
                {
                    using (image)
                    {
                        if (!ReadBoolean(image, "Attached"))
                            continue;

                        var path = Convert.ToString(image["ImagePath"], CultureInfo.InvariantCulture);
                        if (string.IsNullOrWhiteSpace(path) || !IsSupportedExtension(path))
                            continue;

                        var state = ToState(image);
                        result[state.ImagePath] = state;
                    }
                }
            }
        }

        return result.Values
            .OrderBy(x => x.ImagePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Mount(string imagePath, bool readOnly, bool noDriveLetter)
    {
        var fullPath = NormalizePath(imagePath);
        EnsureSupportedExtension(fullPath);
        EnsureWindows();

        var scope = CreateScope();
        using var image = OpenDiskImage(scope, fullPath);
        using var input = image.GetMethodParameters("Mount");
        input["Access"] = readOnly ? AccessReadOnly : AccessReadWrite;
        input["NoDriveLetter"] = noDriveLetter;

        using var output = image.InvokeMethod("Mount", input, null);
        EnsureSuccess(output, "mount");
    }

    public void Dismount(string imagePath)
    {
        var fullPath = NormalizePath(imagePath);
        EnsureSupportedExtension(fullPath);
        EnsureWindows();

        var scope = CreateScope();
        using var image = OpenDiskImage(scope, fullPath);
        using var output = image.InvokeMethod("Dismount", null, null);
        EnsureSuccess(output, "dismount");
    }

    private static ManagementScope CreateScope()
    {
        var scope = new ManagementScope(StorageNamespace);
        scope.Connect();
        return scope;
    }

    private static ManagementObject OpenDiskImage(ManagementScope scope, string fullPath)
    {
        // StorageWMI resolves MSFT_DiskImage through an instance object path. The
        // StorageType key must be Unknown (0) for provider-side type resolution;
        // after Get(), the returned instance exposes the actual ISO/VHD/VHDX type.
        var escapedPath = EscapeManagementPathKey(fullPath);
        var path = new ManagementPath(
            $"MSFT_DiskImage.ImagePath=\"{escapedPath}\",StorageType={StorageTypeUnknown}");
        var image = new ManagementObject(scope, path, null);

        try
        {
            image.Get();
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static WindowsDiskImageState ToState(ManagementObject image)
    {
        var path = Convert.ToString(image["ImagePath"], CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException("Windows Storage returned a disk image without ImagePath.");
        var attached = ReadBoolean(image, "Attached");
        var devicePath = Convert.ToString(image["DevicePath"], CultureInfo.InvariantCulture);

        return new WindowsDiskImageState(
            Path.GetFullPath(path),
            attached,
            string.IsNullOrWhiteSpace(devicePath) ? null : devicePath,
            attached ? ReadDriveLetters(image, path) : Array.Empty<string>());
    }

    private static IReadOnlyList<string> ReadDriveLetters(ManagementObject image, string imagePath)
    {
        // MSFT_DiskImage.Number/MSFT_Partition.DriveLetter can expose a remembered
        // partition letter even when Mount(NoDriveLetter=true) has deliberately not
        // published that letter in the current namespace. Conversely, the forward
        // DiskImage->Volume relation is not reliable for dynamically opened VHDX
        // instances on hosted Windows. Resolve the live namespace from the direction
        // already proven by mounted inventory: Volume -> related DiskImage, then bind
        // each non-empty volume DriveLetter back to the exact canonical image path.
        var expectedPath = Path.GetFullPath(imagePath);
        var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var volumeSearcher = new ManagementObjectSearcher(
            image.Scope,
            new ObjectQuery("SELECT * FROM MSFT_Volume"));
        using var volumes = volumeSearcher.Get();

        foreach (ManagementObject volume in volumes)
        {
            using (volume)
            {
                var driveLetter = Convert.ToString(volume["DriveLetter"], CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(driveLetter))
                    continue;

                using var relatedImages = volume.GetRelated("MSFT_DiskImage");
                foreach (ManagementObject relatedImage in relatedImages)
                {
                    using (relatedImage)
                    {
                        if (!ReadBoolean(relatedImage, "Attached"))
                            continue;

                        var relatedPath = Convert.ToString(
                            relatedImage["ImagePath"],
                            CultureInfo.InvariantCulture);
                        if (string.IsNullOrWhiteSpace(relatedPath))
                            continue;

                        string normalizedRelatedPath;
                        try
                        {
                            normalizedRelatedPath = Path.GetFullPath(relatedPath);
                        }
                        catch (Exception ex) when (
                            ex is ArgumentException or NotSupportedException or PathTooLongException)
                        {
                            continue;
                        }

                        if (!string.Equals(
                                normalizedRelatedPath,
                                expectedPath,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        AddDriveLetter(letters, driveLetter);
                        break;
                    }
                }
            }
        }

        return letters.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddDriveLetter(HashSet<string> letters, object? rawDriveLetter)
    {
        var driveLetter = Convert.ToString(rawDriveLetter, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(driveLetter))
            return;

        letters.Add(driveLetter.EndsWith(':') ? driveLetter : $"{driveLetter}:");
    }

    private static bool ReadBoolean(ManagementBaseObject value, string propertyName)
        => value[propertyName] is bool boolean && boolean;

    private static bool IsSupportedExtension(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".iso" or ".vhd" or ".vhdx";

    private static void EnsureSupportedExtension(string path)
    {
        if (!IsSupportedExtension(path))
            throw new NotSupportedException("Native Windows disk-image management supports ISO, VHD and VHDX only.");
    }

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
        if (output is null || output["ReturnValue"] is null)
        {
            throw new InvalidOperationException(
                $"Windows Storage did not return a status code for the {operation} operation.");
        }

        uint code;
        try
        {
            code = Convert.ToUInt32(output["ReturnValue"], CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException(
                $"Windows Storage returned an invalid status code for the {operation} operation.",
                ex);
        }

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
