namespace DragonDiskForge.Core.Providers;

[Flags]
public enum ProviderCapabilities
{
    None = 0,
    Inspect = 1 << 0,
    DirectBrowse = 1 << 1,
    Search = 1 << 2,
    CopyOut = 1 << 3
}

public static class ProviderCapabilityExtensions
{
    public static ProviderCapabilities GetCapabilities(this IDiskImageProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var capabilities = ProviderCapabilities.Inspect;
        if (provider is IDirectBrowseProvider)
        {
            capabilities |= ProviderCapabilities.DirectBrowse
                | ProviderCapabilities.Search
                | ProviderCapabilities.CopyOut;
        }

        return capabilities;
    }
}
