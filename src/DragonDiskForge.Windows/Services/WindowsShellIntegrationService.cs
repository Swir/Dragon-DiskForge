using DragonDiskForge.Core.Services;
using Microsoft.Win32;

namespace DragonDiskForge.Windows.Services;

public sealed record WindowsShellIntegrationStatus(
    bool IsRegistered,
    string ApplicationPath,
    string OpenCommand,
    int RegisteredExtensionCount,
    int ExpectedExtensionCount);

public sealed class WindowsShellIntegrationService
{
    public const string ApplicationExecutableName = "DragonDiskForge.App.exe";
    public const string ContextMenuVerb = "DragonDiskForge.Open";
    public const string ContextMenuText = "Open with Dragon DiskForge";

    private readonly RegistryKey _root;
    private readonly string _classesPath;

    public WindowsShellIntegrationService()
        : this(Registry.CurrentUser, @"Software\Classes")
    {
    }

    public WindowsShellIntegrationService(RegistryKey root, string classesPath)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        if (string.IsNullOrWhiteSpace(classesPath))
            throw new ArgumentException("Classes registry path cannot be empty.", nameof(classesPath));
        _classesPath = classesPath.Trim().TrimEnd('\\');
    }

    public static IReadOnlyList<string> SupportedExtensions { get; } = SupportedFormats.All
        .SelectMany(x => x.Extensions)
        .Select(NormalizeExtension)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public WindowsShellIntegrationStatus Register(string applicationPath)
    {
        var normalizedApplicationPath = NormalizeApplicationPath(applicationPath, requireExists: true);
        var command = BuildOpenCommand(normalizedApplicationPath);

        using var classes = _root.CreateSubKey(_classesPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the per-user Windows Classes registry root.");

        using (var application = classes.CreateSubKey($@"Applications\{ApplicationExecutableName}", writable: true))
        {
            if (application is null)
                throw new InvalidOperationException("Could not create the Dragon DiskForge application registration.");

            application.SetValue("FriendlyAppName", "Dragon DiskForge", RegistryValueKind.String);
            using var defaultIcon = application.CreateSubKey("DefaultIcon", writable: true);
            defaultIcon?.SetValue(null, normalizedApplicationPath, RegistryValueKind.String);
            using var openCommand = application.CreateSubKey(@"shell\open\command", writable: true);
            openCommand?.SetValue(null, command, RegistryValueKind.String);
            using var supportedTypes = application.CreateSubKey("SupportedTypes", writable: true);
            if (supportedTypes is null)
                throw new InvalidOperationException("Could not create the Dragon DiskForge SupportedTypes registration.");

            foreach (var extension in SupportedExtensions)
                supportedTypes.SetValue(extension, string.Empty, RegistryValueKind.String);
        }

        foreach (var extension in SupportedExtensions)
        {
            using var verb = classes.CreateSubKey(
                $@"SystemFileAssociations\{extension}\shell\{ContextMenuVerb}",
                writable: true);
            if (verb is null)
                throw new InvalidOperationException($"Could not create shell integration for '{extension}'.");

            verb.SetValue("MUIVerb", ContextMenuText, RegistryValueKind.String);
            verb.SetValue("Icon", normalizedApplicationPath, RegistryValueKind.String);
            verb.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);
            using var verbCommand = verb.CreateSubKey("command", writable: true);
            verbCommand?.SetValue(null, command, RegistryValueKind.String);
        }

        return GetStatus(normalizedApplicationPath);
    }

    public WindowsShellIntegrationStatus GetStatus(string applicationPath)
    {
        var normalizedApplicationPath = NormalizeApplicationPath(applicationPath, requireExists: false);
        var expectedCommand = BuildOpenCommand(normalizedApplicationPath);
        var registered = 0;
        var appMatches = false;

        using var classes = _root.OpenSubKey(_classesPath, writable: false);
        if (classes is not null)
        {
            using var appCommand = classes.OpenSubKey($@"Applications\{ApplicationExecutableName}\shell\open\command");
            appMatches = string.Equals(
                appCommand?.GetValue(null) as string,
                expectedCommand,
                StringComparison.OrdinalIgnoreCase);

            foreach (var extension in SupportedExtensions)
            {
                using var command = classes.OpenSubKey(
                    $@"SystemFileAssociations\{extension}\shell\{ContextMenuVerb}\command");
                if (string.Equals(
                    command?.GetValue(null) as string,
                    expectedCommand,
                    StringComparison.OrdinalIgnoreCase))
                {
                    registered++;
                }
            }
        }

        return new WindowsShellIntegrationStatus(
            appMatches && registered == SupportedExtensions.Count,
            normalizedApplicationPath,
            expectedCommand,
            registered,
            SupportedExtensions.Count);
    }

    public void Unregister()
    {
        using var classes = _root.OpenSubKey(_classesPath, writable: true);
        if (classes is null)
            return;

        classes.DeleteSubKeyTree($@"Applications\{ApplicationExecutableName}", throwOnMissingSubKey: false);
        foreach (var extension in SupportedExtensions)
        {
            classes.DeleteSubKeyTree(
                $@"SystemFileAssociations\{extension}\shell\{ContextMenuVerb}",
                throwOnMissingSubKey: false);
        }
    }

    public static string BuildOpenCommand(string applicationPath)
    {
        var normalized = NormalizeApplicationPath(applicationPath, requireExists: false);
        return $"\"{normalized}\" \"%1\"";
    }

    private static string NormalizeApplicationPath(string applicationPath, bool requireExists)
    {
        if (string.IsNullOrWhiteSpace(applicationPath))
            throw new ArgumentException("Application path cannot be empty.", nameof(applicationPath));
        if (applicationPath.IndexOf('\0') >= 0 || applicationPath.Contains('"'))
            throw new ArgumentException("Application path contains characters that cannot be safely registered.", nameof(applicationPath));

        var fullPath = Path.GetFullPath(applicationPath);
        if (!Path.GetFileName(fullPath).Equals(ApplicationExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Shell integration must target {ApplicationExecutableName}.",
                nameof(applicationPath));
        }

        if (requireExists && !File.Exists(fullPath))
            throw new FileNotFoundException("Dragon DiskForge application executable was not found.", fullPath);

        return fullPath;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidOperationException("Supported image extension cannot be empty.");
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed.ToLowerInvariant() : $".{trimmed.ToLowerInvariant()}";
    }
}
