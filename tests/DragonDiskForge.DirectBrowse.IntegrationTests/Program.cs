using System.Diagnostics;
using System.Text;
using DragonDiskForge.Core.Providers;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("SKIP  ISO direct-browse integration requires Windows IMAPI.");
    return;
}

var tempRoot = Path.Combine(Path.GetTempPath(), $"dragon-direct-browse-{Guid.NewGuid():N}");
var sourceRoot = Path.Combine(tempRoot, "source");
var nestedRoot = Path.Combine(sourceRoot, "Nested");
var exportRoot = Path.Combine(tempRoot, "export");
var folderExportRoot = Path.Combine(tempRoot, "folder-export");
Directory.CreateDirectory(nestedRoot);
Directory.CreateDirectory(exportRoot);
Directory.CreateDirectory(folderExportRoot);

var markerName = "dragon-direct.txt";
var markerContent = "Dragon DiskForge direct ISO browsing works without mounting.";
var nestedName = "nested.txt";
var nestedContent = "Nested provider-backed content.";
var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
await File.WriteAllTextAsync(Path.Combine(sourceRoot, markerName), markerContent, utf8NoBom);
await File.WriteAllTextAsync(Path.Combine(nestedRoot, nestedName), nestedContent, utf8NoBom);

var imagePath = Path.Combine(tempRoot, "direct-browse.iso");
var provider = new Iso9660DirectBrowseProvider();

try
{
    Console.WriteLine("INFO  Creating disposable ISO with Windows IMAPI2FS...");
    await CreateIsoAsync(sourceRoot, imagePath);
    Check(File.Exists(imagePath) && new FileInfo(imagePath).Length > 0, "IMAPI produced a non-empty direct-browse ISO");
    Check(!await IsAttachedAsync(imagePath), "direct-browse test ISO starts detached");

    Check(await provider.CanHandleAsync(imagePath), "ISO9660 direct provider recognizes a real IMAPI image");
    var info = await provider.InspectAsync(imagePath);
    Check(info.CanExplore && info.Format == "ISO", "direct provider advertises real ISO exploration capability");

    var rootEntries = await provider.ListAsync(imagePath, "/");
    var marker = rootEntries.FirstOrDefault(x => !x.IsDirectory && x.Name.Equals(markerName, StringComparison.OrdinalIgnoreCase));
    var nested = rootEntries.FirstOrDefault(x => x.IsDirectory && x.Name.Equals("Nested", StringComparison.OrdinalIgnoreCase));
    Check(marker is not null, "direct provider lists a root file without mounting");
    Check(nested is not null, "direct provider lists a root directory without mounting");
    Check(marker?.SizeBytes == utf8NoBom.GetByteCount(markerContent), "direct provider reports ISO file size metadata");

    var nestedEntries = await provider.ListAsync(imagePath, "/Nested");
    Check(nestedEntries.Any(x => !x.IsDirectory && x.Name.Equals(nestedName, StringComparison.OrdinalIgnoreCase)),
        "direct provider navigates a nested ISO directory");

    var search = await provider.SearchAsync(imagePath, "/", "nested");
    Check(search.Any(x => x.Name.Equals(nestedName, StringComparison.OrdinalIgnoreCase)),
        "direct provider recursively searches ISO metadata");

    var fileProgress = new CaptureProgress();
    await provider.CopyOutAsync(imagePath, "/" + markerName, exportRoot, fileProgress);
    var copiedMarker = Path.Combine(exportRoot, markerName);
    Check(File.Exists(copiedMarker), "direct provider copies a file out without mounting");
    Check(await File.ReadAllTextAsync(copiedMarker, utf8NoBom) == markerContent,
        "direct file copy preserves exact content");
    Check(fileProgress.Last >= 0.999d, "direct file copy reports completion");

    var folderProgress = new CaptureProgress();
    await provider.CopyOutAsync(imagePath, "/Nested", folderExportRoot, folderProgress);
    var copiedNested = Path.Combine(folderExportRoot, "Nested", nestedName);
    Check(File.Exists(copiedNested), "direct provider copies a directory tree out without mounting");
    Check(await File.ReadAllTextAsync(copiedNested, utf8NoBom) == nestedContent,
        "direct directory copy preserves nested content");
    Check(folderProgress.Last >= 0.999d, "direct directory copy reports completion");

    try
    {
        await provider.CopyOutAsync(imagePath, "/" + markerName, exportRoot);
        throw new InvalidOperationException("direct provider unexpectedly overwrote an existing destination");
    }
    catch (IOException)
    {
        Check(true, "direct provider refuses silent overwrite conflicts");
    }

    try
    {
        await provider.ListAsync(imagePath, "/../escape");
        throw new InvalidOperationException("direct provider unexpectedly accepted path traversal");
    }
    catch (InvalidOperationException)
    {
        Check(true, "direct provider blocks virtual path traversal");
    }

    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try
        {
            await provider.SearchAsync(imagePath, "/", "dragon", cancellationToken: cancelled.Token);
            throw new InvalidOperationException("pre-cancelled direct search unexpectedly completed");
        }
        catch (OperationCanceledException)
        {
            Check(true, "direct provider honors pre-cancelled search");
        }
    }

    var invalidPath = Path.Combine(tempRoot, "invalid.iso");
    await File.WriteAllBytesAsync(invalidPath, new byte[64 * 1024]);
    Check(!await provider.CanHandleAsync(invalidPath), "direct provider rejects a fake .iso extension");

    Check(!await IsAttachedAsync(imagePath), "provider-backed browsing leaves the ISO detached after list/search/copy");
    Console.WriteLine("\nDragon DiskForge ISO direct-browse integration tests passed.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL  ISO direct-browse integration test: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
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

static async Task<bool> IsAttachedAsync(string imagePath)
{
    var literal = ToPowerShellLiteral(imagePath);
    var script = $"$ErrorActionPreference='Stop'; (Get-DiskImage -ImagePath {literal}).Attached";
    var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    var result = await RunProcessAsync(
        "powershell.exe",
        $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}");
    if (result.ExitCode != 0)
        throw new InvalidOperationException($"Could not query ISO attachment state. {result.Error}");
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

static void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    Console.WriteLine($"PASS  {name}");
}

sealed class CaptureProgress : IProgress<double>
{
    public double Last { get; private set; }
    public void Report(double value) => Last = value;
}

sealed record ProcessResult(int ExitCode, string Output, string Error);
