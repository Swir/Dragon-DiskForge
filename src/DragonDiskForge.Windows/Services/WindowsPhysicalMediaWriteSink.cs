using System.ComponentModel;
using System.Runtime.InteropServices;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace DragonDiskForge.Windows.Services;

/// <summary>
/// Windows physical-device sink candidate for the 0.7 disposable-media validation gate.
/// The type is deliberately not wired to the application UI. Creation requires the exact
/// destination-bound confirmation token, a successful Windows-specific read-only preflight,
/// exclusive locks on every discoverable target volume, a fresh identity revalidation after
/// the device handle is opened, and sector-aligned writes.
/// </summary>
public sealed class WindowsPhysicalMediaWriteSink : IPhysicalMediaWriteSink, IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly IReadOnlyList<SafeFileHandle> _volumeLocks;
    private readonly long _capacityBytes;
    private long _nextOffset;
    private bool _disposed;

    private WindowsPhysicalMediaWriteSink(
        PhysicalDiskInfo destination,
        int logicalSectorSizeBytes,
        FileStream stream,
        IReadOnlyList<SafeFileHandle> volumeLocks)
    {
        Destination = destination;
        LogicalSectorSizeBytes = logicalSectorSizeBytes;
        _stream = stream;
        _volumeLocks = volumeLocks;
        _capacityBytes = destination.CapacityBytes
            ?? throw new InvalidOperationException("A physical write sink requires a proven destination capacity.");
    }

    public PhysicalDiskInfo Destination { get; }

    public int LogicalSectorSizeBytes { get; }

    public static async Task<WindowsPhysicalMediaWriteSink> OpenAsync(
        PhysicalMediaWritePlan plan,
        string? suppliedConfirmationToken,
        WindowsPhysicalMediaWritePreflightService? preflightService = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows physical-media writing is implemented only for Windows.");

        var safety = new PhysicalMediaSafetyService();
        if (!safety.ConfirmationMatches(plan, suppliedConfirmationToken))
            throw new InvalidOperationException("The exact destination-bound destructive confirmation token is required before a Windows physical write handle can be opened.");

        preflightService ??= new WindowsPhysicalMediaWritePreflightService();
        var initial = await preflightService.ValidateAsync(plan, cancellationToken).ConfigureAwait(false);
        ThrowIfRefused(initial, "Initial Windows physical-media preflight refused the operation");

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<SafeFileHandle>? volumeLocks = null;
        SafeFileHandle? deviceHandle = null;
        FileStream? stream = null;

        try
        {
            volumeLocks = WindowsPhysicalMediaInterop.LockAndDismountVolumes(initial.Destination.DiskNumber);
            cancellationToken.ThrowIfCancellationRequested();

            deviceHandle = WindowsPhysicalMediaInterop.OpenDevice(
                initial.Destination.DevicePath,
                WindowsPhysicalMediaInterop.GenericWrite,
                FileShare.ReadWrite,
                WindowsPhysicalMediaInterop.FileFlagWriteThrough);

            if (deviceHandle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open {initial.Destination.DevicePath} for physical write access.");

            // Re-query inventory/source/geometry after locking target volumes and opening the
            // write handle. Any identity, capacity, source-backing or sector change fails closed.
            var final = await preflightService.ValidateAsync(plan, cancellationToken).ConfigureAwait(false);
            ThrowIfRefused(final, "Final Windows physical-media preflight refused the operation");

            if (!SameIdentity(initial.Destination, final.Destination)
                || initial.LogicalSectorSizeBytes != final.LogicalSectorSizeBytes)
            {
                throw new InvalidOperationException("Destination identity or sector geometry changed while acquiring the physical write handle.");
            }

            if (!safety.ConfirmationMatches(plan, suppliedConfirmationToken))
                throw new InvalidOperationException("Destructive confirmation binding changed before the physical write sink became available.");

            // The handle is intentionally synchronous + WRITE_THROUGH. The coordinator still uses
            // async APIs, but cancellation is guaranteed at chunk boundaries instead of pretending
            // that an in-flight raw device write can always be cancelled atomically.
            stream = new FileStream(
                deviceHandle,
                FileAccess.Write,
                bufferSize: Math.Max(4096, final.LogicalSectorSizeBytes),
                isAsync: false);
            deviceHandle = null; // FileStream now owns the handle lifetime.

            return new WindowsPhysicalMediaWriteSink(
                final.Destination,
                final.LogicalSectorSizeBytes,
                stream,
                volumeLocks);
        }
        catch
        {
            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
            else
                deviceHandle?.Dispose();

            if (volumeLocks is not null)
                WindowsPhysicalMediaInterop.ReleaseVolumeLocks(volumeLocks);

            throw;
        }
    }

    public async ValueTask WriteAsync(
        long offset,
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (buffer.Length == 0)
            throw new ArgumentException("Physical-media writes cannot contain an empty buffer.", nameof(buffer));

        if (offset != _nextOffset)
            throw new IOException($"Physical-media write offset {offset} is not the expected sequential offset {_nextOffset}.");

        if (offset < 0
            || offset % LogicalSectorSizeBytes != 0
            || buffer.Length % LogicalSectorSizeBytes != 0)
        {
            throw new IOException(
                $"Physical-media writes must be aligned to the {LogicalSectorSizeBytes}-byte logical sector size.");
        }

        long endOffset;
        try
        {
            endOffset = checked(offset + buffer.Length);
        }
        catch (OverflowException)
        {
            throw new IOException("Physical-media write range overflowed.");
        }

        if (endOffset > _capacityBytes)
            throw new IOException("Physical-media write would exceed the proven destination capacity.");

        await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        _nextOffset = endOffset;
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        WindowsPhysicalMediaInterop.FlushToDisk(_stream.SafeFileHandle);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            WindowsPhysicalMediaInterop.ReleaseVolumeLocks(_volumeLocks);
        }
    }

    private static void ThrowIfRefused(WindowsPhysicalMediaWritePreflight preflight, string prefix)
    {
        if (preflight.IsAllowed)
            return;

        var details = preflight.RefusalReasons.Count > 0
            ? string.Join(" ", preflight.RefusalReasons)
            : "No validated write path is available.";
        throw new InvalidOperationException($"{prefix}: {details}");
    }

    private static bool SameIdentity(PhysicalDiskInfo left, PhysicalDiskInfo right)
        => left.DiskNumber == right.DiskNumber
            && string.Equals(left.DevicePath, right.DevicePath, StringComparison.OrdinalIgnoreCase)
            && left.HasStableIdentity
            && right.HasStableIdentity
            && !string.IsNullOrWhiteSpace(left.StableId)
            && string.Equals(left.StableId, right.StableId, StringComparison.Ordinal)
            && left.CapacityBytes == right.CapacityBytes
            && left.IsSystemDisk == right.IsSystemDisk;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
