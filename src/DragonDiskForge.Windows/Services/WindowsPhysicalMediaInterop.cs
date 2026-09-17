using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DragonDiskForge.Windows.Services;

internal static class WindowsPhysicalMediaInterop
{
    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileFlagWriteThrough = 0x80000000;

    private const uint IoctlDiskGetDriveGeometry = 0x00070000;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlUnlockVolume = 0x0009001C;
    private const uint FsctlDismountVolume = 0x00090020;
    private const int ErrorNoMoreFiles = 18;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    internal static SafeFileHandle OpenDevice(
        string devicePath,
        uint desiredAccess,
        FileShare shareMode,
        uint flagsAndAttributes = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

        return CreateFileW(
            devicePath,
            desiredAccess,
            shareMode,
            IntPtr.Zero,
            FileMode.Open,
            flagsAndAttributes,
            IntPtr.Zero);
    }

    internal static int GetLogicalSectorSize(string devicePath)
    {
        using var handle = OpenDevice(
            devicePath,
            desiredAccess: 0,
            FileShare.ReadWrite | FileShare.Delete);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open {devicePath} for sector-size query.");

        var output = new byte[24];
        if (!DeviceIoControl(
                handle,
                IoctlDiskGetDriveGeometry,
                null,
                0,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero)
            || returned < 24)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not query logical sector size for {devicePath}.");
        }

