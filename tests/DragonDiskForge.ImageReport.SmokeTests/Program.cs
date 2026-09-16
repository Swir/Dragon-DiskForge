using System.Text.Json;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "dragon-diskforge-report-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var unknown = Path.Combine(root, "unknown.payload");
    await File.WriteAllBytesAsync(unknown, new byte[64]);

    var service = new ImageReportService();
    var report = await service.AnalyzeAsync(unknown);
    Require(report.Image.FileName == "unknown.payload", "Report must retain the inspected file name.");
    Require(report.Image.Format == "Unknown image", "Unknown input must not gain a fabricated format.");
    Require(report.Provider is null, "Unknown input must not gain a fabricated provider.");
    Require(report.Analysis is null, "Unknown input must not fabricate image intelligence.");
    Require(report.Diagnostics.Any(x => x.Contains("No metadata provider", StringComparison.Ordinal)),
        "Report must explain when no metadata provider accepted the image.");

    var json = ImageReportService.ToJson(report);
    using var document = JsonDocument.Parse(json);
    Require(document.RootElement.GetProperty("Image").GetProperty("FileName").GetString() == "unknown.payload",
        "JSON report must expose the inspected image metadata.");
    Require(document.RootElement.GetProperty("Provider").ValueKind == JsonValueKind.Null,
        "JSON report must preserve a missing provider as null.");

    var text = ImageReportService.ToText(report);
    Require(text.Contains("Provider: not available", StringComparison.Ordinal),
        "Text report must make missing provider support explicit.");
    Require(text.Contains("No metadata provider accepted this image", StringComparison.Ordinal),
        "Text report must carry diagnostics without inventing capabilities.");

    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var cancelled = false;
    try
    {
        await service.AnalyzeAsync(unknown, cts.Token);
    }
    catch (OperationCanceledException)
    {
        cancelled = true;
    }
    Require(cancelled, "Pre-cancelled analysis must stop instead of returning a partial report.");

    Console.WriteLine("Image report smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
