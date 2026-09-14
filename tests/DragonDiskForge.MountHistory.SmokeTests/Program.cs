using DragonDiskForge.Core.Models;
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

var root = Path.Combine(Path.GetTempPath(), $"dragon-diskforge-history-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
var historyPath = Path.Combine(root, "state", "mount-history.json");
var imageA = Path.Combine(root, "A.iso");
var imageB = Path.Combine(root, "B.vhdx");
var imageC = Path.Combine(root, "C.iso");

try
{
    using (var history = new JsonMountHistoryService(historyPath, maxEntries: 3))
    {
        var first = await history.RecordAsync(imageA, MountHistoryAction.Mounted, "E:\\");
        Check(first.Count == 1, "Mount history records the first event");
        Check(first[0].ImagePath == Path.GetFullPath(imageA), "Mount history normalizes image paths");
        Check(first[0].Action == MountHistoryAction.Mounted, "Mount history preserves the event action");
        Check(first[0].TargetDisplay == "E:\\", "Mount history preserves the mount target");

        await Task.Delay(5);
        await history.RecordAsync(imageA, MountHistoryAction.Unmounted, "E:\\");
        await Task.Delay(5);
        await history.RecordAsync(imageB, MountHistoryAction.Mounted, "F:\\");
        await Task.Delay(5);
        var pruned = await history.RecordAsync(imageC, MountHistoryAction.Mounted, "G:\\");

        Check(pruned.Count == 3, "Mount history enforces the configured maximum entry count");
        Check(pruned[0].ImagePath == Path.GetFullPath(imageC), "Mount history returns newest events first");
        Check(pruned.Any(x => x.ImagePath == Path.GetFullPath(imageA) && x.Action == MountHistoryAction.Unmounted), "Mount history keeps distinct mount and unmount events");
        Check(!pruned.Any(x => x.ImagePath == Path.GetFullPath(imageA) && x.Action == MountHistoryAction.Mounted), "Mount history prunes the oldest event first");
    }

    using (var reloaded = new JsonMountHistoryService(historyPath, maxEntries: 3))
    {
        var persisted = await reloaded.GetAsync();
        Check(persisted.Count == 3, "Mount history survives process-style JSON reload");
        Check(persisted[0].ImagePath == Path.GetFullPath(imageC), "Reloaded mount history preserves newest-first ordering");

        try
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await reloaded.RecordAsync(imageA, MountHistoryAction.Mounted, cancellationToken: cancelled.Token);
            Check(false, "Mount history honors cancellation");
        }
        catch (OperationCanceledException)
        {
            Check(true, "Mount history honors cancellation");
        }

        await reloaded.ClearAsync();
        Check((await reloaded.GetAsync()).Count == 0, "Mount history clears persisted events");
    }
}
finally
{
    try
    {
        Directory.Delete(root, recursive: true);
    }
    catch
    {
        // Best-effort smoke-test cleanup.
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"\n{failures.Count} mount-history smoke test(s) failed.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("\nDragon DiskForge mount-history smoke tests passed.");
