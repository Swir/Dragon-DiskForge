using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Providers;

public sealed class ProviderRegistry
{
    private readonly IReadOnlyList<ProviderRegistration> _registrations;

    public ProviderRegistry(IEnumerable<IDiskImageProvider> providers)
        : this(CreateRegistrations(providers))
    {
    }

    public ProviderRegistry(IEnumerable<ProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var materialized = registrations
            .Select(registration => registration ?? throw new ArgumentException("Provider registration cannot be null.", nameof(registrations)))
            .ToArray();

        var duplicate = materialized
            .GroupBy(x => x.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Provider id '{duplicate.Key}' is registered more than once.", nameof(registrations));

        _registrations = materialized
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
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
        var diagnostics = new List<ProviderProbeDiagnostic>(_registrations.Count);

        foreach (var candidate in OrderCandidates(fullPath))
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
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var errors = new List<string>();

        foreach (var candidate in OrderCandidates(fullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = candidate.Registration.Provider;
            var descriptor = candidate.Registration.Descriptor;

            bool supported;
            try
            {
                supported = await provider.CanHandleAsync(fullPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{descriptor.Id} probe: {ex.Message}");
                continue;
            }

            if (!supported)
                continue;

            try
            {
                return await provider.InspectAsync(fullPath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{descriptor.Id} inspect: {ex.Message}");
            }
        }

        var detail = errors.Count == 0 ? string.Empty : $" Provider errors: {string.Join(" | ", errors)}";
        throw new NotSupportedException(
            $"No registered disk-image provider could inspect '{Path.GetFileName(path)}'.{detail}");
    }

    private IEnumerable<Candidate> OrderCandidates(string fullPath)
    {
        var extension = NormalizeExtension(Path.GetExtension(fullPath));
        return _registrations
            .Select((registration, index) => new Candidate(
                registration,
                index,
                ExtensionMatches(registration.Descriptor, extension)))
            .OrderByDescending(x => x.ExtensionMatched)
            .ThenBy(x => x.Index);
    }

    private static bool ExtensionMatches(ProviderDescriptor descriptor, string extension)
        => extension.Length > 0 && descriptor.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        var value = extension.Trim();
        return value.StartsWith('.') ? value : "." + value;
    }

    private static ProviderRegistration[] CreateRegistrations(IEnumerable<IDiskImageProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return providers.Select(provider => new ProviderRegistration(provider)).ToArray();
    }

    private sealed record Candidate(
        ProviderRegistration Registration,
        int Index,
        bool ExtensionMatched);
}
