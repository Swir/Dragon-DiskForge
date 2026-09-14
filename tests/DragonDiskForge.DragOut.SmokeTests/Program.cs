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

var validator = new ExplorerDragOutValidator();
var root = Path.Combine(Path.GetTempPath(), $"dragon-drag-out-{Guid.NewGuid():N}");
var outsideRoot = Path.Combine(Path.GetTempPath(), $"dragon-drag-outside-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
Directory.CreateDirectory(outsideRoot);

try
{
    var file = Path.Combine(root, "dragon.txt");
    var folder = Path.Combine(root, "Folder");
    var outside = Path.Combine(outsideRoot, "outside.txt");
    await File.WriteAllTextAsync(file, "dragon");
    Directory.CreateDirectory(folder);
    await File.WriteAllTextAsync(outside, "outside");

    Check(validator.Validate(root, file, isDirectory: false) == Path.GetFullPath(file),
        "drag-out accepts an existing file inside the mounted root");
    Check(validator.Validate(root, folder, isDirectory: true) == Path.GetFullPath(folder),
        "drag-out accepts an existing folder inside the mounted root");

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
    try { Directory.Delete(root, recursive: true); } catch { }
    try { Directory.Delete(outsideRoot, recursive: true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"\n{failures.Count} drag-out smoke test(s) failed.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("\nDragon DiskForge drag-out smoke tests passed.");
