namespace DragonDiskForge.Core.Providers;

/// <summary>
/// Creates the canonical built-in provider registry shared by first-party front ends.
/// Keeping registration order and priorities in Core prevents the desktop app, CLI and
/// future automation surfaces from drifting into different capability sets.
/// </summary>
public static class ProviderRegistryFactory
{
    public static ProviderRegistry CreateDefault()
        => new(CreateDefaultRegistrations());

    public static ProviderRegistration[] CreateDefaultRegistrations()
        =>
        [
            new ProviderRegistration(new Iso9660DirectBrowseProvider(), Priority: 100),
            new ProviderRegistration(new CcdImageProvider(), Priority: 95),
            new ProviderRegistration(new RawPartitionImageProvider(), Priority: 90),
            new ProviderRegistration(new FloppyImageProvider(), Priority: 80),
            new ProviderRegistration(new CueSheetImageProvider(), Priority: 70),
            new ProviderRegistration(new MdsImageProvider(), Priority: 60),
            new ProviderRegistration(new NrgImageProvider(), Priority: 50),
            new ProviderRegistration(new VmdkSparseImageProvider(), Priority: 40),
            new ProviderRegistration(new QcowImageProvider(), Priority: 30),
            new ProviderRegistration(new DmgUdifImageProvider(), Priority: 20),
            new ProviderRegistration(new WimEsdImageProvider(), Priority: 10),
            new ProviderRegistration(new FfuImageProvider(), Priority: 5)
        ];
}
