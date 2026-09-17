using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

namespace DragonDiskForge.Windows.Services;

/// <summary>
/// Read-only verifier used by the disposable-media validation harness. It hashes exactly the
/// requested physical-device prefix after revalidating destination identity and capacity.
/// </summary>
public sealed class WindowsPhysicalMediaReadbackVerifier
{
    private const int BufferSizeBytes = 1024 * 1024;
    private readonly IPhysicalDiskInventoryService _inventory;

    public WindowsPhysicalMediaReadbackVerifier(IPhysicalDiskInventoryService? inventory = null)
    {
        _inventory = inventory ?? new WindowsPhysicalDiskInventoryService();
    }

    public async Task<string> ComputePrefixSha256Async(
        PhysicalDiskInfo expectedDestination,
        long lengthBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedDestination);
        if (lengthBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(lengthBytes), "Read-back length must be greater than zero.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows physical-media read-back verification is implemented only for Windows.");

        var disks = await _inventory.GetDisksAsync(cancellationToken).ConfigureAwait(false);
        var current = disks.FirstOrDefault(x => x.DiskNumber == expectedDestination.DiskNumber)
            ?? throw new IOException("Destination physical disk disappeared before read-back verification.");

        if (!SameIdentity(expectedDestination, current))
            throw new IOException("Destination identity changed before read-back verification.");

        if (current.CapacityBytes is null or <= 0 || lengthBytes > current.CapacityBytes.Value)
            throw new IOException("Requested read-back range exceeds the proven destination capacity.");

        using var handle = WindowsPhysicalMediaInterop.OpenDevice(
            current.DevicePath,
            WindowsPhysicalMediaInterop.GenericRead,
            FileShare.ReadWrite | FileShare.Delete);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open {current.DevicePath} for read-back verification.");

        // The raw device handle is synchronous by design. Async stream methods still keep the
        // calling pipeline responsive without falsely assuming cancellable overlapped device I/O.
        await using var stream = new FileStream(
            handle,
            FileAccess.Read,
            bufferSize: BufferSizeBytes,
            isAsync: false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = GC.AllocateUninitializedArray<byte>(BufferSizeBytes);
        long remaining = lengthBytes;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Physical device ended before the requested read-back range was verified.");

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool SameIdentity(PhysicalDiskInfo left, PhysicalDiskInfo right)
        => left.DiskNumber == right.DiskNumber
            && string.Equals(left.DevicePath, right.DevicePath, StringComparison.OrdinalIgnoreCase)
            && left.HasStableIdentity
            && right.HasStableIdentity
            && !string.IsNullOrWhiteSpace(left.StableId)
            && string.Equals(left.StableId, right.StableId, StringComparison.Ordinal)
            && left.CapacityBytes == right.CapacityBytes;
}
