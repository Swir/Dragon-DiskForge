using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

var tempRoot = Path.Combine(Path.GetTempPath(), $"dragon-provider-registry-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);
var samplePath = Path.Combine(tempRoot, "sample.iso");
await File.WriteAllBytesAsync(samplePath, new byte[128]);

try
{
    ValidateCapabilities();
    await ValidateExtensionPreferenceAsync(samplePath);
    await ValidateFallbackAsync(samplePath);
    await ValidateFailureIsolationAsync(samplePath);
    await ValidateCancellationAsync(samplePath);
    ValidateDuplicateIds();
    await ValidateInspectionAsync(samplePath);
    Console.WriteLine("\nDragon DiskForge provider-registry smoke tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL  Provider registry smoke test: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

static void ValidateCapabilities()
{
    var direct = new FakeDirectProvider("direct", ["iso"], canHandle: true);
    var descriptor = ProviderDescriptor.From(direct, priority: 25);

    Check(descriptor.Extensions.SequenceEqual([".iso"]), "provider descriptor normalizes extensions");
    Check(descriptor.Priority == 25, "provider descriptor preserves explicit priority");
    Check(descriptor.Capabilities.HasFlag(ProviderCapabilities.Inspect), "all providers report inspect capability");
    Check(descriptor.Capabilities.HasFlag(ProviderCapabilities.DirectBrowse), "direct provider reports direct-browse capability");
    Check(descriptor.Capabilities.HasFlag(ProviderCapabilities.Search), "direct provider reports search capability");
    Check(descriptor.Capabilities.HasFlag(ProviderCapabilities.CopyOut), "direct provider reports copy-out capability");
}

static async Task ValidateExtensionPreferenceAsync(string path)
{
    var highPriorityWrongExtension = new FakeProvider("high", [".img"], canHandle: true);
    var lowerPriorityMatching = new FakeProvider("iso", [".iso"], canHandle: true);
    var registry = new ProviderRegistry([
        new ProviderRegistration(highPriorityWrongExtension, Priority: 100),
        new ProviderRegistration(lowerPriorityMatching, Priority: 1)
    ]);

    var resolution = await registry.ResolveAsync(path);
    Check(resolution.Provider?.Id == "iso", "extension match is probed before non-matching providers");
    Check(lowerPriorityMatching.ProbeCount == 1, "matching provider was probed");
    Check(highPriorityWrongExtension.ProbeCount == 0, "non-matching fallback is not probed after a match succeeds");
}

static async Task ValidateFallbackAsync(string path)
{
    var extensionProvider = new FakeProvider("extension", [".iso"], canHandle: false);
    var signatureFallback = new FakeProvider("signature", [".raw"], canHandle: true);
    var registry = new ProviderRegistry([extensionProvider, signatureFallback]);

    var resolution = await registry.ResolveAsync(path);
    Check(resolution.Provider?.Id == "signature", "registry falls back beyond extension candidates when signatures require it");
    Check(resolution.Diagnostics.Count == 2, "fallback resolution preserves probe diagnostics");
    Check(resolution.Diagnostics[0].ExtensionMatched, "diagnostics mark extension-matched probe");
    Check(!resolution.Diagnostics[1].ExtensionMatched && resolution.Diagnostics[1].Supported,
        "diagnostics mark successful signature fallback");
}

static async Task ValidateFailureIsolationAsync(string path)
{
    var broken = new FakeProvider("broken", [".iso"], canHandle: false, probeError: new InvalidDataException("broken parser"));
    var fallback = new FakeProvider("fallback", [".iso"], canHandle: true);
    var registry = new ProviderRegistry([
        new ProviderRegistration(broken, Priority: 100),
        new ProviderRegistration(fallback, Priority: 10)
    ]);

    var resolution = await registry.ResolveAsync(path);
    Check(resolution.Provider?.Id == "fallback", "one provider failure does not stop fallback discovery");
    Check(resolution.Diagnostics.Any(x => x.ProviderId == "broken" && x.ErrorMessage?.Contains("broken parser") == true),
        "provider failure is preserved in diagnostics");
}

static async Task ValidateCancellationAsync(string path)
{
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    var registry = new ProviderRegistry([new FakeProvider("cancel", [".iso"], canHandle: true)]);

    try
    {
        await registry.ResolveAsync(path, cts.Token);
        throw new InvalidOperationException("pre-cancelled provider resolution unexpectedly completed");
    }
    catch (OperationCanceledException)
    {
        Check(true, "provider resolution preserves cancellation instead of isolating it as a parser failure");
    }
}

static void ValidateDuplicateIds()
{
    try
    {
        _ = new ProviderRegistry([
            new FakeProvider("same", [".iso"], canHandle: false),
            new FakeProvider("SAME", [".img"], canHandle: false)
        ]);
        throw new InvalidOperationException("duplicate provider ids were unexpectedly accepted");
    }
    catch (ArgumentException)
    {
        Check(true, "provider ids are unique case-insensitively");
    }
}

static async Task ValidateInspectionAsync(string path)
{
    var provider = new FakeProvider("inspect", [".iso"], canHandle: true);
    var registry = new ProviderRegistry([provider]);
    var info = await registry.InspectAsync(path);

    Check(info.Format == "FAKE", "registry delegates inspection to the resolved provider");
    Check(provider.InspectCount == 1, "resolved provider inspection runs exactly once");
}

static void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    Console.WriteLine($"PASS  {name}");
}

class FakeProvider : IDiskImageProvider
{
    private readonly bool _canHandle;
    private readonly Exception? _probeError;

    public FakeProvider(string id, IReadOnlyCollection<string> extensions, bool canHandle, Exception? probeError = null)
    {
        Id = id;
        Extensions = extensions;
        _canHandle = canHandle;
        _probeError = probeError;
    }

    public string Id { get; }
    public IReadOnlyCollection<string> Extensions { get; }
    public int ProbeCount { get; private set; }
    public int InspectCount { get; private set; }

    public ValueTask<bool> CanHandleAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProbeCount++;
        if (_probeError is not null)
            throw _probeError;
        return ValueTask.FromResult(_canHandle);
    }

    public ValueTask<DiskImageInfo> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InspectCount++;
        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName,
            file.Name,
            "FAKE",
            file.Length,
            "fake-provider",
            CanExplore: false,
            CanMount: false,
            CanConvert: false,
            CanVerify: true));
    }
}

sealed class FakeDirectProvider : FakeProvider, IDirectBrowseProvider
{
    public FakeDirectProvider(string id, IReadOnlyCollection<string> extensions, bool canHandle)
        : base(id, extensions, canHandle)
    {
    }

    public string DisplayName => "Fake Direct";

    public Task<IReadOnlyList<ExplorerEntry>> ListAsync(string imagePath, string directoryPath, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExplorerEntry>>([]);

    public Task<IReadOnlyList<ExplorerEntry>> SearchAsync(string imagePath, string startPath, string query, int maxResults = 200, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExplorerEntry>>([]);

    public Task CopyOutAsync(string imagePath, string sourcePath, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
