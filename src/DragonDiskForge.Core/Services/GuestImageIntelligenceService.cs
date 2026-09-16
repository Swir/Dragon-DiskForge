using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Connects proven sparse-container guest-byte readers to the existing bounded partition/filesystem intelligence model.
/// Only explicitly implemented container readers are eligible; unsupported optional container semantics fail closed.
/// </summary>
public sealed class GuestImageIntelligenceService
{
    private readonly ProviderRegistry _registry;

    public GuestImageIntelligenceService(ProviderRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public async Task<GuestImageIntelligenceInfo> AnalyzeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Disk image was not found.", fullPath);

        var resolution = await _registry.ResolveAsync(fullPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolution.Provider is null || resolution.Descriptor is null)
            throw new NotSupportedException("Guest image intelligence requires a registered provider that recognizes the image.");

        var descriptor = resolution.Descriptor;
        IGuestByteReader reader;
        string readerKind;

        switch (resolution.Provider)
        {
            case QcowImageProvider:
                reader = await Qcow2GuestByteReader.OpenAsync(fullPath, cancellationToken);
                readerKind = "QCOW2 standard uncompressed guest bytes";
                break;
            case VmdkSparseImageProvider:
                reader = await VmdkSparseGuestByteReader.OpenAsync(fullPath, cancellationToken);
                readerKind = "VMDK hosted sparse standard guest bytes";
                break;
            default:
                throw new NotSupportedException(
                    $"Provider '{descriptor.DisplayName}' does not have a proven guest-byte reader integration.");
        }

        await using (reader)
        {
            if (reader.Length > long.MaxValue)
                throw new NotSupportedException("Guest image intelligence currently supports address spaces up to Int64.MaxValue bytes.");

            var guestSize = (long)reader.Length;
            var table = await GuestPartitionTableReader.TryReadAsync(reader, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            PartitionIntelligenceInfo? layout = null;
            if (table is not null)
            {
                layout = PartitionIntelligenceService.AnalyzeLayout(
                    table,
                    guestSize,
                    descriptor.Id,
                    descriptor.DisplayName + " guest");

                if (layout.HasErrors)
                {
                    var codes = string.Join(", ", layout.Findings
                        .Where(x => x.Severity == PartitionFindingSeverity.Error)
                        .Select(x => x.Code)
                        .Distinct(StringComparer.Ordinal)
                        .Take(8));
                    throw new InvalidDataException(
                        $"Guest intelligence refuses a structurally invalid partition layout. Findings: {codes}.");
                }
            }

            var fileSystems = await new GuestFileSystemRecognitionService().AnalyzeAsync(
                reader,
                descriptor.Id,
                descriptor.DisplayName,
                table,
                cancellationToken);

            return new GuestImageIntelligenceInfo(
                readerKind,
                guestSize,
                layout,
                fileSystems);
        }
    }
}
