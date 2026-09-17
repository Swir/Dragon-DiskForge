using System.IO.Compression;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "dragon-diskforge-portability-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var localState = Path.Combine(root, "local", "app-state.json");
var importedState = Path.Combine(root, "imported", "app-state.json");
var exportedState = Path.Combine(root, "portable", "dragon-state.json");
var diagnosticZip = Path.Combine(root, "support", "dragon-support.zip");
Directory.CreateDirectory(Path.GetDirectoryName(exportedState)!);
Directory.CreateDirectory(Path.GetDirectoryName(diagnosticZip)!);

try
{
    var service = new ApplicationPortabilityService(localState);
    var defaults = await service.LoadAsync();
    Require(defaults.SchemaVersion == DragonPortableState.CurrentSchemaVersion, "Missing local state returns current schema defaults.");
    Require(defaults.Settings.RestoreLastImage, "Restore-last-image defaults to enabled.");
    Require(defaults.Session.LastImagePath is null, "Default session contains no fabricated image path.");

    var imagePath = Path.Combine(root, "images", "sample.iso");
    Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
    await File.WriteAllBytesAsync(imagePath, Encoding.ASCII.GetBytes("fixture"));

    var recorded = await service.RecordLastImageAsync(imagePath);
    Require(recorded.Session.LastImagePath == Path.GetFullPath(imagePath), "Session persistence normalizes the last image path.");
    Require(recorded.Session.LastSavedUtc is not null, "Session persistence records a save timestamp.");

    var changed = await service.SetSettingsAsync(new DragonApplicationSettings(RestoreLastImage: false));
    Require(!changed.Settings.RestoreLastImage, "Settings changes persist independently from session state.");
    Require(changed.Session.LastImagePath == Path.GetFullPath(imagePath), "Settings changes preserve the saved session.");

    await service.ExportAsync(exportedState);
    Require(File.Exists(exportedState), "Portable state export creates a real file.");

    var importedService = new ApplicationPortabilityService(importedState);
    var imported = await importedService.ImportAsync(exportedState);
    Require(!imported.Settings.RestoreLastImage, "Portable import preserves settings.");
    Require(imported.Session.LastImagePath == Path.GetFullPath(imagePath), "Portable import preserves session state.");
    Require((await importedService.LoadAsync()) == imported, "Imported state survives a process-style reload.");

    var invalid = Path.Combine(root, "portable", "invalid.json");
    await File.WriteAllTextAsync(invalid, "{\"SchemaVersion\":999,\"Settings\":{\"RestoreLastImage\":true},\"Session\":{\"LastImagePath\":null,\"LastSavedUtc\":null}}");
    try
    {
        await importedService.ImportAsync(invalid);
        Require(false, "Unsupported schema import must fail closed.");
    }
    catch (NotSupportedException)
    {
        Require(true, "Unsupported schema import fails closed.");
    }
    Require((await importedService.LoadAsync()) == imported, "Rejected import leaves the existing local state unchanged.");

    await service.CreateDiagnosticBundleAsync(diagnosticZip);
    Require(File.Exists(diagnosticZip), "Diagnostic export creates a support bundle.");
    using (var archive = ZipFile.OpenRead(diagnosticZip))
    {
        var names = archive.Entries.Select(x => x.FullName).OrderBy(x => x).ToArray();
        Require(names.SequenceEqual(new[] { "README.txt", "diagnostics.json", "state-summary.json" }),
            "Diagnostic bundle contains only the documented sanitized entries.");

        foreach (var entry in archive.Entries.Where(x => x.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(entry.Open());
            var text = await reader.ReadToEndAsync();
            Require(!text.Contains(Path.GetFullPath(imagePath), StringComparison.OrdinalIgnoreCase),
                $"{entry.FullName} must not disclose the full saved image path.");
        }
    }

    var cancelledExport = Path.Combine(root, "portable", "cancelled.json");
    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        try
        {
            await service.ExportAsync(cancelledExport, cancellationToken: cts.Token);
            Require(false, "Pre-cancelled export must be cancelled.");
        }
        catch (OperationCanceledException)
        {
            Require(true, "Pre-cancelled export is cancelled before publication.");
        }
    }
    Require(!File.Exists(cancelledExport), "Cancelled export does not publish a partial destination.");

    Console.WriteLine("Dragon DiskForge portability smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
    Console.WriteLine("PASS  " + message);
}
