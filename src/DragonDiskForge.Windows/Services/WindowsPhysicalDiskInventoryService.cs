using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace DragonDiskForge.Windows.Services;

public sealed class WindowsPhysicalDiskInventoryService : IPhysicalDiskInventoryService
{
    private const uint IoctlDiskGetLengthInfo = 0x0007405C;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const int MaxPhysicalDrives = 64;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;

    public Task<IReadOnlyList<PhysicalDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Physical disk inventory is implemented only for Windows.");

        return Task.Run<IReadOnlyList<PhysicalDiskInfo>>(
            () => Enumerate(cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<PhysicalDiskInfo> Enumerate(CancellationToken cancellationToken)
    {
        var systemDisks = GetSystemDiskNumbers();
        var disks = new List<PhysicalDiskInfo>();

        for (var diskNumber = 0; diskNumber < MaxPhysicalDrives; diskNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var devicePath = $@"\\.\PhysicalDrive{diskNumber}";

            using var handle = OpenQueryHandle(devicePath);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                if (error is ErrorFileNotFound or ErrorPathNotFound)
                    continue;

                if (error == ErrorAccessDenied)
                {
                    disks.Add(CreateAccessDeniedEntry(diskNumber, devicePath, systemDisks.Contains(diskNumber)));
                    continue;
                }

                continue;
            }

            var capacity = TryGetLength(handle);
            var descriptor = TryGetStorageDescriptor(handle);
            var serial = descriptor.SerialNumber;
            var hasStableIdentity = !string.IsNullOrWhiteSpace(serial);
            var stableId = BuildStableId(
                hasStableIdentity
                    ? $"serial|{descriptor.Vendor}|{descriptor.Product}|{serial}"
                    : $"fallback|{devicePath}|{capacity}|{descriptor.Vendor}|{descriptor.Product}|{descriptor.Revision}");

            var evidence = new List<string>
            {
                "inventory:win32-read-only-query",
                $"disk-number:{diskNumber}",
                $"bus:{descriptor.BusType}",
                hasStableIdentity ? "identity:serial-backed" : "identity:fallback-ambiguous"
            };

            if (capacity is > 0)
                evidence.Add($"capacity-bytes:{capacity.Value}");
            if (systemDisks.Contains(diskNumber))
                evidence.Add("system-disk:system-volume-extent");
            if (descriptor.IsRemovable)
                evidence.Add("media:removable");

            disks.Add(new PhysicalDiskInfo(
                diskNumber,
                devicePath,
                capacity,
                descriptor.BusType,
                descriptor.IsRemovable,
                systemDisks.Contains(diskNumber),
                descriptor.Vendor,
                descriptor.Product,
                descriptor.Revision,
                serial,
                stableId,
                hasStableIdentity,
                evidence));
        }

        return disks.OrderBy(x => x.DiskNumber).ToArray();
    }

    private static PhysicalDiskInfo CreateAccessDeniedEntry(int diskNumber, string devicePath, bool isSystemDisk)
    {
        return new PhysicalDiskInfo(
            diskNumber,
            devicePath,
            CapacityBytes: null,
            BusType: "Unknown",
            IsRemovable: false,
            IsSystemDisk: isSystemDisk,
            Vendor: null,
            Product: null,
            Revision: null,
            SerialNumber: null,
            StableId: BuildStableId($"inaccessible|{devicePath}"),
            HasStableIdentity: false,
            Evidence: new[]
            {
                "inventory:win32-read-only-query",
                $"disk-number:{diskNumber}",
                "query:access-denied",
                "identity:fallback-ambiguous"
            });
    }

    private static SafeFileHandle OpenQueryHandle(string devicePath)
    {
        return CreateFileW(
            devicePath,
            dwDesiredAccess: 0,
            dwShareMode: FileShare.ReadWrite | FileShare.Delete,
            lpSecurityAttributes: IntPtr.Zero,
            dwCreationDisposition: FileMode.Open,
            dwFlagsAndAttributes: 0,
            hTemplateFile: IntPtr.Zero);
    }

    private static long? TryGetLength(SafeFileHandle handle)
    {
        var output = new byte[8];
        if (!DeviceIoControl(
                handle,
                IoctlDiskGetLengthInfo,
                null,
                0,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero) || returned < 8)
        {
            return null;
        }

        var value = BinaryPrimitives.ReadInt64LittleEndian(output);
        return value > 0 ? value : null;
    }

    private static StorageDescriptor TryGetStorageDescriptor(SafeFileHandle handle)
    {
        var query = new byte[12];
        var output = new byte[4096];

        if (!DeviceIoControl(
                handle,
                IoctlStorageQueryProperty,
                query,
                query.Length,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero) || returned < 33)
        {
            return StorageDescriptor.Unknown;
        }

        var removable = output[10] != 0;
        var vendorOffset = ReadUInt32(output, 12);
        var productOffset = ReadUInt32(output, 16);
        var revisionOffset = ReadUInt32(output, 20);
        var serialOffset = ReadUInt32(output, 24);
        var busType = ReadUInt32(output, 28);

        return new StorageDescriptor(
            ReadAnsiString(output, returned, vendorOffset),
            ReadAnsiString(output, returned, productOffset),
            ReadAnsiString(output, returned, revisionOffset),
            ReadAnsiString(output, returned, serialOffset),
            MapBusType(busType),
            removable);
    }

    private static HashSet<int> GetSystemDiskNumbers()
    {
        var result = new HashSet<int>();
        var windowsRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrWhiteSpace(windowsRoot) || windowsRoot.Length < 2)
            return result;

        var volumePath = $@"\\.\{windowsRoot[..2]}";
        using var handle = OpenQueryHandle(volumePath);
        if (handle.IsInvalid)
            return result;

        var output = new byte[4096];
        if (!DeviceIoControl(
                handle,
                IoctlVolumeGetVolumeDiskExtents,
                null,
                0,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero) || returned < 4)
        {
            return result;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(output);
        var firstExtentOffset = Marshal.OffsetOf<VolumeDiskExtentsLayout>(nameof(VolumeDiskExtentsLayout.FirstExtent)).ToInt32();
        var extentSize = Marshal.SizeOf<DiskExtent>();

        for (var i = 0u; i < count; i++)
        {
            var offset = firstExtentOffset + checked((int)i * extentSize);
            if (offset < 0 || offset + 4 > returned || offset + 4 > output.Length)
                break;

            var diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(offset, 4));
            if (diskNumber <= int.MaxValue)
                result.Add((int)diskNumber);
        }

        return result;
    }

    private static uint ReadUInt32(byte[] buffer, int offset)
    {
        return offset >= 0 && offset + 4 <= buffer.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4))
            : 0;
    }

    private static string? ReadAnsiString(byte[] buffer, int returned, uint offset)
    {
        if (offset == 0 || offset >= returned || offset >= buffer.Length)
            return null;

        var start = checked((int)offset);
        var end = start;
        var limit = Math.Min(returned, buffer.Length);
        while (end < limit && buffer[end] != 0)
            end++;

        if (end <= start)
            return null;

        var value = Encoding.ASCII.GetString(buffer, start, end - start).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string BuildStableId(string material)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string MapBusType(uint busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE1394",
        6 => "FibreChannel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "FileBackedVirtual",
        16 => "StorageSpaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "Unknown"
    };

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

    private sealed record StorageDescriptor(
        string? Vendor,
        string? Product,
        string? Revision,
        string? SerialNumber,
        string BusType,
        bool IsRemovable)
    {
        public static readonly StorageDescriptor Unknown = new(null, null, null, null, "Unknown", false);
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
}
