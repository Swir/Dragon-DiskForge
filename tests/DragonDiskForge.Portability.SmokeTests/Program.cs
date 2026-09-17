using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DragonDiskForge.Cli;
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
        var names = archive.Entries.Select(x => x.FullName).ToHashSet(StringComparer.Ordinal);
        Require(names.Count == 3
            && names.Contains("README.txt")
            && names.Contains("diagnostics.json")
            && names.Contains("state-summary.json"),
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

    var cliState = Path.Combine(root, "cli", "app-state.json");
    var cliExport = Path.Combine(root, "cli", "exported-state.json");
    var cliDiagnostics = Path.Combine(root, "cli", "support.zip");
    Directory.CreateDirectory(Path.GetDirectoryName(cliState)!);

    var cliDisable = await RunCliAsync("restore-last-image", "off", "--state", cliState);
    Require(cliDisable.ExitCode == 0 && string.IsNullOrWhiteSpace(cliDisable.Stderr),
        "CLI can safely disable last-image restoration in isolated app state.");

    var cliShow = await RunCliAsync("state-show", "--state", cliState, "--format", "json");
    Require(cliShow.ExitCode == 0 && string.IsNullOrWhiteSpace(cliShow.Stderr),
        "CLI state-show emits clean JSON without diagnostics on stdout.");
    using (var document = JsonDocument.Parse(cliShow.Stdout))
    {
        Require(!document.RootElement.GetProperty("Settings").GetProperty("RestoreLastImage").GetBoolean(),
            "CLI JSON state reflects the persisted restore setting.");
    }

    var cliExportResult = await RunCliAsync("state-export", cliExport, "--state", cliState);
    Require(cliExportResult.ExitCode == 0 && File.Exists(cliExport),
        "CLI exports portable settings/session state to a real file.");

    var cliEnable = await RunCliAsync("restore-last-image", "on", "--state", cliState);
    Require(cliEnable.ExitCode == 0, "CLI can re-enable last-image restoration.");

    var cliImport = await RunCliAsync("state-import", cliExport, "--state", cliState);
    Require(cliImport.ExitCode == 0 && string.IsNullOrWhiteSpace(cliImport.Stderr),
        "CLI validates and imports portable state atomically.");
    var restoredCliState = await new ApplicationPortabilityService(cliState).LoadAsync();
    Require(!restoredCliState.Settings.RestoreLastImage,
        "CLI import restores the exported setting rather than keeping later local mutations.");

    var cliDiagnosticResult = await RunCliAsync("diagnostics", cliDiagnostics, "--state", cliState);
    Require(cliDiagnosticResult.ExitCode == 0 && File.Exists(cliDiagnostics),
        "CLI diagnostic command creates the sanitized support ZIP.");
    using (var archive = ZipFile.OpenRead(cliDiagnostics))
    {
        Require(archive.Entries.All(x => !string.IsNullOrWhiteSpace(x.FullName)),
            "CLI diagnostic bundle contains only named entries.");
    }

    var cliBadSetting = await RunCliAsync("restore-last-image", "maybe", "--state", cliState);
    Require(cliBadSetting.ExitCode == 2 && cliBadSetting.Stderr.Contains("must be 'on' or 'off'", StringComparison.Ordinal),
        "CLI rejects invalid restore setting values as a usage error.");

    var cliBadImport = await RunCliAsync("state-import", invalid, "--state", cliState);
    Require(cliBadImport.ExitCode == 3,
        "CLI reports unsupported portable-state schemas as an input/operation error.");
    Require(!(await new ApplicationPortabilityService(cliState).LoadAsync()).Settings.RestoreLastImage,
        "Failed CLI import does not corrupt the previously persisted application state.");

    Console.WriteLine("Dragon DiskForge portability smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(params string[] args)
{
    using var stdout = new StringWriter();
    using var stderr = new StringWriter();
    var exitCode = await CliRunner.RunAsync(args, stdout, stderr);
    return (exitCode, stdout.ToString(), stderr.ToString());
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
    Console.WriteLine("PASS  " + message);
}
