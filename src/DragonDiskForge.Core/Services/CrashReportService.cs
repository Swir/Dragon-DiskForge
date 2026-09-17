using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DragonDiskForge.Core.Services;

public sealed record DragonCrashReport(
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    string ExceptionType,
    int HResult,
    string FingerprintSha256,
    IReadOnlyList<string> ExceptionChainTypes,
    IReadOnlyList<string> Frames);

/// <summary>
/// Stores a small, privacy-preserving crash history for support diagnostics.
/// Raw exception messages, file paths, image contents and source-file locations are never persisted.
/// </summary>
public sealed class CrashReportService
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxReports = 10;
    public const int MaxFrames = 24;
    public const long MaxReportBytes = 64 * 1024;

    private readonly string _storageDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public CrashReportService(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("Crash-report storage directory cannot be empty.", nameof(storageDirectory));
        _storageDirectory = Path.GetFullPath(storageDirectory);
    }

    public bool TryRecord(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string? temporaryPath = null;

        try
        {
            Directory.CreateDirectory(_storageDirectory);
            var report = CreateReport(exception);
            var json = JsonSerializer.Serialize(report, _jsonOptions);
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
            if (bytes.LongLength > MaxReportBytes)
                return false;

            var stamp = report.CreatedUtc.UtcDateTime.ToString("yyyyMMdd-HHmmssfff");
            var stem = $"dragon-crash-{stamp}-{Guid.NewGuid():N}";
            temporaryPath = Path.Combine(_storageDirectory, stem + ".tmp");
            var destinationPath = Path.Combine(_storageDirectory, stem + ".json");

            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath);
            temporaryPath = null;
            TrimOldReports();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    public IReadOnlyList<DragonCrashReport> LoadRecent(int maxReports = 3)
    {
        if (maxReports < 0 || maxReports > MaxReports)
            throw new ArgumentOutOfRangeException(nameof(maxReports), $"Crash report count must be between 0 and {MaxReports}.");
        if (maxReports == 0 || !Directory.Exists(_storageDirectory))
            return Array.Empty<DragonCrashReport>();

        var reports = new List<DragonCrashReport>(maxReports);
        foreach (var path in Directory.EnumerateFiles(_storageDirectory, "dragon-crash-*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Take(MaxReports))
        {
            if (reports.Count >= maxReports)
                break;

            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaxReportBytes)
                    continue;

                var json = File.ReadAllText(path, Encoding.UTF8);
                var report = JsonSerializer.Deserialize<DragonCrashReport>(json, _jsonOptions);
                if (report is null
                    || report.SchemaVersion != CurrentSchemaVersion
                    || string.IsNullOrWhiteSpace(report.ExceptionType)
                    || report.FingerprintSha256.Length != 64
                    || report.ExceptionChainTypes is null
                    || report.Frames is null
                    || report.ExceptionChainTypes.Count > 8
                    || report.Frames.Count > MaxFrames)
                {
                    continue;
                }

                reports.Add(report);
            }
            catch
            {
                // Support export must survive a partially written or manually modified crash directory.
            }
        }

        return reports.AsReadOnly();
    }

    private static DragonCrashReport CreateReport(Exception exception)
    {
        var chain = new List<string>(4);
        for (var current = exception; current is not null && chain.Count < 8; current = current.InnerException)
            chain.Add(current.GetType().FullName ?? current.GetType().Name);

        var frames = (new StackTrace(exception, fNeedFileInfo: false).GetFrames() ?? Array.Empty<StackFrame>())
            .Select(frame => frame.GetMethod())
            .Where(method => method is not null)
            .Select(method =>
            {
                var declaringType = method!.DeclaringType?.FullName;
                return string.IsNullOrWhiteSpace(declaringType)
                    ? method.Name
                    : declaringType + "." + method.Name;
            })
            .Take(MaxFrames)
            .ToArray();

        var fingerprintMaterial = string.Join("\n", new[]
        {
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.HResult.ToString(System.Globalization.CultureInfo.InvariantCulture),
            exception.Message ?? string.Empty,
            string.Join("|", chain),
            string.Join("|", frames)
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintMaterial)));

        return new DragonCrashReport(
            CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.HResult,
            fingerprint,
            Array.AsReadOnly(chain.ToArray()),
            Array.AsReadOnly(frames));
    }

    private void TrimOldReports()
    {
        try
        {
            var files = Directory.EnumerateFiles(_storageDirectory, "dragon-crash-*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                .Skip(MaxReports)
                .ToArray();
            foreach (var path in files)
            {
                try { File.Delete(path); } catch { }
            }
        }
        catch
        {
            // Rotation is best-effort. A write must not fail just because cleanup cannot run.
        }
    }
}
