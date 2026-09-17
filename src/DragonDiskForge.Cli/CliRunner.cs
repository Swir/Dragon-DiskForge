using System.Text.Json;
using System.Text.Json.Serialization;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

namespace DragonDiskForge.Cli;

public static class CliRunner
{
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;
    private const int ExitInput = 3;
    private const int ExitVerificationMismatch = 4;
    private const int ExitCancelled = 130;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                await stdout.WriteLineAsync(HelpText);
                return ExitSuccess;
            }

            return args[0].ToLowerInvariant() switch
            {
                "analyze" => await RunAnalyzeAsync(args, stdout, cancellationToken),
                "verify" => await RunVerifyAsync(args, stdout, cancellationToken),
                "formats" => await RunFormatsAsync(args, stdout, cancellationToken),
                _ => throw new CliUsageException($"Unknown command '{args[0]}'.")
            };
        }
        catch (CliUsageException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            await stderr.WriteLineAsync("Run 'dragon-diskforge --help' for usage.");
            return ExitUsage;
        }
        catch (OperationCanceledException)
        {
            await stderr.WriteLineAsync("Operation cancelled.");
            return ExitCancelled;
        }
        catch (Exception ex) when (ex is FileNotFoundException
            or DirectoryNotFoundException
            or UnauthorizedAccessException
            or InvalidDataException
            or NotSupportedException
            or IOException)
        {
            await stderr.WriteLineAsync(ex.Message);
            return ExitInput;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"Unexpected failure: {ex.Message}");
            return ExitFailure;
        }
    }

    private static async Task<int> RunAnalyzeAsync(
        string[] args,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        var path = RequirePath(args, "analyze");
        var options = ParseOptions(args, 2, "--format");
        var format = ResolveFormat(options);

        var report = await new ImageReportService(ProviderRegistryFactory.CreateDefault())
            .AnalyzeAsync(path, cancellationToken);
        var output = format == OutputFormat.Json
            ? ImageReportService.ToJson(report)
            : ImageReportService.ToText(report);

        await stdout.WriteLineAsync(output.TrimEnd());
        return ExitSuccess;
    }

    private static async Task<int> RunVerifyAsync(
        string[] args,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        var path = RequirePath(args, "verify");
        var options = ParseOptions(args, 2, "--format", "--sha256", "--sha512");
        var format = ResolveFormat(options);
        var expectedSha256 = NormalizeExpectedDigest(options.GetValueOrDefault("--sha256"), 64, "SHA-256");
        var expectedSha512 = NormalizeExpectedDigest(options.GetValueOrDefault("--sha512"), 128, "SHA-512");

        var verification = await new ImageVerificationService().ComputeAsync(path, cancellationToken: cancellationToken);
        var sha256Matches = expectedSha256 is null || string.Equals(expectedSha256, verification.Sha256, StringComparison.OrdinalIgnoreCase);
        var sha512Matches = expectedSha512 is null || string.Equals(expectedSha512, verification.Sha512, StringComparison.OrdinalIgnoreCase);
        var result = new VerificationOutput(
            Path.GetFullPath(path),
            verification.SizeBytes,
            verification.Sha256,
            verification.Sha512,
            expectedSha256,
            expectedSha256 is null ? null : sha256Matches,
            expectedSha512,
            expectedSha512 is null ? null : sha512Matches,
            sha256Matches && sha512Matches);

        if (format == OutputFormat.Json)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            await stdout.WriteLineAsync(ToVerificationText(result));
        }

        return result.MatchesAllExpected ? ExitSuccess : ExitVerificationMismatch;
    }

    private static async Task<int> RunFormatsAsync(
        string[] args,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = ParseOptions(args, 1, "--format");
        var format = ResolveFormat(options);
        var providers = ProviderRegistryFactory.CreateDefault().Providers
            .Select(x => new ProviderOutput(
                x.Id,
                x.DisplayName,
                x.Extensions.ToArray(),
                x.Capabilities.ToString(),
                x.Priority))
            .ToArray();

        if (format == OutputFormat.Json)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(providers, JsonOptions));
        }
        else
        {
            foreach (var provider in providers)
            {
                await stdout.WriteLineAsync(
                    $"{provider.Id}\t{provider.DisplayName}\t{string.Join(',', provider.Extensions)}\t{provider.Capabilities}");
            }
        }

        return ExitSuccess;
    }

    private static string RequirePath(string[] args, string command)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("--", StringComparison.Ordinal))
            throw new CliUsageException($"Command '{command}' requires an image path.");
        return args[1];
    }

    private static Dictionary<string, string> ParseOptions(
        string[] args,
        int startIndex,
        params string[] allowedOptions)
    {
        var allowed = new HashSet<string>(allowedOptions, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = startIndex; index < args.Length; index++)
        {
            var option = args[index];
            if (!allowed.Contains(option))
                throw new CliUsageException($"Unknown option '{option}'.");
            if (result.ContainsKey(option))
                throw new CliUsageException($"Option '{option}' was specified more than once.");
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                throw new CliUsageException($"Option '{option}' requires a value.");
            result[option] = args[index];
        }

        return result;
    }

    private static OutputFormat ResolveFormat(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("--format", out var value))
            return OutputFormat.Text;

        return value.ToLowerInvariant() switch
        {
            "text" => OutputFormat.Text,
            "json" => OutputFormat.Json,
            _ => throw new CliUsageException("--format must be 'text' or 'json'.")
        };
    }

    private static string? NormalizeExpectedDigest(string? value, int length, string displayName)
    {
        if (value is null)
            return null;

        var normalized = value.Trim();
        if (normalized.Length != length || !normalized.All(Uri.IsHexDigit))
            throw new CliUsageException($"Expected {displayName} must contain exactly {length} hexadecimal characters.");
        return normalized.ToUpperInvariant();
    }

    private static string ToVerificationText(VerificationOutput result)
    {
        var lines = new List<string>
        {
            $"File: {result.Path}",
            $"Bytes hashed: {result.BytesHashed}",
            $"SHA-256: {result.Sha256}",
            $"SHA-512: {result.Sha512}"
        };

        if (result.ExpectedSha256 is not null)
            lines.Add($"Expected SHA-256: {result.ExpectedSha256} | Match: {result.Sha256Matches}");
        if (result.ExpectedSha512 is not null)
            lines.Add($"Expected SHA-512: {result.ExpectedSha512} | Match: {result.Sha512Matches}");

        lines.Add($"Verification: {(result.MatchesAllExpected ? "PASS" : "MISMATCH")}");
        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "help";

    private enum OutputFormat
    {
        Text,
        Json
    }

    private sealed record VerificationOutput(
        string Path,
        long BytesHashed,
        string Sha256,
        string Sha512,
        string? ExpectedSha256,
        bool? Sha256Matches,
        string? ExpectedSha512,
        bool? Sha512Matches,
        bool MatchesAllExpected);

    private sealed record ProviderOutput(
        string Id,
        string DisplayName,
        string[] Extensions,
        string Capabilities,
        int Priority);

    private sealed class CliUsageException(string message) : Exception(message);

    private const string HelpText = """
Dragon DiskForge CLI — read-only automation surface

Usage:
  dragon-diskforge analyze <image> [--format text|json]
  dragon-diskforge verify <image> [--sha256 <hex>] [--sha512 <hex>] [--format text|json]
  dragon-diskforge formats [--format text|json]

Commands:
  analyze   Run the same provider-backed image intelligence used by the desktop app.
  verify    Compute SHA-256 and SHA-512 in one bounded sequential pass. When an expected
            digest is supplied, exit code 4 indicates a mismatch.
  formats   List the canonical built-in providers and their truthful capabilities.

Automation contract:
  stdout contains only the requested text or one complete JSON document.
  diagnostics and errors are written to stderr.
  no command modifies the inspected image or physical media.

Exit codes: 0 success, 1 unexpected failure, 2 usage error, 3 input/operation error,
            4 verification mismatch, 130 cancelled.
""";
}
