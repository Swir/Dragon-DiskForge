using DragonDiskForge.Windows.Services;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Dragon DiskForge shell integration is available on Windows only.");
    return 3;
}

try
{
    if (args.Length == 0 || args[0] is "--help" or "-h")
    {
        PrintHelp();
        return 0;
    }

    var command = args[0].ToLowerInvariant();
    var applicationPath = ResolveApplicationPath(args.Skip(1).ToArray());
    var service = new WindowsShellIntegrationService();

    switch (command)
    {
        case "register":
        {
            var status = service.Register(applicationPath);
            Console.WriteLine($"Registered Dragon DiskForge for {status.RegisteredExtensionCount}/{status.ExpectedExtensionCount} image extensions.");
            Console.WriteLine("Windows defaults were not changed; the integration adds Open With support and an explicit context-menu verb only.");
            return status.IsRegistered ? 0 : 3;
        }
        case "unregister":
            service.Unregister();
            Console.WriteLine("Removed Dragon DiskForge-owned per-user shell integration keys.");
            return 0;
        case "status":
        {
            var status = service.GetStatus(applicationPath);
            Console.WriteLine(status.IsRegistered ? "Registered" : "Not registered");
            Console.WriteLine($"Extensions: {status.RegisteredExtensionCount}/{status.ExpectedExtensionCount}");
            Console.WriteLine($"Application: {status.ApplicationPath}");
            return status.IsRegistered ? 0 : 4;
        }
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            PrintHelp();
            return 2;
    }
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidOperationException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}

static string ResolveApplicationPath(string[] options)
{
    string? explicitPath = null;
    for (var index = 0; index < options.Length; index++)
    {
        if (!options[index].Equals("--app", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown option '{options[index]}'.");
        if (++index >= options.Length || string.IsNullOrWhiteSpace(options[index]))
            throw new ArgumentException("--app requires a path to DragonDiskForge.App.exe.");
        explicitPath = options[index];
    }

    if (!string.IsNullOrWhiteSpace(explicitPath))
        return explicitPath;

    return Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        WindowsShellIntegrationService.ApplicationExecutableName));
}

static void PrintHelp()
{
    Console.WriteLine("""
Dragon DiskForge Windows shell integration

Usage:
  dragon-diskforge-shell register [--app <DragonDiskForge.App.exe>]
  dragon-diskforge-shell unregister [--app <DragonDiskForge.App.exe>]
  dragon-diskforge-shell status [--app <DragonDiskForge.App.exe>]

The registration is per-user (HKCU) and does not replace Windows UserChoice/default-app settings.
It adds Dragon DiskForge to Open With discovery and an explicit "Open with Dragon DiskForge"
context-menu action for the image extensions supported by the application. Unregister removes only
Dragon DiskForge-owned keys.
""");
}
