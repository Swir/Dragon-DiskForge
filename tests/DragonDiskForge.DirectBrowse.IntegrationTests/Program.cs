using System.Diagnostics;
using System.Text;
using DragonDiskForge.Core.Providers;

const int SectorSizeForProbe = 2048 * 20;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("SKIP  Direct ISO browse integration requires Windows IMAPI.");
    return;
}

var failures = new List<string>();
var tempRoot = Path.Combine(Path.GetTempPath(), $"dragon-direct-iso-{Guid.NewGuid():N}");
var sourceRoot = Path.Combine(tempRoot, "source");
var docsRoot = Path.Combine(sourceRoot, "Docs");
var exportRoot = Path.Combine(tempRoot, "export");
Directory.CreateDirectory(docsRoot);
Directory.CreateDirectory(exportRoot);

var markerName = "dragon-direct.txt";
var markerContent = "Dragon DiskForge direct ISO provider test.";
var nestedName = "readme.txt";
var nestedContent = "Nested provider-backed file.";
var unicodeName = "zażółć.txt";
var unicodeContent = "Joliet Unicode filename.";

try
{
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, markerName), markerContent);
    await File.WriteAllTextAsync(Path.Combine(docsRoot, nestedName), nestedContent);
    await File.WriteAllTextAsync(Path.Combine(sourceRoot, unicodeName), unicodeContent);

    var imagePath = Path.Combine(tempRoot, "direct.iso");
    Console.WriteLine("INFO  Creating disposable Joliet/ISO9660 image with Windows IMAPI2FS...");
    await CreateIsoAsync(sourceRoot, imagePath);
    Check(File.Exists(imagePath) && new FileInfo(imagePath).Length > 0, "IMAPI produced direct-browse ISO fixture");
    Check(!await QueryAttachedAsync(imagePath), "direct-browse ISO starts detached");

    var registry = DirectBrowseProviderRegistry.CreateDefault();
    var provider = await registry.ResolveAsync(imagePath);
    Check(provider is not null, "provider registry resolves the ISO9660 direct provider");
    Check(provider?.Id == "iso9660", "resolved provider reports iso9660 id");

    var explorer = await provider!.OpenAsync(imagePath);
    Check(explorer.RootPath == "/", "direct explorer exposes virtual root path");

    var rootEntries = await explorer.ListAsync("/");
    var marker = rootEntries.FirstOrDefault(x => !x.IsDirectory && x.Name.Equals(markerName, StringComparison.OrdinalIgnoreCase));
    var docs = rootEntries.FirstOrDefault(x => x.IsDirectory && x.Name.Equals("Docs", StringComparison.OrdinalIgnoreCase));
    var unicode = rootEntries.FirstOrDefault(x => !x.IsDirectory && x.Name.Equals(unicodeName, StringComparison.OrdinalIgnoreCase));
    Check(marker is not null, "direct provider lists root file without mounting");
    Check(marker?.SizeBytes == Encoding.UTF8.GetByteCount(markerContent), "direct provider reports root file size");
    Check(docs is not null, "direct provider lists nested folder without mounting");
    Check(unicode is not null, "direct provider decodes Joliet Unicode names");

    var docsEntries = await explorer.ListAsync("/Docs");
    Check(docsEntries.Any(x => x.Name.Equals(nestedName, StringComparison.OrdinalIgnoreCase)),
        "direct provider enumerates nested folder content");

    var search = await explorer.SearchAsync("/", "readme", maxResults: 50);
    Check(search.Any(x => x.FullPath.Equals("/Docs/readme.txt", StringComparison.OrdinalIgnoreCase)),
        "direct provider searches the image without mounting");

    var copyProgress = new CaptureProgress();
    await explorer.CopyOutAsync(marker!.FullPath, exportRoot, copyProgress);
    var copiedMarker = Path.Combine(exportRoot, markerName);
    Check(File.Exists(copiedMarker), "direct provider copies a file out without mounting");
    Check(await File.ReadAllTextAsync(copiedMarker) == markerContent, "direct file copy preserves content");
    Check(copyProgress.Last >= 0.999d, "direct file copy reports completion");

    var folderExport = Path.Combine(tempRoot, "folder-export");
    Directory.CreateDirectory(folderExport);
    copyProgress.Reset();
    await explorer.CopyOutAsync(docs!.FullPath, folderExport, copyProgress);
    var copiedNested = Path.Combine(folderExport, "Docs", nestedName);
    Check(File.Exists(copiedNested), "direct provider copies a folder tree out");
    Check(await File.ReadAllTextAsync(copiedNested) == nestedContent, "direct folder copy preserves nested content");
    Check(copyProgress.Last >= 0.999d, "direct folder copy reports completion");

    try
    {
        await explorer.CopyOutAsync(marker.FullPath, exportRoot);
        Check(false, "direct provider refuses silent overwrite");
    }
    catch (IOException)
    {
        Check(true, "direct provider refuses silent overwrite");
    }

    try
    {
        await explorer.ListAsync("/../outside");
        Check(false, "direct provider blocks virtual parent traversal");
    }
    catch (InvalidOperationException)
    {
        Check(true, "direct provider blocks virtual parent traversal");
    }

    var cancelExport = Path.Combine(tempRoot, "cancel-export");
    Directory.CreateDirectory(cancelExport);
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try
        {
            await explorer.CopyOutAsync(unicode!.FullPath, cancelExport, cancellationToken: cancelled.Token);
            Check(false, "pre-cancelled direct copy does not start");
        }
        catch (OperationCanceledException)
        {
            Check(true, "pre-cancelled direct copy does not start");
        }
    }
    Check(!Directory.EnumerateFileSystemEntries(cancelExport).Any(), "cancelled direct copy leaves no output");

    Check(!await QueryAttachedAsync(imagePath), "direct list/search/copy operations leave ISO detached");

    var fakePath = Path.Combine(tempRoot, "not-an-iso.iso");
    await File.WriteAllBytesAsync(fakePath, new byte[SectorSizeForProbe]);
    Check(await registry.ResolveAsync(fakePath) is null, "registry rejects an invalid ISO payload");
}
catch (Exception ex)
{
    failures.Add(ex.ToString());
    Console.Error.WriteLine($"FAIL  direct ISO integration: {ex}");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("\nDragon DiskForge direct ISO browse integration tests passed.");

void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    Console.WriteLine($"PASS  {name}");
}

