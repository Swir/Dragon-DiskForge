using System.Buffers.Binary;
using DragonDiskForge.Core.Services;

const int ClusterBits = 12;
const int ClusterSize = 1 << ClusterBits;
const ulong CopiedBit = 1UL << 63;
const ulong CompressedBit = 1UL << 62;
const ulong ZeroBit = 1UL;

var root = Path.Combine(Path.GetTempPath(), "DragonDiskForge-QcowGuest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var valid = Path.Combine(root, "guest-reader.qcow2");
    CreateQcow2(valid, version: 3);

    await using (var reader = await Qcow2GuestByteReader.OpenAsync(valid))
    {
        Expect(reader.Length == 4UL * ClusterSize, "Guest reader should expose the declared QCOW2 virtual size.");

        var first = new byte[64];
        await reader.ReadExactlyAsync(0, first);
        Expect(first.All(value => value == 0xA5), "Allocated QCOW2 cluster bytes should map through L1/L2 tables.");

        var across = new byte[64];
        await reader.ReadExactlyAsync((ulong)ClusterSize - 16, across);
        Expect(across.Take(16).All(value => value == 0xA5), "Read should preserve the tail of an allocated cluster.");
        Expect(across.Skip(16).All(value => value == 0), "QCOW2 v3 zero-cluster flag should produce zeroes across a cluster boundary.");

        var unallocated = new byte[128];
        await reader.ReadExactlyAsync(2UL * ClusterSize, unallocated);
        Expect(unallocated.All(value => value == 0), "Unallocated QCOW2 clusters without a backing file should read as zeroes.");

        await reader.ReadExactlyAsync(reader.Length, Memory<byte>.Empty);
        await ExpectThrowsAsync<ArgumentOutOfRangeException>(
            () => reader.ReadExactlyAsync(reader.Length - 1, new byte[2]).AsTask(),
            "Guest reads extending beyond the virtual size must be rejected.");

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await ExpectCanceledAsync(
            () => reader.ReadExactlyAsync(0, new byte[1], cts.Token).AsTask(),
            "Pre-cancelled guest reads should propagate cancellation.");
    }

    var backing = Copy(valid, root, "backing.qcow2");
    PatchU64(backing, 8, 104);
    PatchU32(backing, 16, 8);
    PatchBytes(backing, 104, "base.raw"u8.ToArray());
    await ExpectThrowsAsync<NotSupportedException>(
        () => OpenAndDisposeAsync(backing),
        "Backing-file QCOW2 images must remain outside the first guest-reader slice.");

    var encrypted = Copy(valid, root, "encrypted.qcow2");
    PatchU32(encrypted, 32, 1);
    await ExpectThrowsAsync<NotSupportedException>(
        () => OpenAndDisposeAsync(encrypted),
        "Encrypted QCOW2 guest data must be refused.");

    var dirty = Copy(valid, root, "dirty.qcow2");
    PatchU64(dirty, 72, 1);
    await ExpectThrowsAsync<InvalidDataException>(
        () => OpenAndDisposeAsync(dirty),
        "Dirty QCOW2 images must be refused by guest-byte translation.");

    var compressed = Copy(valid, root, "compressed-entry.qcow2");
    PatchU64(compressed, 3L * ClusterSize, CompressedBit | 512UL);
    await using (var reader = await Qcow2GuestByteReader.OpenAsync(compressed))
    {
        await ExpectThrowsAsync<NotSupportedException>(
            () => reader.ReadExactlyAsync(0, new byte[1]).AsTask(),
            "Compressed QCOW2 L2 entries must not be approximated as normal clusters.");
    }

    var reservedL1 = Copy(valid, root, "reserved-l1.qcow2");
    PatchU64(reservedL1, ClusterSize, CopiedBit | (1UL << 56) | 3UL * ClusterSize);
    await using (var reader = await Qcow2GuestByteReader.OpenAsync(reservedL1))
    {
        await ExpectThrowsAsync<InvalidDataException>(
            () => reader.ReadExactlyAsync(0, new byte[1]).AsTask(),
            "Reserved L1 entry bits must be rejected before following the mapping.");
    }

    var outOfRange = Copy(valid, root, "oob-data.qcow2");
    PatchU64(outOfRange, 3L * ClusterSize, CopiedBit | 6UL * ClusterSize);
    await using (var reader = await Qcow2GuestByteReader.OpenAsync(outOfRange))
    {
        await ExpectThrowsAsync<InvalidDataException>(
            () => reader.ReadExactlyAsync(0, new byte[1]).AsTask(),
            "Mapped data clusters extending outside the physical QCOW2 file must be rejected.");
    }

    var v2 = Path.Combine(root, "v2-zero-flag.qcow2");
    CreateQcow2(v2, version: 2);
    await using (var reader = await Qcow2GuestByteReader.OpenAsync(v2))
    {
        await ExpectThrowsAsync<InvalidDataException>(
            () => reader.ReadExactlyAsync((ulong)ClusterSize, new byte[1]).AsTask(),
            "QCOW2 v2 must reject the v3-only zero-cluster flag.");
    }

    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        await ExpectCanceledAsync(
            () => OpenAndDisposeAsync(valid, cts.Token),
            "Pre-cancelled QCOW2 guest-reader open should propagate cancellation.");
    }

    Console.WriteLine("Dragon DiskForge QCOW2 guest-byte reader smoke tests passed.");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

