using DragonDiskForge.Core.Services;
using DragonDiskForge.Windows.Services;
using Microsoft.Win32;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("SKIP  Windows shell integration smoke tests require Windows.");
    return;
}

var tempRoot = Path.Combine(Path.GetTempPath(), $"dragon-shell-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);
var appPath = Path.Combine(tempRoot, WindowsShellIntegrationService.ApplicationExecutableName);
await File.WriteAllBytesAsync(appPath, [0x4d, 0x5a]);

var classesPath = $@"Software\DragonDiskForge.Tests\{Guid.NewGuid():N}\Classes";
var parentPath = classesPath[..classesPath.LastIndexOf("\\Classes", StringComparison.Ordinal)];
var service = new WindowsShellIntegrationService(Registry.CurrentUser, classesPath);

try
{
    Check(WindowsShellIntegrationService.SupportedExtensions.Count >= 18, "shell integration covers the full supported extension set");
    Check(
        WindowsShellIntegrationService.SupportedExtensions.SequenceEqual(
            SupportedFormats.All.SelectMany(x => x.Extensions).Select(x => x.ToLowerInvariant()).Distinct().OrderBy(x => x),
            StringComparer.OrdinalIgnoreCase),
        "shell extension set is derived from canonical SupportedFormats");

    var supportedImage = Path.Combine(tempRoot, "launch.iso");
    var unsupported = Path.Combine(tempRoot, "launch.txt");
    await File.WriteAllTextAsync(supportedImage, "image");
    await File.WriteAllTextAsync(unsupported, "text");
    Check(ImageLaunchArgumentResolver.Resolve([supportedImage]) == Path.GetFullPath(supportedImage), "startup resolver accepts an existing supported image");
    Check(ImageLaunchArgumentResolver.Resolve([unsupported]) is null, "startup resolver rejects unsupported extensions");
    Check(ImageLaunchArgumentResolver.Resolve(["--ignored", supportedImage]) == Path.GetFullPath(supportedImage), "startup resolver ignores switches and resolves the image operand");
    Check(ImageLaunchArgumentResolver.Resolve([Path.Combine(tempRoot, "missing.iso")]) is null, "startup resolver rejects missing images");

    var initial = service.GetStatus(appPath);
    Check(!initial.IsRegistered && initial.RegisteredExtensionCount == 0, "isolated shell integration starts unregistered");

    var registered = service.Register(appPath);
    Check(registered.IsRegistered, "per-user shell registration reports complete");
    Check(registered.RegisteredExtensionCount == registered.ExpectedExtensionCount, "every supported extension receives the Dragon context-menu verb");
    Check(registered.OpenCommand == $"\"{Path.GetFullPath(appPath)}\" \"%1\"", "registered open command safely quotes executable and image path placeholder");

    using (var classes = Registry.CurrentUser.OpenSubKey(classesPath, writable: true)
        ?? throw new InvalidOperationException("Isolated classes root was not created."))
    {
        using var application = classes.OpenSubKey($@"Applications\{WindowsShellIntegrationService.ApplicationExecutableName}");
        Check((application?.GetValue("FriendlyAppName") as string) == "Dragon DiskForge", "Applications registration has the expected friendly name");

        using var supportedTypes = classes.OpenSubKey($@"Applications\{WindowsShellIntegrationService.ApplicationExecutableName}\SupportedTypes");
        Check(supportedTypes is not null && supportedTypes.GetValueNames().Length == WindowsShellIntegrationService.SupportedExtensions.Count,
            "Open With SupportedTypes contains every canonical extension");

        foreach (var extension in WindowsShellIntegrationService.SupportedExtensions)
        {
            using var verb = classes.OpenSubKey($@"SystemFileAssociations\{extension}\shell\{WindowsShellIntegrationService.ContextMenuVerb}");
            Check((verb?.GetValue("MUIVerb") as string) == WindowsShellIntegrationService.ContextMenuText,
                $"{extension} context menu uses the explicit Dragon label");
        }

        Check(classes.OpenSubKey(@".iso\UserChoice") is null, "registration does not create a UserChoice override");
        Check(classes.GetValue(".iso") is null, "registration does not replace a Classes-root default association");

        using var otherVerb = classes.CreateSubKey(@"SystemFileAssociations\.iso\shell\OtherApp.Verb", writable: true);
        otherVerb?.SetValue("MUIVerb", "Other app", RegistryValueKind.String);
    }

    var repeated = service.Register(appPath);
    Check(repeated.IsRegistered, "registration is idempotent");

    service.Unregister();
    var removed = service.GetStatus(appPath);
    Check(!removed.IsRegistered && removed.RegisteredExtensionCount == 0, "unregister removes all Dragon-owned verbs");
    using (var classes = Registry.CurrentUser.OpenSubKey(classesPath, writable: false))
    {
        Check(classes?.OpenSubKey($@"Applications\{WindowsShellIntegrationService.ApplicationExecutableName}") is null,
            "unregister removes the Dragon Applications registration");
        Check(classes?.OpenSubKey(@"SystemFileAssociations\.iso\shell\OtherApp.Verb") is not null,
            "unregister preserves unrelated shell verbs");
    }

    service.Unregister();
    Check(true, "unregister is idempotent");
    Console.WriteLine("\nDragon DiskForge Windows shell integration smoke tests passed.");
}
finally
{
    try { Registry.CurrentUser.DeleteSubKeyTree(parentPath, throwOnMissingSubKey: false); } catch { }
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

static void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL  {description}");
    Console.WriteLine($"PASS  {description}");
}
