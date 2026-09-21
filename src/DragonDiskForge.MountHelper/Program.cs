using System.Security.Principal;
using DragonDiskForge.Windows.Services;

if (!OperatingSystem.IsWindows())
    return 3;

try
{
    if (args.Length == 0 || args[0] is "--help" or "-h")
        return 0;

    var command = args[0].ToLowerInvariant();
    var options = ParseOptions(args.Skip(1).ToArray());
    var imagePath = RequireImagePath(options);

    if (!IsSupportedImage(imagePath))
        return 2;

    var manager = new WindowsDiskImageManager();

    switch (command)
    {
        case "mount":
        {
            var readOnly = !options.ContainsKey("--read-write");
            var noDriveLetter = options.ContainsKey("--no-drive-letter");

            if (!readOnly && Path.GetExtension(imagePath).Equals(".iso", StringComparison.OrdinalIgnoreCase))
                readOnly = true;

            manager.Mount(imagePath, readOnly, noDriveLetter);
            return 0;
        }
        case "unmount":
            manager.Dismount(imagePath);
            return 0;
        default:
            return 2;
    }
}
catch (UnauthorizedAccessException)
{
    return 5;
}
catch (Exception ex) when (ex is ArgumentException
    or FileNotFoundException
    or InvalidOperationException
    or System.Management.ManagementException)
{
    return 3;
}

static Dictionary<string, string?> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index++)
    {
        var option = args[index];
        switch (option.ToLowerInvariant())
        {
            case "--image":
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                    throw new ArgumentException("--image requires a path.");
                result[option] = args[index];
                break;
            case "--read-only":
            case "--read-write":
            case "--no-drive-letter":
                result[option] = null;
                break;
            default:
                throw new ArgumentException($"Unknown option '{option}'.");
        }
    }

    if (result.ContainsKey("--read-only") && result.ContainsKey("--read-write"))
        throw new ArgumentException("Choose either --read-only or --read-write, not both.");

    return result;
}

static string RequireImagePath(Dictionary<string, string?> options)
{
    if (!options.TryGetValue("--image", out var value) || string.IsNullOrWhiteSpace(value))
        throw new ArgumentException("--image is required.");

    var fullPath = Path.GetFullPath(value);
    if (!File.Exists(fullPath))
        throw new FileNotFoundException("Disk image was not found.", fullPath);

    return fullPath;
}

static bool IsSupportedImage(string path)
    => Path.GetExtension(path).ToLowerInvariant() is ".iso" or ".vhd" or ".vhdx";
