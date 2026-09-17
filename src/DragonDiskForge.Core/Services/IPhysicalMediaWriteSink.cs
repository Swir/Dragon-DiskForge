using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

/// <summary>
/// Abstract destination for a physical-media write operation.
/// Implementations own the OS/device handle lifetime; the execution service only coordinates
/// bounded sequential writes, cancellation and fail-safe result reporting.
/// </summary>
public interface IPhysicalMediaWriteSink
{
    PhysicalDiskInfo Destination { get; }

    ValueTask WriteAsync(
        long offset,
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken);

    ValueTask FlushAsync(CancellationToken cancellationToken);
}
