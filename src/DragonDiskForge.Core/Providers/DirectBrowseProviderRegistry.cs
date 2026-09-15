namespace DragonDiskForge.Core.Providers;

public sealed class DirectBrowseProviderRegistry
{
    private readonly IReadOnlyList<IDirectBrowseProvider> _providers;

    public DirectBrowseProviderRegistry(IEnumerable<IDirectBrowseProvider> providers)
        => _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));

    public static DirectBrowseProviderRegistry CreateDefault()
        => new(new IDirectBrowseProvider[] { new Iso9660DirectBrowseProvider() });

    public async ValueTask<IDirectBrowseProvider?> ResolveAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        var extension = Path.GetExtension(imagePath);
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (provider.Extensions.Count > 0
                && !provider.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await provider.CanHandleAsync(imagePath, cancellationToken))
                return provider;
        }

        return null;
    }
}
