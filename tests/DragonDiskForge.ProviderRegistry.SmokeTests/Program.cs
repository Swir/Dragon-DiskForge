using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Providers;

var tempRoot = Path.Combine(Path.GetTempPath(), $"dragon-provider-registry-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);
var samplePath = Path.Combine(tempRoot, "sample.iso");
await File.WriteAllBytesAsync(samplePath, new byte[128]);

try
{
    ValidateCapabilities();
    ValidateRegistrationContract();
    ValidateDisplayNameFallback();
    await ValidateDescriptorSnapshotAsync(samplePath);
    await ValidateDeterministicTieBreakAsync(samplePath);
    await ValidateExtensionPreferenceAsync(samplePath);
    await ValidateFallbackAsync(samplePath);
    await ValidateFailureIsolationAsync(samplePath);
    await ValidateInspectionFailureFallbackAsync(samplePath);
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

static void ValidateRegistrationContract()
{
    ExpectThrows<ArgumentNullException>(
        () => _ = new ProviderRegistration(null!),
        "registration rejects null provider");

    ExpectThrows<ArgumentException>(
        () => _ = new ProviderRegistration(new FakeProvider("", [".iso"], canHandle: false)),
        "registration rejects empty provider id");

    ExpectThrows<ArgumentException>(
        () => _ = new ProviderRegistration(new FakeProvider(" bad", [".iso"], canHandle: false)),
        "registration rejects provider id whitespace");

    ExpectThrows<ArgumentException>(
        () => _ = new ProviderRegistration(new FakeProvider("bad id", [".iso"], canHandle: false)),
        "registration rejects invalid provider id characters");

    ExpectThrows<ArgumentException>(
        () => _ = new ProviderRegistration(new FakeProvider("empty-ext", [""], canHandle: false)),
        "registration rejects empty extension entries");

    ExpectThrows<ArgumentException>(
        () => _ = new ProviderRegistration(new FakeProvider("bad-ext", ["../iso"], canHandle: false)),
        "registration rejects path-like extension entries");

    ExpectThrows<ArgumentException>(
        () => _ = new ProviderRegistration(new FakeProvider("duplicate-ext", ["iso", ".ISO"], canHandle: false)),
        "registration rejects duplicate normalized extensions");

    ExpectThrows<ArgumentException>(
        () => _ = new ProviderRegistration(new FakeProvider("null-ext", null!, canHandle: false)),
        "registration rejects null extension collections");

    var signatureOnly = new ProviderRegistration(new FakeProvider("signature-only", [], canHandle: false));
    Check(signatureOnly.Descriptor.Extensions.Count == 0,
        "signature-only providers may intentionally declare no extensions");
}

static void ValidateDisplayNameFallback()
{
    var blank = new FakeDirectProvider("blank-name", [".iso"], canHandle: false, displayName: "   ");
    var descriptor = ProviderDescriptor.From(blank);
    Check(descriptor.DisplayName == "blank-name", "blank display name falls back to stable provider id");
}

static async Task ValidateDescriptorSnapshotAsync(string path)
{
    var mutableExtensions = new List<string> { ".iso" };
    var provider = new FakeProvider("snapshot", mutableExtensions, canHandle: true);
    var registration = new ProviderRegistration(provider, Priority: 50);

    mutableExtensions.Clear();
    mutableExtensions.Add(".img");

    var registry = new ProviderRegistry([registration]);
    var resolution = await registry.ResolveAsync(path);

    Check(registration.Descriptor.Extensions.SequenceEqual([".iso"]),
        "registration snapshots normalized provider extensions");
    Check(resolution.Diagnostics.Count == 1 && resolution.Diagnostics[0].ExtensionMatched,
        "resolution uses immutable descriptor snapshot rather than mutable provider metadata");
}

static async Task ValidateDeterministicTieBreakAsync(string path)
{
    var zeta = new FakeProvider("zeta", [".iso"], canHandle: true);
    var alpha = new FakeProvider("alpha", [".iso"], canHandle: true);
    var registry = new ProviderRegistry([
        new ProviderRegistration(zeta, Priority: 10),
        new ProviderRegistration(alpha, Priority: 10)
    ]);

    Check(registry.Providers.Select(x => x.Id).SequenceEqual(["alpha", "zeta"]),
        "equal-priority provider descriptors are ordered deterministically by id");

    var resolution = await registry.ResolveAsync(path);
    Check(resolution.Provider?.Id == "alpha",
        "equal-priority extension matches resolve deterministically by provider id");
    Check(alpha.ProbeCount == 1 && zeta.ProbeCount == 0,
        "deterministic winner stops later equal-priority probes");
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

static async Task ValidateInspectionFailureFallbackAsync(string path)
{
    var brokenInspector = new FakeProvider(
        "broken-inspect", [".iso"], canHandle: true,
        inspectError: new InvalidDataException("damaged inspector"));
    var fallback = new FakeProvider("fallback-inspect", [".iso"], canHandle: true, format: "FALLBACK");
    var registry = new ProviderRegistry([
        new ProviderRegistration(brokenInspector, Priority: 100),
        new ProviderRegistration(fallback, Priority: 10)
    ]);

    var info = await registry.InspectAsync(path);
    Check(info.Format == "FALLBACK", "inspection failure falls back to the next accepted provider");
    Check(brokenInspector.InspectCount == 1, "failing inspector was attempted once");
    Check(fallback.InspectCount == 1, "fallback inspector runs after isolated inspection failure");
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
    ExpectThrows<ArgumentException>(
        () => _ = new ProviderRegistry([
            new FakeProvider("same", [".iso"], canHandle: false),
            new FakeProvider("SAME", [".img"], canHandle: false)
        ]),
        "provider ids are unique case-insensitively");
}

static async Task ValidateInspectionAsync(string path)
{
    var provider = new FakeProvider("inspect", [".iso"], canHandle: true);
    var registry = new ProviderRegistry([provider]);
    var info = await registry.InspectAsync(path);

    Check(info.Format == "FAKE", "registry delegates inspection to the resolved provider");
    Check(provider.InspectCount == 1, "resolved provider inspection runs exactly once");
}

static void ExpectThrows<TException>(Action action, string name)
    where TException : Exception
{
    try
    {
        action();
        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
    }
    catch (TException)
    {
        Check(true, name);
    }
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
    private readonly Exception? _inspectError;
    private readonly string _format;

    public FakeProvider(
        string id,
        IReadOnlyCollection<string> extensions,
        bool canHandle,
        Exception? probeError = null,
        Exception? inspectError = null,
        string format = "FAKE")
    {
        Id = id;
        Extensions = extensions;
        _canHandle = canHandle;
        _probeError = probeError;
        _inspectError = inspectError;
        _format = format;
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
        if (_inspectError is not null)
            throw _inspectError;

        var file = new FileInfo(path);
        return ValueTask.FromResult(new DiskImageInfo(
            file.FullName,
            file.Name,
            _format,
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
    private readonly string _displayName;

    public FakeDirectProvider(
        string id,
        IReadOnlyCollection<string> extensions,
        bool canHandle,
        string displayName = "Fake Direct")
        : base(id, extensions, canHandle)
    {
        _displayName = displayName;
    }

    public string DisplayName => _displayName;

    public Task<IReadOnlyList<ExplorerEntry>> ListAsync(string imagePath, string directoryPath, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExplorerEntry>>([]);

    public Task<IReadOnlyList<ExplorerEntry>> SearchAsync(string imagePath, string startPath, string query, int maxResults = 200, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExplorerEntry>>([]);

    public Task CopyOutAsync(string imagePath, string sourcePath, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
