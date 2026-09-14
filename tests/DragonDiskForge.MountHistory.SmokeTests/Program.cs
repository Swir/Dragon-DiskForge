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

var root = Path.Combine(Path.GetTempPath(), $"dragon-mount-history-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    var storagePath = Path.Combine(root, "state", "mount-history.json");
    var imageA = Path.Combine(root, "A.iso");
    var imageB = Path.Combine(root, "B.vhdx");
    var imageC = Path.Combine(root, "C.vhd");
    await File.WriteAllBytesAsync(imageA, new byte[1]);
    await File.WriteAllBytesAsync(imageB, new byte[1]);
    await File.WriteAllBytesAsync(imageC, new byte[1]);

    using (var history = new JsonMountHistoryService(storagePath, maxEntries: 2))
    {
        await history.RecordMountedAsync(imageA, "D:");
        await Task.Delay(5);
        await history.RecordMountedAsync(imageB, "E:");

        var snapshot = await history.GetAsync();
        Check(snapshot.Count == 2, "mount history stores observed mounts");
        Check(snapshot[0].Path == Path.GetFullPath(imageB), "mount history is newest-first");
        Check(snapshot[0].LastTargetDisplay == "E:", "mount history preserves the last target display");

        await Task.Delay(5);
        var deduped = await history.RecordMountedAsync(imageA, "F:");
        Check(deduped.Count == 2, "repeated mount does not duplicate a path");
        Check(deduped[0].Path == Path.GetFullPath(imageA) && deduped[0].LastTargetDisplay == "F:",
            "repeated mount refreshes the existing history entry");

        await Task.Delay(5);
        var pruned = await history.RecordMountedAsync(imageC, null);
        Check(pruned.Count == 2, "mount history enforces its configured limit");
        Check(!pruned.Any(x => x.Path == Path.GetFullPath(imageB)), "oldest history entry is pruned first");
    }

    using (var reloaded = new JsonMountHistoryService(storagePath, maxEntries: 2))
    {
        var persisted = await reloaded.GetAsync();
        Check(persisted.Count == 2, "mount history survives process-style JSON reload");
        Check(persisted.Any(x => x.Path == Path.GetFullPath(imageA)), "reloaded history preserves paths");

        var removed = await reloaded.RemoveAsync(imageA);
        Check(!removed.Any(x => x.Path == Path.GetFullPath(imageA)), "mount history removes one entry cleanly");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await reloaded.GetAsync(cancelled.Token);
            Check(false, "mount history honors cancellation");
        }
        catch (OperationCanceledException)
        {
            Check(true, "mount history honors cancellation");
        }

        await reloaded.ClearAsync();
        Check((await reloaded.GetAsync()).Count == 0, "mount history clears persisted entries");
        Check(!File.Exists(storagePath), "clear removes the persisted history file");
    }
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"\n{failures.Count} mount-history smoke test(s) failed.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("\nDragon DiskForge mount-history smoke tests passed.");
