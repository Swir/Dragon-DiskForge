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

    Check(validator.Validate(root, file, isDirectory: false) == Path.GetFullPath(file),
        "drag-out accepts an existing file inside the mounted root");
    Check(validator.Validate(root, folder, isDirectory: true) == Path.GetFullPath(folder),
        "drag-out accepts an existing folder inside the mounted root");
    Check(validator.Validate(root, nestedFile, isDirectory: false) == Path.GetFullPath(nestedFile),
        "drag-out accepts a normal nested file with non-reparse ancestors");

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
    var escapeLink = Path.Combine(root, "escape-link");
    try
    {
        if (Directory.Exists(escapeLink))
            Directory.Delete(escapeLink);
    }
    catch { }

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