        var bytesPerSector = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(20, 4));
        if (bytesPerSector is < 512 or > 1024 * 1024 || (bytesPerSector & (bytesPerSector - 1)) != 0)
            throw new InvalidDataException($"Windows reported an unsupported logical sector size: {bytesPerSector} bytes.");

        return checked((int)bytesPerSector);
    }

    internal static IReadOnlyList<int> GetBackingDiskNumbersForPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        var mountPoint = new StringBuilder(1024);
        if (!GetVolumePathNameW(fullPath, mountPoint, mountPoint.Capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not resolve the source volume for {fullPath}.");

        var volumeName = new StringBuilder(1024);
        if (!GetVolumeNameForVolumeMountPointW(mountPoint.ToString(), volumeName, volumeName.Capacity))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not resolve the source volume identity for {fullPath}.");

        return GetVolumeDiskNumbers(volumeName.ToString());
    }

    internal static IReadOnlyList<SafeFileHandle> LockAndDismountVolumes(int diskNumber)
    {
        if (diskNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(diskNumber));

        var locks = new List<SafeFileHandle>();

        try
        {
            foreach (var volumeName in EnumerateVolumeNames())
            {
                IReadOnlyList<int> backingDisks;
                try
                {
                    backingDisks = GetVolumeDiskNumbers(volumeName);
                }
                catch (Win32Exception)
                {
                    continue;
                }

                if (!backingDisks.Contains(diskNumber))
                    continue;

                var devicePath = NormalizeVolumeDevicePath(volumeName);
                var handle = OpenDevice(
                    devicePath,
                    GenericRead | GenericWrite,
                    FileShare.ReadWrite);

                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new Win32Exception(error, $"Could not open target volume {volumeName} for an exclusive safety lock.");
                }

                if (!ControlWithoutBuffers(handle, FsctlLockVolume))
                {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new Win32Exception(error, $"Could not lock target volume {volumeName}; physical write is refused.");
                }

                if (!ControlWithoutBuffers(handle, FsctlDismountVolume))
                {
                    var error = Marshal.GetLastWin32Error();
                    _ = ControlWithoutBuffers(handle, FsctlUnlockVolume);
                    handle.Dispose();
                    throw new Win32Exception(error, $"Could not dismount target volume {volumeName}; physical write is refused.");
                }

                locks.Add(handle);
            }

            return locks;
        }
        catch
        {
            ReleaseVolumeLocks(locks);
            throw;
        }
    }

    internal static void ReleaseVolumeLocks(IEnumerable<SafeFileHandle> handles)
    {
        foreach (var handle in handles.Reverse())
        {
            try
            {
                if (!handle.IsClosed && !handle.IsInvalid)
                    _ = ControlWithoutBuffers(handle, FsctlUnlockVolume);
            }
            catch
            {
                // Best effort on teardown. Closing the handle also releases the volume lock.
            }
            finally
            {
                handle.Dispose();
            }
        }
    }

    internal static void FlushToDisk(SafeFileHandle handle)
    {
        if (!FlushFileBuffers(handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not flush the physical-media write handle.");
    }

    private static IReadOnlyList<string> EnumerateVolumeNames()
    {
        var result = new List<string>();
        var buffer = new StringBuilder(1024);
        var searchHandle = FindFirstVolumeW(buffer, (uint)buffer.Capacity);
        if (searchHandle == InvalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows volume enumeration could not start.");

        try
        {
            while (true)
            {
                var value = buffer.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);

                buffer.Clear();
                if (FindNextVolumeW(searchHandle, buffer, (uint)buffer.Capacity))
                    continue;

                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreFiles)
                    break;

                throw new Win32Exception(error, "Windows volume enumeration failed.");
            }
        }
        finally
        {
            _ = FindVolumeClose(searchHandle);
        }

        return result;
    }

    private static IReadOnlyList<int> GetVolumeDiskNumbers(string volumeName)
    {
        var devicePath = NormalizeVolumeDevicePath(volumeName);
        using var handle = OpenDevice(
            devicePath,
            desiredAccess: 0,
            FileShare.ReadWrite | FileShare.Delete);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open volume {volumeName} for extent query.");

        var output = new byte[64 * 1024];
        if (!DeviceIoControl(
                handle,
                IoctlVolumeGetVolumeDiskExtents,
                null,
                0,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero)
            || returned < 4)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not query physical extents for volume {volumeName}.");
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(output);
        var firstExtentOffset = Marshal.OffsetOf<VolumeDiskExtentsLayout>(nameof(VolumeDiskExtentsLayout.FirstExtent)).ToInt32();
        var extentSize = Marshal.SizeOf<DiskExtent>();
        var result = new HashSet<int>();

        for (var i = 0u; i < count; i++)
        {
            var offset = firstExtentOffset + checked((int)i * extentSize);
            if (offset < 0 || offset + 4 > returned || offset + 4 > output.Length)
                throw new InvalidDataException($"Volume extent list for {volumeName} is truncated or inconsistent.");

            var diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(offset, 4));
            if (diskNumber > int.MaxValue)
                throw new InvalidDataException($"Volume {volumeName} reported an out-of-range physical disk number.");

            result.Add((int)diskNumber);
        }

        return result.OrderBy(x => x).ToArray();
    }

    private static string NormalizeVolumeDevicePath(string volumeName)
    {
        var value = volumeName.Trim();
        while (value.EndsWith('\\'))
            value = value[..^1];
        return value;
    }

    private static bool ControlWithoutBuffers(SafeFileHandle handle, uint controlCode)
        => DeviceIoControl(
            handle,
            controlCode,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            0,
            out _,
            IntPtr.Zero);

    [StructLayout(LayoutKind.Sequential)]
    private struct DiskExtent
    {
        public uint DiskNumber;
        public long StartingOffset;
        public long ExtentLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeDiskExtentsLayout
    {
        public uint NumberOfDiskExtents;
        public DiskExtent FirstExtent;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathNameW(
        string lpszFileName,
        StringBuilder lpszVolumePathName,
        int cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string lpszVolumeMountPoint,
        StringBuilder lpszVolumeName,
        int cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstVolumeW(StringBuilder lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextVolumeW(IntPtr hFindVolume, StringBuilder lpszVolumeName, uint cchBufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindVolumeClose(IntPtr hFindVolume);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[]? lpInBuffer,
        int nInBufferSize,
        [Out] byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);
}
