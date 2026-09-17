using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

namespace DragonDiskForge.Core.Services;

public sealed class ApplicationPortabilityService
{
    private const long MaxStateBytes = 1024 * 1024;
    private readonly string _storagePath;
    private readonly SafeOutputService _safeOutput = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ApplicationPortabilityService(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException("Application-state storage path cannot be empty.", nameof(storagePath));
        _storagePath = Path.GetFullPath(storagePath);
    }

    public async Task<DragonPortableState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnsafeAsync(_storagePath, allowMissing: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DragonPortableState> RecordLastImageAsync(
        string? imagePath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadUnsafeAsync(_storagePath, allowMissing: true, cancellationToken);
            var normalizedPath = NormalizeOptionalPath(imagePath);
            var updated = current with
            {
                Session = new DragonSessionState(normalizedPath, DateTimeOffset.UtcNow)
            };
            await SaveUnsafeAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DragonPortableState> SetSettingsAsync(
        DragonApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadUnsafeAsync(_storagePath, allowMissing: true, cancellationToken);
            var updated = current with { Settings = settings };
            await SaveUnsafeAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ExportAsync(
        string destinationPath,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        await WriteJsonAsync(destinationPath, state, overwritePolicy, cancellationToken);
    }

    public async Task<DragonPortableState> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var imported = await LoadUnsafeAsync(fullSourcePath, allowMissing: false, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveUnsafeAsync(imported, cancellationToken);
            return imported;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CreateDiagnosticBundleAsync(
        string destinationPath,
        OutputOverwritePolicy overwritePolicy = OutputOverwritePolicy.FailIfExists,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        var providerIds = ProviderRegistryFactory.CreateDefault().Providers
            .Select(x => x.Id)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var informationalVersion = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? typeof(ApplicationPortabilityService).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        var extension = state.Session.LastImagePath is null
            ? null
            : Path.GetExtension(state.Session.LastImagePath);
        var stateDirectory = Path.GetDirectoryName(_storagePath)
            ?? throw new InvalidOperationException("Application-state storage path has no parent directory.");
        var crashReports = new CrashReportService(Path.Combine(stateDirectory, "crashes"))
            .LoadRecent(maxReports: 3);

        var diagnostics = new DragonDiagnosticSnapshot(
            SchemaVersion: 1,
            CreatedUtc: DateTimeOffset.UtcNow,
            OperatingSystem: RuntimeInformation.OSDescription,
            Runtime: RuntimeInformation.FrameworkDescription,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ProductVersion: informationalVersion,
            RestoreLastImage: state.Settings.RestoreLastImage,
            HasSavedSession: state.Session.LastImagePath is not null,
            SavedImageExtension: string.IsNullOrWhiteSpace(extension) ? null : extension,
            ProviderCount: providerIds.Length,
            ProviderIds: providerIds);

        var fullDestination = Path.GetFullPath(destinationPath);
        await _safeOutput.WriteAsync(
            fullDestination,
            async (stream, token) =>
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                await WriteZipEntryAsync(archive, "diagnostics.json", diagnostics, token);
                await WriteZipEntryAsync(
                    archive,
                    "state-summary.json",
                    new
                    {
                        schemaVersion = DragonPortableState.CurrentSchemaVersion,
                        restoreLastImage = state.Settings.RestoreLastImage,
                        hasSavedSession = state.Session.LastImagePath is not null,
                        savedImageExtension = diagnostics.SavedImageExtension,
                        lastSavedUtc = state.Session.LastSavedUtc
                    },
                    token);
                await WriteZipEntryAsync(archive, "crash-summary.json", crashReports, token);

                var readme = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
                await using var writerStream = readme.Open();
                await using var writer = new StreamWriter(
                    writerStream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 1024,
                    leaveOpen: false);
                await writer.WriteAsync(
                    "Dragon DiskForge diagnostic bundle\n" +
                    "This bundle intentionally excludes full image paths and image contents.\n" +
                    "It contains runtime/provider metadata, a sanitized session/settings summary, and up to three privacy-preserving crash fingerprints.\n" +
                    "Crash summaries never store raw exception messages, source-file paths or image data.\n");
                await writer.FlushAsync(token);
            },
            overwritePolicy,
            cancellationToken);
    }

    private async Task SaveUnsafeAsync(DragonPortableState state, CancellationToken cancellationToken)
    {
        var normalized = ValidateAndNormalize(state);
        var directory = Path.GetDirectoryName(_storagePath)
            ?? throw new InvalidOperationException("Application-state storage path has no parent directory.");
        Directory.CreateDirectory(directory);

        await WriteJsonAsync(
            _storagePath,
            normalized,
            OutputOverwritePolicy.ReplaceExisting,
            cancellationToken);
    }

    private async Task WriteJsonAsync<T>(
        string destinationPath,
        T value,
        OutputOverwritePolicy overwritePolicy,
        CancellationToken cancellationToken)
    {
        await _safeOutput.WriteAsync(
            destinationPath,
            (stream, token) => JsonSerializer.SerializeAsync(stream, value, _jsonOptions, token),
            overwritePolicy,
            cancellationToken);
    }

    private async Task<DragonPortableState> LoadUnsafeAsync(
        string path,
        bool allowMissing,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            if (allowMissing)
                return DragonPortableState.Default;
            throw new FileNotFoundException("Portable state file was not found.", path);
        }

        var length = new FileInfo(path).Length;
        if (length <= 0 || length > MaxStateBytes)
            throw new InvalidDataException($"Portable state file must be between 1 and {MaxStateBytes} bytes.");

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var state = await JsonSerializer.DeserializeAsync<DragonPortableState>(stream, _jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Portable state JSON is empty or invalid.");
        return ValidateAndNormalize(state);
    }

    private static DragonPortableState ValidateAndNormalize(DragonPortableState state)
    {
        if (state.SchemaVersion != DragonPortableState.CurrentSchemaVersion)
            throw new NotSupportedException($"Unsupported portable-state schema version: {state.SchemaVersion}.");
        if (state.Settings is null || state.Session is null)
            throw new InvalidDataException("Portable state is missing required settings or session data.");

        var normalizedPath = NormalizeOptionalPath(state.Session.LastImagePath);
        return state with
        {
            Session = state.Session with { LastImagePath = normalizedPath }
        };
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (path.IndexOf('\0') >= 0 || path.Length > 32767)
            throw new InvalidDataException("Saved image path is invalid or unreasonably long.");
        return Path.GetFullPath(path);
    }

    private async Task WriteZipEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, value, _jsonOptions, cancellationToken);
    }
}
