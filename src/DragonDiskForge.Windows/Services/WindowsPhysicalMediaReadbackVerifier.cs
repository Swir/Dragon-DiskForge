using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace DragonDiskForge.Windows.Services;

/// <summary>
/// Read-only verifier used by the disposable-media validation harness. It hashes exactly the
/// requested physical-device prefix after revalidating destination identity and capacity.
/// </summary>
public sealed class WindowsPhysicalMediaReadbackVerifier
{
    private const int BufferSizeBytes = 1024 * 1024;
    private const uint IoctlDiskGetDriveGeometry = 0x00070000;
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

        // Query the sector geometry on the SAME opened read handle. Raw disk requests,
        // including the final tail, must use whole logical sectors. Only requested
        // prefix bytes are hashed; any rounding bytes are read but never hashed.
        var sectorBytes = GetLogicalSectorSize(handle);
        if (BufferSizeBytes % sectorBytes != 0)
            throw new IOException("Read-back buffer size is not a whole logical sector multiple.");
        var remainder = lengthBytes % sectorBytes;
        var tailPaddingBytes = remainder == 0 ? 0 : sectorBytes - remainder;
        if (tailPaddingBytes > current.CapacityBytes.Value - lengthBytes)
            throw new IOException("A sector-aligned read-back would exceed the proven destination capacity.");
        var alignedReadBytes = checked(lengthBytes + tailPaddingBytes);

        // The raw device handle is synchronous by design. Async stream methods still keep the
        // calling pipeline responsive without falsely assuming cancellable overlapped device I/O.
        await using var stream = new FileStream(
            handle,
            FileAccess.Read,
            bufferSize: BufferSizeBytes,
            isAsync: false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = GC.AllocateUninitializedArray<byte>(BufferSizeBytes);
        long remainingRaw = alignedReadBytes;
        long remainingToHash = lengthBytes;

        while (remainingRaw > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, remainingRaw);
            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (read != requested)
                throw new EndOfStreamException("Short raw-device read would make read-back sector alignment uncertain.");

            var toHash = (int)Math.Min(read, remainingToHash);
            hash.AppendData(buffer, 0, toHash);
            remainingToHash -= toHash;
            remainingRaw -= read;
        }
        if (remainingToHash != 0)
            throw new IOException("Read-back did not cover the full requested prefix.");

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static int GetLogicalSectorSize(SafeFileHandle handle)
    {
        var output = new byte[24];
        if (!DeviceIoControl(
                handle, IoctlDiskGetDriveGeometry, null, 0, output, output.Length,
                out var returned, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Could not query sector geometry on the opened physical read-back handle.");
        }
        if (returned < 24)
            throw new IOException("Windows returned a truncated physical read-back geometry response.");
        var sectorBytes = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(20, 4));
        if (sectorBytes is < 512 or > BufferSizeBytes || (sectorBytes & (sectorBytes - 1)) != 0)
            throw new IOException($"Invalid logical sector size on read-back device: {sectorBytes} bytes.");
        return checked((int)sectorBytes);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, byte[]? lpInBuffer,
        int nInBufferSize, [Out] byte[] lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

    private static bool SameIdentity(PhysicalDiskInfo left, PhysicalDiskInfo right)
        => left.DiskNumber == right.DiskNumber
            && string.Equals(left.DevicePath, right.DevicePath, StringComparison.OrdinalIgnoreCase)
            && left.HasStableIdentity
            && right.HasStableIdentity
            && !string.IsNullOrWhiteSpace(left.StableId)
            && string.Equals(left.StableId, right.StableId, StringComparison.Ordinal)
            && left.CapacityBytes == right.CapacityBytes;
}
