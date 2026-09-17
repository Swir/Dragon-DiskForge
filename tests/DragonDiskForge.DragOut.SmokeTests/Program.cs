using System.Diagnostics;
using DragonDiskForge.Core.Services;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS  {name}");
        return;
    }

    failures.Add(name);
    Console.Error.WriteLine($"FAIL  {name}");
}

async Task CreateDirectoryLinkAsync(string linkPath, string targetPath)
{
    if (!OperatingSystem.IsWindows())
    {
        Directory.CreateSymbolicLink(linkPath, targetPath);
        return;
    }

    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        },
    };

    process.StartInfo.ArgumentList.Add("/d");
    process.StartInfo.ArgumentList.Add("/c");
    process.StartInfo.ArgumentList.Add("mklink");
    process.StartInfo.ArgumentList.Add("/J");
    process.StartInfo.ArgumentList.Add(linkPath);
    process.StartInfo.ArgumentList.Add(targetPath);

    if (!process.Start())
        throw new InvalidOperationException("Could not start cmd.exe to create the Explorer junction fixture.");

    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var stdout = await stdoutTask;
    var stderr = await stderrTask;

    if (process.ExitCode != 0 || !Directory.Exists(linkPath))
    {
        throw new InvalidOperationException(
            $"Could not create Explorer junction fixture (exit {process.ExitCode}). stdout={stdout} stderr={stderr}");
    }
}

