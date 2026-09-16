using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

public sealed record ImageReport(DiskImageInfo Image, string? Provider, string[] Capabilities,
    ImageIntelligenceInfo? Analysis, IReadOnlyList<string> Diagnostics);

public sealed class ImageReportService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly ProviderRegistry _registry;
    public ImageReportService(ProviderRegistry? registry = null) => _registry = registry ?? DefaultProviderRegistry.Create();

    public async Task<ImageReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        var image = await new ImageDetectionService().InspectAsync(path, cancellationToken);
        var resolution = await _registry.ResolveAsync(path, cancellationToken);
        var diagnostics = resolution.Diagnostics.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage))
            .Select(x => $"{x.ProviderId}: {x.ErrorMessage}").ToList();
        ImageIntelligenceInfo? analysis = null;
        if (resolution.Provider is not null)
        {
            try { analysis = await new ImageIntelligenceService(_registry).AnalyzeAsync(path, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { diagnostics.Add($"Analysis incomplete: {ex.Message}"); }
        }
        else diagnostics.Add("No metadata provider accepted this image. Native Windows mounting may still be available.");
        var flags = resolution.Descriptor?.Capabilities ?? ProviderCapabilities.None;
        return new(image, resolution.Descriptor?.DisplayName,
            Enum.GetValues<ProviderCapabilities>().Where(x => x != ProviderCapabilities.None && flags.HasFlag(x)).Select(x => x.ToString()).ToArray(),
            analysis, diagnostics);
    }

    public static string ToJson(ImageReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static string ToText(ImageReport report)
    {
        var b = new StringBuilder();
        b.AppendLine(report.Image.FileName).AppendLine($"{report.Image.Format} | {report.Image.SizeDisplay}");
        b.AppendLine($"Provider: {report.Provider ?? "not available"}");
        b.AppendLine($"Available operations: {string.Join(", ", report.Capabilities)}");
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
                foreach (var f in fs.Detections)
                    b.AppendLine($"{f.DisplayName} | {f.Label} | {f.Identifier} | {f.Evidence}");
            else b.AppendLine("No filesystem identified within the supported read scope.");
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
                foreach (var item in a.Identity) b.AppendLine($"{item.Kind}: {item.Value} ({item.Source})");
            }
            if (a.ArchitectureHints.Count > 0) b.AppendLine($"Architecture hints: {string.Join(", ", a.ArchitectureHints)}");
            b.AppendLine().AppendLine("HEALTH FINDINGS");
            foreach (var finding in a.HealthFindings) b.AppendLine($"{finding.Severity}: {finding.Message} [{finding.Code}]");
            b.AppendLine(a.HasHealthFindings
                ? "Findings are limited to inspected metadata; this is not a complete integrity check."
                : "No findings in inspected metadata. This does not prove that the image is healthy.");
        }
        foreach (var diagnostic in report.Diagnostics) b.AppendLine().AppendLine(diagnostic);
        return b.ToString();
    }
}
