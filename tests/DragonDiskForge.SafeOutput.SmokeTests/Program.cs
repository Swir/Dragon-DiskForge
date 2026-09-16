using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-SafeOutput-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var service = new SafeOutputService();
    var payload = Encoding.UTF8.GetBytes("dragon-safe-output\n");

    var newPath = Path.Combine(root, "new.img");
    var newResult = await service.WriteAsync(
        newPath,
        (stream, token) => stream.WriteAsync(payload, token).AsTask());
    Require(newResult.DestinationPath == Path.GetFullPath(newPath), "Committed path should be normalized.");
    Require(newResult.SizeBytes == payload.LongLength, "Committed size should match written bytes.");
    Require(!newResult.ReplacedExisting, "A new destination must not report replacement.");
    Require(File.ReadAllBytes(newPath).SequenceEqual(payload), "New output should contain the writer payload.");
    RequireNoTemporaryFiles(root, "Successful output should not leave a temp file.");

    var existingPath = Path.Combine(root, "existing.img");
    await File.WriteAllTextAsync(existingPath, "original");
    var writerCalls = 0;
    await ExpectThrowsAsync<IOException>(
        () => service.WriteAsync(
            existingPath,
            (stream, token) =>
            {
                writerCalls++;
                return Task.CompletedTask;
            }),
        "FailIfExists must reject a pre-existing destination.");
    Require(writerCalls == 0, "FailIfExists should refuse an existing destination before invoking the writer.");
    Require(await File.ReadAllTextAsync(existingPath) == "original", "FailIfExists must preserve the existing destination.");
    RequireNoTemporaryFiles(root, "FailIfExists should not leave a temp file.");

    var replacement = Encoding.UTF8.GetBytes("replacement");
    var replaceResult = await service.WriteAsync(
        existingPath,
        (stream, token) => stream.WriteAsync(replacement, token).AsTask(),
        OutputOverwritePolicy.ReplaceExisting);
    Require(replaceResult.ReplacedExisting, "Replacing an existing destination should be reported.");
    Require(File.ReadAllBytes(existingPath).SequenceEqual(replacement), "ReplaceExisting should atomically promote the new payload.");
    RequireNoTemporaryFiles(root, "ReplaceExisting should not leave a temp file.");

    var failurePath = Path.Combine(root, "writer-failure.img");
    await File.WriteAllTextAsync(failurePath, "keep-me");
    await ExpectThrowsAsync<InvalidOperationException>(
        () => service.WriteAsync(
            failurePath,
            async (stream, token) =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("partial"), token);
                throw new InvalidOperationException("synthetic writer failure");
            },
            OutputOverwritePolicy.ReplaceExisting),
        "Writer failures must propagate.");
    Require(await File.ReadAllTextAsync(failurePath) == "keep-me", "Writer failure must preserve the original destination.");
    RequireNoTemporaryFiles(root, "Writer failure should clean the temporary output.");

    var cancellationPath = Path.Combine(root, "cancelled.img");
    using (var cts = new CancellationTokenSource())
    {
        await ExpectCanceledAsync(
            () => service.WriteAsync(
                cancellationPath,
                async (stream, token) =>
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("partial"), token);
                    cts.Cancel();
                },
                cancellationToken: cts.Token),
            "Cancellation after partial writing must propagate before commit.");
    }
    Require(!File.Exists(cancellationPath), "Cancelled output must not publish a partial destination.");
    RequireNoTemporaryFiles(root, "Cancelled output should clean the temporary output.");

    var preCancelledPath = Path.Combine(root, "pre-cancelled.img");
    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        var called = false;
        await ExpectCanceledAsync(
            () => service.WriteAsync(
                preCancelledPath,
                (stream, token) =>
                {
                    called = true;
                    return Task.CompletedTask;
                },
                cancellationToken: cts.Token),
            "Pre-cancellation must stop before temporary output is created.");
        Require(!called, "Pre-cancellation should stop before invoking the writer.");
    }
    Require(!File.Exists(preCancelledPath), "Pre-cancelled output must not create a destination.");
    RequireNoTemporaryFiles(root, "Pre-cancellation should not leave a temp file.");

    var racePath = Path.Combine(root, "race.img");
    await ExpectThrowsAsync<IOException>(
        () => service.WriteAsync(
            racePath,
            async (stream, token) =>
            {
                await stream.WriteAsync(payload, token);
                await File.WriteAllTextAsync(racePath, "racer", token);
            }),
        "FailIfExists must fail closed if a destination appears before commit.");
    Require(await File.ReadAllTextAsync(racePath) == "racer", "FailIfExists must preserve the racing destination.");
    RequireNoTemporaryFiles(root, "FailIfExists race should clean the temporary output.");

    var replaceRacePath = Path.Combine(root, "replace-race.img");
    var replaceRaceResult = await service.WriteAsync(
        replaceRacePath,
        async (stream, token) =>
        {
            await stream.WriteAsync(replacement, token);
            await File.WriteAllTextAsync(replaceRacePath, "racer", token);
        },
        OutputOverwritePolicy.ReplaceExisting);
    Require(replaceRaceResult.ReplacedExisting, "ReplaceExisting should report replacing a destination that appeared during writing.");
    Require(File.ReadAllBytes(replaceRacePath).SequenceEqual(replacement), "ReplaceExisting should promote the completed temp over a racing destination.");
    RequireNoTemporaryFiles(root, "ReplaceExisting race should not leave a temp file.");

    var missingParentPath = Path.Combine(root, "missing", "output.img");
    await ExpectThrowsAsync<DirectoryNotFoundException>(
        () => service.WriteAsync(missingParentPath, (_, _) => Task.CompletedTask),
        "Missing destination directories must fail explicitly rather than being guessed or created.");

    await ExpectThrowsAsync<ArgumentOutOfRangeException>(
        () => service.WriteAsync(
            Path.Combine(root, "invalid-policy.img"),
            (_, _) => Task.CompletedTask,
            (OutputOverwritePolicy)999),
        "Unknown overwrite policies must fail closed.");

    Console.WriteLine("Dragon DiskForge safe output transaction smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void RequireNoTemporaryFiles(string directory, string message)
{
    if (Directory.EnumerateFiles(directory, "*.dragon-tmp", SearchOption.TopDirectoryOnly).Any())
        throw new InvalidOperationException(message);
}

static async Task ExpectCanceledAsync(Func<Task> action, string message)
{
    try
    {
        await action();
    }
    catch (OperationCanceledException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static async Task ExpectThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