var validator = new ExplorerDragOutValidator();
var explorer = new MountedFileSystemExplorerService();
var root = Path.Combine(Path.GetTempPath(), $"dragon-drag-out-{Guid.NewGuid():N}");
var outsideRoot = Path.Combine(Path.GetTempPath(), $"dragon-drag-outside-{Guid.NewGuid():N}");
var exportRoot = Path.Combine(Path.GetTempPath(), $"dragon-drag-export-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
Directory.CreateDirectory(outsideRoot);
Directory.CreateDirectory(exportRoot);

try
{
    var file = Path.Combine(root, "dragon.txt");
    var folder = Path.Combine(root, "Folder");
    var nestedFolder = Path.Combine(folder, "Nested");
    var nestedFile = Path.Combine(nestedFolder, "safe.txt");
    var outside = Path.Combine(outsideRoot, "outside.txt");
    await File.WriteAllTextAsync(file, "dragon");
    Directory.CreateDirectory(nestedFolder);
    await File.WriteAllTextAsync(nestedFile, "safe");
    await File.WriteAllTextAsync(outside, "outside");

    var validatedFile = validator.Validate(root, file, isDirectory: false);
    Check(validatedFile == Path.GetFullPath(file),
        "drag-out accepts an existing file inside the mounted root");
    Check(validator.Validate(root, folder, isDirectory: true) == Path.GetFullPath(folder),
        "drag-out accepts an existing folder inside the mounted root");
    Check(validator.Validate(root, nestedFile, isDirectory: false) == Path.GetFullPath(nestedFile),
        "drag-out accepts a normal nested file with non-reparse ancestors");

    Check(validator.ValidateResolvedStorageItem(
            root,
            validatedFile,
            file,
            expectedIsDirectory: false,
            resolvedIsDirectory: false) == Path.GetFullPath(file),
        "post-resolution drag-out revalidation accepts the same file and shape");

    try
    {
        validator.ValidateResolvedStorageItem(
            root,
            validatedFile,
            nestedFile,
            expectedIsDirectory: false,
            resolvedIsDirectory: false);
        Check(false, "post-resolution drag-out rejects a different resolved path");
    }
    catch (InvalidOperationException)
    {
        Check(true, "post-resolution drag-out rejects a different resolved path");
    }

    try
    {
        validator.ValidateResolvedStorageItem(
            root,
            folder,
            folder,
            expectedIsDirectory: true,
            resolvedIsDirectory: false);
        Check(false, "post-resolution drag-out rejects a changed file/directory shape");
    }
    catch (InvalidOperationException)
    {
        Check(true, "post-resolution drag-out rejects a changed file/directory shape");
    }

    var staleAfterResolution = Path.Combine(root, "stale-after-resolution.bin");
    await File.WriteAllTextAsync(staleAfterResolution, "stale");
    var validatedStalePath = validator.Validate(root, staleAfterResolution, isDirectory: false);
    File.Delete(staleAfterResolution);
    try
    {
        validator.ValidateResolvedStorageItem(
            root,
            validatedStalePath,
            staleAfterResolution,
            expectedIsDirectory: false,
            resolvedIsDirectory: false);
        Check(false, "post-resolution drag-out blocks a source removed after initial validation");
    }
    catch (FileNotFoundException)
    {
        Check(true, "post-resolution drag-out blocks a source removed after initial validation");
    }

    try
    {
        validator.Validate(root, outside, isDirectory: false);
        Check(false, "drag-out blocks path escape outside the mounted root");
    }
    catch (InvalidOperationException)
    {
        Check(true, "drag-out blocks path escape outside the mounted root");
    }

    try
    {
        validator.Validate(root, file, isDirectory: false, declaredReparsePoint: true);
        Check(false, "drag-out blocks a listed reparse point before transfer");
    }
    catch (InvalidOperationException)
    {
        Check(true, "drag-out blocks a listed reparse point before transfer");
    }

    var swapFolder = Path.Combine(root, "swap-folder");
    Directory.CreateDirectory(swapFolder);
    var validatedSwapFolder = validator.Validate(root, swapFolder, isDirectory: true);
    Directory.Delete(swapFolder);
    await CreateDirectoryLinkAsync(swapFolder, outsideRoot);
    try
    {
        validator.ValidateResolvedStorageItem(
            root,
            validatedSwapFolder,
            swapFolder,
            expectedIsDirectory: true,
            resolvedIsDirectory: true);
        Check(false, "post-resolution drag-out blocks a source replaced by a reparse point");
    }
    catch (InvalidOperationException)
    {
        Check(true, "post-resolution drag-out blocks a source replaced by a reparse point");
    }

    var escapeLink = Path.Combine(root, "escape-link");
    await CreateDirectoryLinkAsync(escapeLink, outsideRoot);

    try
    {
        validator.Validate(root, escapeLink, isDirectory: true);
        Check(false, "drag-out blocks an actual reparse-point directory");
    }
    catch (InvalidOperationException)
    {
        Check(true, "drag-out blocks an actual reparse-point directory");
    }

    var escapedThroughAncestor = Path.Combine(escapeLink, "outside.txt");
    Check(File.Exists(escapedThroughAncestor),
        "reparse-ancestor fixture resolves to an existing outside file");
    try
    {
        validator.Validate(root, escapedThroughAncestor, isDirectory: false);
        Check(false, "drag-out blocks a lexically in-root path that traverses a reparse ancestor");
    }
    catch (InvalidOperationException)
    {
        Check(true, "drag-out blocks a lexically in-root path that traverses a reparse ancestor");
    }

    try
    {
        await explorer.ListAsync(root, escapeLink);
        Check(false, "Explorer browse blocks a directory reached through a reparse ancestor");
    }
    catch (InvalidOperationException)
    {
        Check(true, "Explorer browse blocks a directory reached through a reparse ancestor");
    }

    try
    {
        await explorer.SearchAsync(root, escapeLink, "outside");
        Check(false, "Explorer search blocks a start path reached through a reparse ancestor");
    }
    catch (InvalidOperationException)
    {
        Check(true, "Explorer search blocks a start path reached through a reparse ancestor");
    }

    try
    {
        await explorer.CopyOutAsync(root, escapedThroughAncestor, exportRoot);
        Check(false, "Explorer copy-out blocks a lexically in-root file reached through a reparse ancestor");
    }
    catch (InvalidOperationException)
    {
        Check(true, "Explorer copy-out blocks a lexically in-root file reached through a reparse ancestor");
    }

    Check(!File.Exists(Path.Combine(exportRoot, "outside.txt")),
        "blocked reparse-ancestor copy-out creates no destination file");

    await explorer.CopyOutAsync(root, file, exportRoot);
    var copiedFile = Path.Combine(exportRoot, "dragon.txt");
    Check(File.Exists(copiedFile) && await File.ReadAllTextAsync(copiedFile) == "dragon",
        "Explorer copy-out transaction commits a complete single file");

    await explorer.CopyOutAsync(root, folder, exportRoot);
    var copiedNestedFile = Path.Combine(exportRoot, "Folder", "Nested", "safe.txt");
    Check(File.Exists(copiedNestedFile) && await File.ReadAllTextAsync(copiedNestedFile) == "safe",
        "Explorer directory copy-out commits only after the staged tree is complete");

    var cancelFile = Path.Combine(root, "cancel-file.bin");
    await File.WriteAllBytesAsync(cancelFile, new byte[1024 * 1024]);
    using (var cts = new CancellationTokenSource())
    {
        try
        {
            await explorer.CopyOutAsync(
                root,
                cancelFile,
                exportRoot,
                new CancelOnFirstProgress(cts),
                cts.Token);
            Check(false, "cancelled single-file copy-out reports cancellation");
        }
        catch (OperationCanceledException)
        {
            Check(true, "cancelled single-file copy-out reports cancellation");
        }
    }

    Check(!File.Exists(Path.Combine(exportRoot, "cancel-file.bin")),
        "cancelled single-file copy-out publishes no partial destination");
    Check(!Directory.EnumerateFiles(exportRoot, "*.dragon-tmp", SearchOption.AllDirectories).Any(),
        "cancelled single-file copy-out removes transactional temp files");

    var cancelFolder = Path.Combine(root, "CancelFolder");
    Directory.CreateDirectory(cancelFolder);
    await File.WriteAllBytesAsync(Path.Combine(cancelFolder, "large.bin"), new byte[1024 * 1024]);
    using (var cts = new CancellationTokenSource())
    {
        try
        {
            await explorer.CopyOutAsync(
                root,
                cancelFolder,
                exportRoot,
                new CancelOnFirstProgress(cts),
                cts.Token);
            Check(false, "cancelled directory copy-out reports cancellation");
        }
        catch (OperationCanceledException)
        {
            Check(true, "cancelled directory copy-out reports cancellation");
        }
    }

    Check(!Directory.Exists(Path.Combine(exportRoot, "CancelFolder")),
        "cancelled directory copy-out publishes no partial destination tree");
    Check(!Directory.EnumerateDirectories(exportRoot, "*.dragon-copy-tmp", SearchOption.TopDirectoryOnly).Any(),
        "cancelled directory copy-out removes its Dragon-owned staging tree");
    Check(!Directory.EnumerateFiles(exportRoot, "*.dragon-tmp", SearchOption.AllDirectories).Any(),
        "cancelled directory copy-out leaves no transactional temp file");

    var missing = Path.Combine(root, "missing.bin");
    try
    {
        validator.Validate(root, missing, isDirectory: false);
        Check(false, "drag-out blocks stale missing files");
    }
    catch (FileNotFoundException)
    {
        Check(true, "drag-out blocks stale missing files");
    }
}
finally
{
    foreach (var linkName in new[] { "escape-link", "swap-folder" })
    {
        var linkPath = Path.Combine(root, linkName);
        try
        {
            if (Directory.Exists(linkPath))
                Directory.Delete(linkPath);
        }
        catch { }
    }

    try { Directory.Delete(root, recursive: true); } catch { }
    try { Directory.Delete(outsideRoot, recursive: true); } catch { }
    try { Directory.Delete(exportRoot, recursive: true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"\n{failures.Count} Explorer safety smoke test(s) failed.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("\nDragon DiskForge Explorer safety smoke tests passed.");

sealed class CancelOnFirstProgress : IProgress<double>
{
    private readonly CancellationTokenSource _cancellation;
    private int _cancelled;

    public CancelOnFirstProgress(CancellationTokenSource cancellation)
        => _cancellation = cancellation;

    public void Report(double value)
    {
        if (value > 0d && Interlocked.Exchange(ref _cancelled, 1) == 0)
            _cancellation.Cancel();
    }
}
