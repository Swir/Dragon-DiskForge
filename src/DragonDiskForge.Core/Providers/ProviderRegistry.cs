using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class ProviderRegistry
{
    private readonly IReadOnlyList<ProviderRegistration> _registrations;

    public ProviderRegistry(IEnumerable<IDiskImageProvider> providers)
        : this(providers.Select(provider => new ProviderRegistration(provider)))
    {
    }

    public ProviderRegistry(IEnumerable<ProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var materialized = registrations
            .Select(registration => registration ?? throw new ArgumentException("Provider registration cannot be null.", nameof(registrations)))
            .ToArray();

        var duplicate = materialized
            .GroupBy(x => x.Provider.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Provider id '{duplicate.Key}' is registered more than once.", nameof(registrations));

        if (materialized.Any(x => string.IsNullOrWhiteSpace(x.Provider.Id)))
            throw new ArgumentException("Every provider must have a non-empty id.", nameof(registrations));

        _registrations = materialized
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Provider.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ProviderDescriptor> Providers
        => _registrations.Select(x => x.Descriptor).ToArray();

    public async Task<ProviderResolution> ResolveAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var extension = NormalizeExtension(Path.GetExtension(fullPath));
        var diagnostics = new List<ProviderProbeDiagnostic>(_registrations.Count);

        var ordered = _registrations
            .Select((registration, index) => new Candidate(
                registration,
                index,
                ExtensionMatches(registration.Provider, extension)))
            .OrderByDescending(x => x.ExtensionMatched)
            .ThenBy(x => x.Index)
            .ToArray();

        foreach (var candidate in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = candidate.Registration.Descriptor;

            try
            {
                var supported = await candidate.Registration.Provider
                    .CanHandleAsync(fullPath, cancellationToken);

                diagnostics.Add(new ProviderProbeDiagnostic(
                    descriptor.Id,
                    descriptor.DisplayName,
                    candidate.ExtensionMatched,
                    supported,
                    ErrorMessage: null));

                if (supported)
                {
                    return new ProviderResolution(
                        candidate.Registration.Provider,
                        descriptor,
                        diagnostics.ToArray());
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ProviderProbeDiagnostic(
                    descriptor.Id,
                    descriptor.DisplayName,
                    candidate.ExtensionMatched,
                    Supported: false,
                    ErrorMessage: ex.Message));
            }
        }

        return new ProviderResolution(null, null, diagnostics.ToArray());
    }

    public async Task<DiskImageInfo> InspectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(path, cancellationToken);
        if (resolution.Provider is null || resolution.Descriptor is null)
        {
            var errors = resolution.Diagnostics
                .Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage))
                .Select(x => $"{x.ProviderId}: {x.ErrorMessage}")
                .ToArray();
            var detail = errors.Length == 0 ? string.Empty : $" Provider errors: {string.Join(" | ", errors)}";
            throw new NotSupportedException($"No registered disk-image provider accepted '{Path.GetFileName(path)}'.{detail}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await resolution.Provider.InspectAsync(Path.GetFullPath(path), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Provider '{resolution.Descriptor.DisplayName}' could not inspect '{Path.GetFileName(path)}'.",
                ex);
        }
    }

    private static bool ExtensionMatches(IDiskImageProvider provider, string extension)
        => extension.Length > 0 && provider.Extensions.Any(value =>
            string.Equals(NormalizeExtension(value), extension, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        var value = extension.Trim();
        return value.StartsWith('.') ? value : "." + value;
    }

    private sealed record Candidate(
        ProviderRegistration Registration,
        int Index,
        bool ExtensionMatched);
}
