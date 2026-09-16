namespace DragonDiskForge.Core.Providers;

public static class DefaultProviderRegistry
{
    public static ProviderRegistry Create() => new(new ProviderRegistration[]
    {
        new(new Iso9660DirectBrowseProvider(), 100),
        new(new CcdImageProvider(), 95),
        new(new RawPartitionImageProvider(), 90),
        new(new FloppyImageProvider(), 80),
        new(new CueSheetImageProvider(), 70),
        new(new MdsImageProvider(), 60),
        new(new NrgImageProvider(), 50),
        new(new VmdkSparseImageProvider(), 40),
        new(new QcowImageProvider(), 30),
        new(new DmgUdifImageProvider(), 20),
        new(new WimEsdImageProvider(), 10),
        new(new FfuImageProvider(), 5)
    });
}