static void CreateQcow2(string path, uint version)
{
    const int fileClusters = 6;
    var bytes = new byte[fileClusters * ClusterSize];

    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x514649FB);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), version);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), ClusterBits);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(24, 8), 4UL * ClusterSize);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(32, 4), 0);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(36, 4), 1);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(40, 8), ClusterSize);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(48, 8), 2UL * ClusterSize);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(56, 4), 1);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(60, 4), 0);
    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(64, 8), 0);

    if (version == 3)
    {
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(72, 8), 0);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(80, 8), 0);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(88, 8), 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(96, 4), 4);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(100, 4), 104);
    }

    // Active L1 table (cluster 1) -> L2 table (cluster 3).
    BinaryPrimitives.WriteUInt64BigEndian(
        bytes.AsSpan(ClusterSize, 8),
        CopiedBit | 3UL * ClusterSize);

    // L2[0] -> allocated data cluster 4.
    BinaryPrimitives.WriteUInt64BigEndian(
        bytes.AsSpan(3 * ClusterSize, 8),
        CopiedBit | 4UL * ClusterSize);

    // L2[1] -> explicit zero cluster (valid only for v3, deliberately invalid for v2 test).
    BinaryPrimitives.WriteUInt64BigEndian(
        bytes.AsSpan(3 * ClusterSize + 8, 8),
        ZeroBit);

    // L2[2] remains zero/unallocated. No backing file means logical zeroes.
    bytes.AsSpan(4 * ClusterSize, ClusterSize).Fill(0xA5);

    File.WriteAllBytes(path, bytes);
}

static async Task OpenAndDisposeAsync(string path, CancellationToken cancellationToken = default)
{
    await using var reader = await Qcow2GuestByteReader.OpenAsync(path, cancellationToken);
}

static string Copy(string source, string root, string name)
{
    var target = Path.Combine(root, name);
    File.Copy(source, target);
    return target;
}

static void PatchU32(string path, long offset, uint value)
{
    var buffer = new byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    PatchBytes(path, offset, buffer);
}

static void PatchU64(string path, long offset, ulong value)
{
    var buffer = new byte[8];
    BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
    PatchBytes(path, offset, buffer);
}

static void PatchBytes(string path, long offset, byte[] value)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
    stream.Position = offset;
    stream.Write(value);
}

static void Expect(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task ExpectThrowsAsync<T>(Func<Task> action, string message) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }

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
