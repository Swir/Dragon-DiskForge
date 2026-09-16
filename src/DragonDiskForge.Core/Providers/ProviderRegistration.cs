namespace DragonDiskForge.Core.Providers;

public sealed record ProviderRegistration
{
    public ProviderRegistration(IDiskImageProvider Provider, int Priority = 0)
    {
        this.Provider = Provider ?? throw new ArgumentNullException(nameof(Provider));
        this.Priority = Priority;
        Descriptor = ProviderDescriptor.From(Provider, Priority);
    }

    public IDiskImageProvider Provider { get; }
    public int Priority { get; }
    public ProviderDescriptor Descriptor { get; }
}

public sealed record ProviderDescriptor(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    ProviderCapabilities Capabilities,
    int Priority)
{
    public static ProviderDescriptor From(IDiskImageProvider provider, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var id = ValidateId(provider.Id);
        var displayName = ResolveDisplayName(provider, id);
        var extensions = NormalizeExtensions(provider.Extensions, id);

        return new ProviderDescriptor(
            id,
            displayName,
            extensions,
            provider.GetCapabilities(),
            priority);
    }

    private static string ValidateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Provider id must be non-empty.", nameof(id));

        if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Provider id cannot contain leading or trailing whitespace.", nameof(id));

        if (!id.All(IsProviderIdCharacter))
            throw new ArgumentException(
                "Provider id may contain only letters, digits, '.', '_' and '-'.",
                nameof(id));

        return id;
    }

    private static bool IsProviderIdCharacter(char value)
        => char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-';

    private static string ResolveDisplayName(IDiskImageProvider provider, string fallbackId)
    {
        var candidate = provider switch
        {
            IDirectBrowseProvider direct => direct.DisplayName,
            IPartitionTableProvider partitions => partitions.DisplayName,
            IMediaGeometryProvider media => media.DisplayName,
            ITrackLayoutProvider tracks => tracks.DisplayName,
            IVirtualDiskMetadataProvider virtualDisk => virtualDisk.DisplayName,
            IQcowMetadataProvider qcow => qcow.DisplayName,
            IDmgMetadataProvider dmg => dmg.DisplayName,
            IWimMetadataProvider wim => wim.DisplayName,
            IFfuMetadataProvider ffu => ffu.DisplayName,
            _ => fallbackId
        };

        return string.IsNullOrWhiteSpace(candidate) ? fallbackId : candidate.Trim();
    }

    private static IReadOnlyList<string> NormalizeExtensions(
        IReadOnlyCollection<string>? extensions,
        string providerId)
    {
        if (extensions is null)
            throw new ArgumentException($"Provider '{providerId}' returned a null extension collection.", nameof(extensions));

        var normalized = new List<string>(extensions.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
                throw new ArgumentException($"Provider '{providerId}' declares an empty extension.", nameof(extensions));

            var value = NormalizeExtension(extension);
            if (!IsValidExtension(value))
                throw new ArgumentException($"Provider '{providerId}' declares invalid extension '{extension}'.", nameof(extensions));

            if (!seen.Add(value))
                throw new ArgumentException($"Provider '{providerId}' declares duplicate extension '{value}'.", nameof(extensions));

            normalized.Add(value);
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(normalized.ToArray());
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim();
        return value.StartsWith('.') ? value : "." + value;
    }

    private static bool IsValidExtension(string extension)
    {
        if (extension.Length < 2 || extension[0] != '.')
            return false;

        for (var index = 1; index < extension.Length; index++)
        {
            var value = extension[index];
            if (!char.IsAsciiLetterOrDigit(value) && value is not '.' and not '_' and not '-')
                return false;
        }

        return true;
    }
}
