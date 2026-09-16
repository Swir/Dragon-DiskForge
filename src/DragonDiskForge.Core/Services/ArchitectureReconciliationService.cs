using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public sealed record ArchitectureReconciliationResult(
    IReadOnlyList<string> ArchitectureHints,
    IReadOnlyList<ImageHealthFinding> HealthFindings);

/// <summary>
/// Reconciles independently bounded boot-path and installer-path architecture evidence.
/// Conflicting evidence is preserved rather than silently collapsed to one guessed architecture.
/// </summary>
public sealed class ArchitectureReconciliationService
{
    public ArchitectureReconciliationResult Analyze(BootInstallerIntelligenceInfo? bootInstaller)
    {
        if (bootInstaller is null)
        {
            return new ArchitectureReconciliationResult(
                Array.Empty<string>(),
                Array.Empty<ImageHealthFinding>());
        }

        var bootHints = Normalize(bootInstaller.ArchitectureHints);
        var installerHints = Normalize(bootInstaller.Installers
            .Select(x => x.ArchitectureHint));
        var combined = bootHints
            .Concat(installerHints)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var findings = new List<ImageHealthFinding>();
        if (bootHints.Length == 1
            && installerHints.Length == 1
            && !bootHints[0].Equals(installerHints[0], StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ImageHealthFinding(
                "ARCHITECTURE_EVIDENCE_CONFLICT",
                ImageHealthSeverity.Warning,
                $"Independent architecture evidence disagrees: boot-path evidence indicates '{bootHints[0]}' while installer-path evidence indicates '{installerHints[0]}'. Both hints are preserved.",
                "boot/installer architecture reconciliation"));
        }

        return new ArchitectureReconciliationResult(
            Array.AsReadOnly(combined),
            Array.AsReadOnly(findings.ToArray()));
    }

    private static string[] Normalize(IEnumerable<string> values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
