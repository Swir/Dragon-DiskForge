using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

public sealed record ImageReport(
    DiskImageInfo Image,
    string? Provider,
    string[] Capabilities,
    ImageIntelligenceInfo? Analysis,
    GuestImageIntelligenceInfo? GuestAnalysis,
    IReadOnlyList<string> Diagnostics);

public sealed class ImageReportService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ProviderRegistry _registry;

    public ImageReportService(ProviderRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public async Task<ImageReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var image = await new ImageDetectionService().InspectAsync(path, cancellationToken);
        var resolution = await _registry.ResolveAsync(path, cancellationToken);
        var diagnostics = resolution.Diagnostics.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage))
            .Select(x => $"{x.ProviderId}: {x.ErrorMessage}").ToList();

        ImageIntelligenceInfo? analysis = null;
        if (resolution.Provider is not null)
        {
            try
            {
                analysis = await new ImageIntelligenceService(_registry).AnalyzeAsync(path, cancellationToken);
                if (analysis.FileSystems is { } fileSystems)
                {
                    var depth = await new FileSystemDepthService().AnalyzeAsync(path, fileSystems, cancellationToken);
                    analysis = MergeDepthEvidence(analysis, depth);

                    if (fileSystems.Detections.Any(x => x.Kind == FileSystemKind.Udf))
                    {
                        var udfTraversal = await new UdfTraversalService().AnalyzeAsync(path, fileSystems, cancellationToken);
                        analysis = MergeDepthEvidence(
                            analysis,
                            new FileSystemDepthInfo(udfTraversal.Identity, udfTraversal.HealthFindings));
                    }

                    if (fileSystems.Detections.Any(x => x.Kind == FileSystemKind.Ntfs))
                    {
                        var ntfsDepth = await new NtfsMetadataDepthService().AnalyzeAsync(path, fileSystems, cancellationToken);
                        analysis = MergeDepthEvidence(analysis, ntfsDepth);
                    }
                }

                var architecture = new ArchitectureReconciliationService().Analyze(analysis.BootInstaller);
                analysis = MergeArchitectureEvidence(analysis, architecture);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { diagnostics.Add($"Analysis incomplete: {ex.Message}"); }
        }
        else
        {
            diagnostics.Add("No metadata provider accepted this image. Native Windows mounting may still be available.");
        }

        GuestImageIntelligenceInfo? guestAnalysis = null;
        if (resolution.Provider is QcowImageProvider or VmdkSparseImageProvider)
        {
            try
            {
                guestAnalysis = await new GuestImageIntelligenceService(_registry)
                    .AnalyzeAsync(path, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (NotSupportedException ex)
            {
                diagnostics.Add($"Guest-byte analysis unavailable: {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                diagnostics.Add($"Guest-byte analysis refused unsafe metadata: {ex.Message}");
            }
            catch (EndOfStreamException ex)
            {
                diagnostics.Add($"Guest-byte analysis refused truncated data: {ex.Message}");
            }
        }

        var flags = resolution.Descriptor?.Capabilities ?? ProviderCapabilities.None;
        return new ImageReport(
            image,
            resolution.Descriptor?.DisplayName,
            Enum.GetValues<ProviderCapabilities>()
                .Where(x => x != ProviderCapabilities.None && flags.HasFlag(x))
                .Select(x => x.ToString())
                .ToArray(),
            analysis,
            guestAnalysis,
            diagnostics);
    }

    public static string ToJson(ImageReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string ToText(ImageReport report)
    {
        var b = new StringBuilder();
        b.AppendLine(report.Image.FileName).AppendLine($"{report.Image.Format} | {report.Image.SizeDisplay}");
        b.AppendLine($"Provider: {report.Provider ?? "not available"}");
        b.AppendLine($"Available capabilities: {string.Join(", ", report.Capabilities)}");

        if (report.Analysis is { } a)
        {
            if (a.PartitionLayout is { } p)
            {
                b.AppendLine().AppendLine($"PARTITIONS — {p.SchemeDisplay}, {p.SectorSize}-byte sectors");
                foreach (var part in p.Partitions)
                    b.AppendLine($"#{part.Index} {part.Name} | {part.TypeName} | {part.SizeBytes:N0} bytes | offset {part.OffsetBytes:N0}");
            }

            b.AppendLine().AppendLine("FILE SYSTEMS");
            if (a.FileSystems is { HasDetections: true } fs)
            {
                foreach (var f in fs.Detections)
                    b.AppendLine($"{f.DisplayName} | {f.Label} | {f.Identifier} | {f.Evidence}");
            }
            else
            {
                b.AppendLine("No filesystem identified within the supported read scope.");
            }

            if (a.BootInstaller is { } boot)
            {
                b.AppendLine().AppendLine("BOOT AND INSTALLER");
                b.AppendLine($"El Torito: {boot.HasElToritoCatalog}; BIOS: {boot.SupportsBiosBoot}; UEFI: {boot.SupportsUefiBoot}");
                foreach (var installer in boot.Installers)
                    b.AppendLine($"{installer.Family} | {installer.Variant} | {installer.ArchitectureHint}");
            }

            if (a.Identity.Count > 0)
            {
                b.AppendLine().AppendLine("IDENTITY");
                foreach (var item in a.Identity)
                    b.AppendLine($"{item.Kind}: {item.Value} ({item.Source})");
            }

            if (a.ArchitectureHints.Count > 0)
                b.AppendLine($"Architecture hints: {string.Join(", ", a.ArchitectureHints)}");

            b.AppendLine().AppendLine("HEALTH FINDINGS");
            foreach (var finding in a.HealthFindings)
                b.AppendLine($"{finding.Severity}: {finding.Message} [{finding.Code}]");
            b.AppendLine(a.HasHealthFindings
                ? "Findings are limited to inspected metadata; this is not a complete integrity check."
                : "No findings in inspected metadata. This does not prove that the image is healthy.");
        }

        if (report.GuestAnalysis is { } guest)
        {
            b.AppendLine().AppendLine($"GUEST ADDRESS SPACE — {guest.ReaderKind}, {guest.GuestSizeBytes:N0} bytes");
            if (guest.PartitionLayout is { } guestPartitions)
            {
                b.AppendLine($"GUEST PARTITIONS — {guestPartitions.SchemeDisplay}, {guestPartitions.SectorSize}-byte sectors");
                foreach (var part in guestPartitions.Partitions)
                    b.AppendLine($"#{part.Index} {part.Name} | {part.TypeName} | {part.SizeBytes:N0} bytes | guest offset {part.OffsetBytes:N0}");
            }
            else
            {
                b.AppendLine("No supported guest partition table identified; filesystem recognition used the whole guest address space.");
            }

            b.AppendLine("GUEST FILE SYSTEMS");
            if (guest.FileSystems.HasDetections)
            {
                foreach (var f in guest.FileSystems.Detections)
                    b.AppendLine($"{f.DisplayName} | {f.Label} | {f.Identifier} | guest offset {f.GuestOffsetBytes:N0} | {f.Evidence}");
            }
            else
            {
                b.AppendLine("No filesystem identified within the supported guest read scope.");
            }
        }

        foreach (var diagnostic in report.Diagnostics)
            b.AppendLine().AppendLine(diagnostic);
        return b.ToString();
    }

    private static ImageIntelligenceInfo MergeDepthEvidence(
        ImageIntelligenceInfo analysis,
        FileSystemDepthInfo depth)
    {
        if (!depth.HasIdentity && !depth.HasHealthFindings)
            return analysis;

        var identity = analysis.Identity
            .Concat(depth.Identity)
            .GroupBy(
                x => $"{x.Kind}\u001f{x.Value}\u001f{x.PartitionIndex?.ToString() ?? string.Empty}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PartitionIndex ?? int.MinValue)
            .ThenBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var health = NormalizeHealth(analysis.HealthFindings.Concat(depth.HealthFindings));
        return analysis with
        {
            Identity = Array.AsReadOnly(identity),
            HealthFindings = Array.AsReadOnly(health)
        };
    }

    private static ImageIntelligenceInfo MergeArchitectureEvidence(
        ImageIntelligenceInfo analysis,
        ArchitectureReconciliationResult reconciliation)
    {
        var health = NormalizeHealth(analysis.HealthFindings.Concat(reconciliation.HealthFindings));
        return analysis with
        {
            ArchitectureHints = Array.AsReadOnly(reconciliation.ArchitectureHints.ToArray()),
            HealthFindings = Array.AsReadOnly(health)
        };
    }

    private static ImageHealthFinding[] NormalizeHealth(IEnumerable<ImageHealthFinding> findings)
        => findings
            .GroupBy(
                x => $"{x.Code}\u001f{x.PartitionIndex?.ToString() ?? string.Empty}\u001f{x.Message}",
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.PartitionIndex ?? int.MinValue)
            .ToArray();
}
