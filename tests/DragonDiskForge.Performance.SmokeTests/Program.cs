using System.Diagnostics;
using System.Text.Json;
using DragonDiskForge.Core.Providers;
using DragonDiskForge.Core.Services;

const long VerificationBytes = 128L * 1024 * 1024;
const long SparseRecognitionBytes = 8L * 1024 * 1024 * 1024;
const double MinimumVerificationMiBPerSecond = 2.0;
const double MaximumVerificationSeconds = 60.0;
const long MaximumVerificationAllocatedBytes = 128L * 1024 * 1024;
const double MaximumRecognitionSeconds = 20.0;
const long MaximumRecognitionAllocatedBytes = 128L * 1024 * 1024;

var outputPath = ParseOutputPath(args);
var root = Path.Combine(Path.GetTempPath(), "dragon-diskforge-performance-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var verificationPath = Path.Combine(root, "verification-large.img");
    await CreateSparseFileAsync(verificationPath, VerificationBytes);

    // Small warm-up keeps JIT/startup noise out of the large-image regression measurement.
    var warmupPath = Path.Combine(root, "warmup.img");
    await CreateSparseFileAsync(warmupPath, 1024 * 1024);
    await new ImageVerificationService().ComputeAsync(warmupPath);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var verificationAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var verificationStopwatch = Stopwatch.StartNew();
    var verification = await new ImageVerificationService().ComputeAsync(verificationPath);
    verificationStopwatch.Stop();
    var verificationAllocated = GC.GetTotalAllocatedBytes(precise: true) - verificationAllocatedBefore;
    var verificationMiBPerSecond = (VerificationBytes / 1024d / 1024d)
        / Math.Max(verificationStopwatch.Elapsed.TotalSeconds, 0.001d);

    Require(verification.SizeBytes == VerificationBytes,
        "Dual-hash verification processes the exact large-image byte count.");
    Require(verificationStopwatch.Elapsed.TotalSeconds <= MaximumVerificationSeconds,
        $"128 MiB dual-hash verification stays below the {MaximumVerificationSeconds:0}s regression ceiling.");
    Require(verificationMiBPerSecond >= MinimumVerificationMiBPerSecond,
        $"Dual-hash throughput stays above the {MinimumVerificationMiBPerSecond:0.0} MiB/s safety floor.");
    Require(verificationAllocated <= MaximumVerificationAllocatedBytes,
        $"Dual-hash verification managed allocations stay below {MaximumVerificationAllocatedBytes / 1024 / 1024} MiB.");

    var recognitionPath = Path.Combine(root, "recognition-8gib.img");
    await CreateSparseFileAsync(recognitionPath, SparseRecognitionBytes);
    var recognitionService = new FileSystemRecognitionService(ProviderRegistryFactory.CreateDefault());

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var recognitionAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var recognitionStopwatch = Stopwatch.StartNew();
    var recognition = await recognitionService.AnalyzeAsync(recognitionPath);
    recognitionStopwatch.Stop();
    var recognitionAllocated = GC.GetTotalAllocatedBytes(precise: true) - recognitionAllocatedBefore;

    Require(recognition.PhysicalImageSizeBytes == SparseRecognitionBytes,
        "Filesystem recognition preserves the exact 8 GiB logical image size.");
    Require(recognitionStopwatch.Elapsed.TotalSeconds <= MaximumRecognitionSeconds,
        $"Bounded filesystem recognition stays below the {MaximumRecognitionSeconds:0}s large-image ceiling.");
    Require(recognitionAllocated <= MaximumRecognitionAllocatedBytes,
        $"Bounded filesystem recognition managed allocations stay below {MaximumRecognitionAllocatedBytes / 1024 / 1024} MiB.");

    var report = new
    {
        schemaVersion = 1,
        createdUtc = DateTimeOffset.UtcNow,
        environment = new
        {
            os = Environment.OSVersion.VersionString,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            processorCount = Environment.ProcessorCount
        },
        verification = new
        {
            sizeBytes = VerificationBytes,
            elapsedMilliseconds = verificationStopwatch.Elapsed.TotalMilliseconds,
            throughputMiBPerSecond = verificationMiBPerSecond,
            managedAllocatedBytes = verificationAllocated,
            thresholds = new
            {
                maximumSeconds = MaximumVerificationSeconds,
                minimumMiBPerSecond = MinimumVerificationMiBPerSecond,
                maximumManagedAllocatedBytes = MaximumVerificationAllocatedBytes
            }
        },
        boundedRecognition = new
        {
            logicalSizeBytes = SparseRecognitionBytes,
            elapsedMilliseconds = recognitionStopwatch.Elapsed.TotalMilliseconds,
            managedAllocatedBytes = recognitionAllocated,
            detectionCount = recognition.Detections.Count,
            providerId = recognition.ProviderId,
            thresholds = new
            {
                maximumSeconds = MaximumRecognitionSeconds,
                maximumManagedAllocatedBytes = MaximumRecognitionAllocatedBytes
            }
        }
    };

    if (outputPath is not null)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        await File.WriteAllTextAsync(
            fullOutputPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BENCHMARK " + fullOutputPath);
    }

    Console.WriteLine($"METRIC dual_hash_mib_per_sec={verificationMiBPerSecond:F2}");
    Console.WriteLine($"METRIC dual_hash_allocated_bytes={verificationAllocated}");
    Console.WriteLine($"METRIC recognition_8gib_ms={recognitionStopwatch.Elapsed.TotalMilliseconds:F2}");
    Console.WriteLine($"METRIC recognition_allocated_bytes={recognitionAllocated}");
    Console.WriteLine("Dragon DiskForge performance regression smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task CreateSparseFileAsync(string path, long length)
{
    await using var stream = new FileStream(
        path,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        4096,
        FileOptions.Asynchronous);
    stream.SetLength(length);
    await stream.FlushAsync();
}

static string? ParseOutputPath(string[] arguments)
{
    for (var i = 0; i < arguments.Length; i++)
    {
        if (!string.Equals(arguments[i], "--output", StringComparison.OrdinalIgnoreCase))
            continue;
        if (i + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[i + 1]))
            throw new ArgumentException("--output requires a path.");
        return arguments[i + 1];
    }

    return null;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
    Console.WriteLine("PASS  " + message);
}
