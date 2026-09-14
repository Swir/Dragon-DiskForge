namespace DragonDiskForge.Core.Services;

public sealed class MountOperationException : Exception
{
    public MountOperationException(
        string message,
        bool requiresElevation = false,
        int? nativeExitCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RequiresElevation = requiresElevation;
        NativeExitCode = nativeExitCode;
    }

    public bool RequiresElevation { get; }
    public int? NativeExitCode { get; }
}
