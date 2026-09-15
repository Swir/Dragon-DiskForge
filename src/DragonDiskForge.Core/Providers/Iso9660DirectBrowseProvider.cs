using DragonDiskForge.Core.Services;

namespace DragonDiskForge.Core.Providers;

public sealed class Iso9660DirectBrowseProvider : IDirectBrowseProvider
{
    private static readonly string[] SupportedExtensions = [".iso"];

    public string Id => "iso9660";

    public IReadOnlyCollection<string> Extensions => SupportedExtensions;

    public ValueTask<bool> CanHandleAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
        => Iso9660DirectImageExplorer.CanOpenAsync(imagePath, cancellationToken);

    public async ValueTask<IDirectImageExplorer> OpenAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var explorer = new Iso9660DirectImageExplorer(imagePath);
        await explorer.InitializeAsync(cancellationToken);
        return explorer;
    }
}