static async Task CreateIsoAsync(string sourcePath, string imagePath)
{
    var scriptPath = Path.Combine(Path.GetDirectoryName(imagePath)!, "create-direct-iso.ps1");
    var sourceLiteral = ToPowerShellLiteral(sourcePath);
    var imageLiteral = ToPowerShellLiteral(imagePath);
    var script = $$"""
        $ErrorActionPreference = 'Stop'
        $source = {{sourceLiteral}}
        $target = {{imageLiteral}}

        $code = @'
        using System;
        using System.IO;
        using System.Runtime.InteropServices.ComTypes;

        public static class DragonDirectIsoWriter
        {
            public unsafe static void Create(string path, object stream, int blockSize, int totalBlocks)
            {
                int bytes = 0;
                byte[] buffer = new byte[blockSize];
                IntPtr pointer = (IntPtr)(&bytes);
                IStream input = stream as IStream;
                if (input == null)
                    throw new InvalidOperationException("IMAPI did not return an IStream.");

                using FileStream output = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
                while (totalBlocks-- > 0)
                {
                    input.Read(buffer, blockSize, pointer);
                    output.Write(buffer, 0, bytes);
                }
                output.Flush();
            }
        }
        '@

        $compiler = New-Object System.CodeDom.Compiler.CompilerParameters
        $compiler.CompilerOptions = '/unsafe'
        Add-Type -CompilerParameters $compiler -TypeDefinition $code

        $image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
        $image.FileSystemsToCreate = 3
        $image.FreeMediaBlocks = 0
        $image.VolumeName = 'DRAGONDIRECT'
        $image.Root.AddTree($source, $false)
        $result = $image.CreateResultImage()
        [DragonDirectIsoWriter]::Create($target, $result.ImageStream, $result.BlockSize, $result.TotalBlocks)
        """;

    await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8);
    var result = await RunProcessAsync(
        "powershell.exe",
        $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"");
    if (result.ExitCode != 0)
        throw new InvalidOperationException($"Windows IMAPI could not create the direct-browse ISO.\n{result.Output}\n{result.Error}");
}

static async Task<bool> QueryAttachedAsync(string imagePath)
{
    var literal = ToPowerShellLiteral(imagePath);
    var script = $"$ErrorActionPreference='Stop'; (Get-DiskImage -ImagePath {literal}).Attached";
    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    var result = await RunProcessAsync(
        "powershell.exe",
        $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}");
    if (result.ExitCode != 0)
        throw new InvalidOperationException($"Could not query ISO attached state. {result.Error}");
    return result.Output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
}

static string ToPowerShellLiteral(string value)
    => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }
    };

    process.Start();
    var outputTask = process.StandardOutput.ReadToEndAsync();
    var errorTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
}

sealed class CaptureProgress : IProgress<double>
{
    public double Last { get; private set; }
    public void Report(double value) => Last = value;
    public void Reset() => Last = 0d;
}

sealed record ProcessResult(int ExitCode, string Output, string Error);
