namespace DragonDiskForge.Core.Providers;

public sealed record ProviderRegistration(
    IDiskImageProvider Provider,
    int Priority = 0)
{
    public ProviderDescriptor Descriptor => ProviderDescriptor.From(Provider, Priority);
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

        var displayName = provider switch
        {
            IDirectBrowseProvider direct => direct.DisplayName,
            IPartitionTableProvider partitions => partitions.DisplayName,
            IMediaGeometryProvider media => media.DisplayName,
            ITrackLayoutProvider tracks => tracks.DisplayName,
            IVirtualDiskMetadataProvider virtualDisk => virtualDisk.DisplayName,
            IQcowMetadataProvider qcow => qcow.DisplayName,
            IDmgMetadataProvider dmg => dmg.DisplayName,
            _ => provider.Id
        };

        var extensions = provider.Extensions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProviderDescriptor(
            provider.Id,
            displayName,
            extensions,
            provider.GetCapabilities(),
            priority);
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim();
        return value.StartsWith('.') ? value : "." + value;
    }
}
