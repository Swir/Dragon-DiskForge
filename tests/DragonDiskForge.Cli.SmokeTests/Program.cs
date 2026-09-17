using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DragonDiskForge.Cli;

var root = Path.Combine(Path.GetTempPath(), "dragon-diskforge-cli-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var image = Path.Combine(root, "unknown.payload");
    var payload = Encoding.UTF8.GetBytes("Dragon DiskForge CLI smoke fixture\n");
    await File.WriteAllBytesAsync(image, payload);

    var analyze = await RunAsync("analyze", image, "--format", "json");
    Require(analyze.ExitCode == 0, "Analyze JSON must succeed for a readable unknown image.");
    Require(string.IsNullOrWhiteSpace(analyze.Stderr), "Successful analyze must keep stderr empty.");
    using (var document = JsonDocument.Parse(analyze.Stdout))
    {
        Require(document.RootElement.GetProperty("Image").GetProperty("FileName").GetString() == "unknown.payload",
            "Analyze JSON must expose the inspected file name.");
        Require(document.RootElement.GetProperty("Provider").ValueKind == JsonValueKind.Null,
            "Unknown input must not gain a fabricated provider in CLI JSON.");
    }

    var expected256 = Convert.ToHexString(SHA256.HashData(payload));
    var expected512 = Convert.ToHexString(SHA512.HashData(payload));
    var verify = await RunAsync(
        "verify", image,
        "--sha256", expected256.ToLowerInvariant(),
        "--sha512", expected512,
        "--format", "json");
    Require(verify.ExitCode == 0, "Matching expected digests must return exit code 0.");
    Require(string.IsNullOrWhiteSpace(verify.Stderr), "Successful verify must keep stderr empty.");
    using (var document = JsonDocument.Parse(verify.Stdout))
    {
        Require(document.RootElement.GetProperty("MatchesAllExpected").GetBoolean(),
            "Verify JSON must report a successful expected-digest match.");
        Require(document.RootElement.GetProperty("BytesHashed").GetInt64() == payload.Length,
            "Verify JSON must report the exact hashed byte count.");
    }

    var mismatch = await RunAsync(
        "verify", image,
        "--sha256", new string('0', 64),
        "--format", "json");
    Require(mismatch.ExitCode == 4, "Digest mismatch must use the documented automation exit code 4.");
    using (var document = JsonDocument.Parse(mismatch.Stdout))
    {
        Require(!document.RootElement.GetProperty("MatchesAllExpected").GetBoolean(),
            "Mismatch JSON must remain machine-readable and explicitly false.");
    }

    var formats = await RunAsync("formats", "--format", "json");
    Require(formats.ExitCode == 0, "Provider listing must succeed.");
    Require(string.IsNullOrWhiteSpace(formats.Stderr), "Provider listing must keep stderr empty.");
    using (var document = JsonDocument.Parse(formats.Stdout))
    {
        var providers = document.RootElement.EnumerateArray().ToArray();
        Require(providers.Length >= 12, "CLI must expose the complete canonical built-in provider registry.");
        var ids = providers.Select(x => x.GetProperty("Id").GetString()).ToArray();
        Require(ids.All(x => !string.IsNullOrWhiteSpace(x)), "Every CLI provider entry must have a stable id.");
        Require(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Length,
            "CLI provider ids must be unique.");
    }

    var missing = await RunAsync("verify", Path.Combine(root, "missing.img"), "--format", "json");
    Require(missing.ExitCode == 3, "Missing input must return the documented input error code.");
    Require(string.IsNullOrWhiteSpace(missing.Stdout), "Input failures must not contaminate stdout.");
    Require(!string.IsNullOrWhiteSpace(missing.Stderr), "Input failures must explain themselves on stderr.");

    var invalidDigest = await RunAsync("verify", image, "--sha256", "not-a-digest");
    Require(invalidDigest.ExitCode == 2, "Malformed expected digests must be rejected as usage errors.");
    Require(string.IsNullOrWhiteSpace(invalidDigest.Stdout), "Usage errors must keep stdout clean.");

    Console.WriteLine("CLI smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task<RunResult> RunAsync(params string[] args)
{
    using var stdout = new StringWriter();
    using var stderr = new StringWriter();
    var exitCode = await CliRunner.RunAsync(args, stdout, stderr);
    return new RunResult(exitCode, stdout.ToString(), stderr.ToString());
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct RunResult(int ExitCode, string Stdout, string Stderr);
