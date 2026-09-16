namespace DragonDiskForge.Core.Providers;

[Flags]
public enum ProviderCapabilities
{
    None = 0,
    Inspect = 1 << 0,
    DirectBrowse = 1 << 1,
    Search = 1 << 2,
    CopyOut = 1 << 3,
    PartitionTable = 1 << 4,
    MediaGeometry = 1 << 5,
    TrackLayout = 1 << 6,
    VirtualDiskMetadata = 1 << 7,
    ContainerMetadata = 1 << 8
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

        if (provider is IPartitionTableProvider)
            capabilities |= ProviderCapabilities.PartitionTable;

        if (provider is IMediaGeometryProvider)
            capabilities |= ProviderCapabilities.MediaGeometry;

        if (provider is ITrackLayoutProvider)
            capabilities |= ProviderCapabilities.TrackLayout;

        if (provider is IVirtualDiskMetadataProvider or IQcowMetadataProvider or IDmgMetadataProvider)
            capabilities |= ProviderCapabilities.VirtualDiskMetadata;

        if (provider is IWimMetadataProvider or IFfuMetadataProvider)
            capabilities |= ProviderCapabilities.ContainerMetadata;

        return capabilities;
    }
}
