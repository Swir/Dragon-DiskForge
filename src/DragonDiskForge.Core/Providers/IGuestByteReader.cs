namespace DragonDiskForge.Core.Providers;

/// <summary>
/// Read-only random access to the guest-visible byte address space of a virtual-disk container.
/// Implementations must never expose bytes that cannot be translated truthfully from the container.
/// </summary>
public interface IGuestByteReader : IAsyncDisposable
{
    ulong Length { get; }

    ValueTask ReadExactlyAsync(
        ulong guestOffset,
        Memory<byte> destination,
        CancellationToken cancellationToken = default);
}
