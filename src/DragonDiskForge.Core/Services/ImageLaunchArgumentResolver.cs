namespace DragonDiskForge.Core.Services;

public static class ImageLaunchArgumentResolver
{
    public static string? Resolve(
        IEnumerable<string>? arguments,
        Func<string, bool>? fileExists = null)
    {
        if (arguments is null)
            return null;

        fileExists ??= File.Exists;

        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument)
                || argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string fullPath;
            try
            {
                var candidate = argument.Trim();
                if (candidate.Length >= 2 && candidate[0] == '"' && candidate[^1] == '"')
                    candidate = candidate[1..^1];

                if (string.IsNullOrWhiteSpace(candidate) || candidate.IndexOf('\0') >= 0)
                    continue;

                fullPath = Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (SupportedFormats.FromPath(fullPath) is null || !fileExists(fullPath))
                continue;

            return fullPath;
        }

        return null;
    }
}
