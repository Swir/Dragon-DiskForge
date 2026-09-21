using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

namespace DragonDiskForge.Windows.Services;

public sealed class WindowsDiskImageMountService : IMountService
{
    private const string ElevatedMountHelperExecutable = "dragon-diskforge-mount.exe";

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".vhd", ".vhdx"
    };

    private readonly WindowsDiskImageManager _manager = new();

    public bool CanHandle(string imagePath)
        => SupportedExtensions.Contains(Path.GetExtension(imagePath));

    public bool RequiresElevation(string imagePath)
    {
        var extension = Path.GetExtension(imagePath);
        return extension.Equals(".vhd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vhdx", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MountState> GetStateAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        var path = ValidateImagePath(imagePath);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        var stateQuery = Task.Run(
            () => _manager.TryGetState(path),
            CancellationToken.None);
        var state = await stateQuery.WaitAsync(cancellationToken).ConfigureAwait(false);

        return state is null
            ? new MountState(path, false, null, Array.Empty<string>(), RequiresElevation(path))
            : CreateMountState(path, state);
    }

    public async Task<IReadOnlyList<MountState>> GetMountedAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        var mountedQuery = Task.Run(
            _manager.GetMounted,
            CancellationToken.None);
        var mounted = await mountedQuery.WaitAsync(cancellationToken).ConfigureAwait(false);

        return mounted
            .Where(state => state.Attached && CanHandle(state.ImagePath))
            .Select(state => CreateMountState(state.ImagePath, state))
            .OrderBy(state => state.ImagePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<MountState> MountAsync(
        MountRequest request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = ValidateImagePath(request.ImagePath);
        EnsureSupported(path);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(0.05d);
        var current = await GetStateAsync(path, cancellationToken).ConfigureAwait(false);
        if (current.IsMounted)
        {
            progress?.Report(1d);
            return current;
        }

        progress?.Report(0.2d);
        var readOnly = request.ReadOnly || Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase);
        var requestElevation = RequiresElevation(path) && !IsCurrentProcessElevated();

        await RunStorageMutationAsync(
            operation: "mount",
            path,
            readOnly,
            request.NoDriveLetter,
            requestElevation,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(0.75d);
        var mounted = await WaitForCommittedStateAsync(path, true).ConfigureAwait(false);
        progress?.Report(1d);
        return mounted;
    }

    public async Task<MountState> UnmountAsync(
        string imagePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = ValidateImagePath(imagePath);
        EnsureSupported(path);
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(0.05d);
        var current = await GetStateAsync(path, cancellationToken).ConfigureAwait(false);
        if (!current.IsMounted)
        {
            progress?.Report(1d);
            return current;
        }

        progress?.Report(0.25d);
        var requestElevation = RequiresElevation(path) && !IsCurrentProcessElevated();

        await RunStorageMutationAsync(
            operation: "unmount",
            path,
            readOnly: true,
            noDriveLetter: false,
            requestElevation,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(0.75d);
        var detached = await WaitForCommittedStateAsync(path, false).ConfigureAwait(false);
        progress?.Report(1d);
        return detached;
    }

    private MountState CreateMountState(string path, WindowsDiskImageState state)
        => new(
            Path.GetFullPath(path),
            state.Attached,
            state.DevicePath,
            state.DriveLetters,
            RequiresElevation(path));

    private async Task<MountState> WaitForStateAsync(
        string path,
        bool mounted,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await GetStateAsync(path, cancellationToken).ConfigureAwait(false);
            if (state.IsMounted == mounted)
                return state;

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        throw new MountOperationException(
            mounted
                ? "Windows accepted the mount request, but the image did not become attached in time."
                : "Windows accepted the unmount request, but the image still appears attached.");
    }

    private async Task<MountState> WaitForCommittedStateAsync(string path, bool mounted)
    {
        using var reconciliationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            return await WaitForStateAsync(path, mounted, reconciliationTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (reconciliationTimeout.IsCancellationRequested)
        {
            throw new MountOperationException(
                "Windows completed the native disk-image command, but Dragon DiskForge could not confirm the resulting state in time. Refresh Mounted before retrying.");
        }
    }

    private async Task RunStorageMutationAsync(
        string operation,
        string path,
        bool readOnly,
        bool noDriveLetter,
        bool requestElevation,
        CancellationToken cancellationToken)
    {
        // Native mount/dismount has an explicit commit boundary: caller cancellation is
        // honored before the Windows storage mutation starts. Once started, let Windows
        // finish and reconcile the real state rather than pretending the operation rolled back.
        cancellationToken.ThrowIfCancellationRequested();

        if (requestElevation)
        {
            await RunElevatedMountHelperAsync(
                operation,
                path,
                readOnly,
                noDriveLetter).ConfigureAwait(false);
            return;
        }

        try
        {
            await Task.Run(
                () =>
                {
                    if (operation.Equals("mount", StringComparison.Ordinal))
                        _manager.Mount(path, readOnly, noDriveLetter);
                    else
                        _manager.Dismount(path);
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MountOperationException(
                "Windows requires administrator approval for this disk-image operation.",
                requiresElevation: RequiresElevation(path),
                innerException: ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException)
        {
            throw new MountOperationException(
                $"Windows Storage could not {operation} the disk image.",
                requiresElevation: RequiresElevation(path),
                innerException: ex);
        }
    }

    private static async Task RunElevatedMountHelperAsync(
        string operation,
        string path,
        bool readOnly,
        bool noDriveLetter)
    {
        var helperPath = ResolveElevatedMountHelperPath();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        process.StartInfo.ArgumentList.Add(operation);
        process.StartInfo.ArgumentList.Add("--image");
        process.StartInfo.ArgumentList.Add(path);

        if (operation.Equals("mount", StringComparison.Ordinal))
        {
            process.StartInfo.ArgumentList.Add(readOnly ? "--read-only" : "--read-write");
            if (noDriveLetter)
                process.StartInfo.ArgumentList.Add("--no-drive-letter");
        }

        try
        {
            process.Start();
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new MountOperationException(
                    $"The elevated Windows disk-image helper failed with exit code {process.ExitCode}.",
                    requiresElevation: true,
                    nativeExitCode: process.ExitCode);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new MountOperationException(
                "Administrator approval was cancelled. The disk image was not changed.",
                requiresElevation: true,
                nativeExitCode: ex.NativeErrorCode,
                innerException: ex);
        }
        catch (Win32Exception ex)
        {
            throw new MountOperationException(
                "Windows could not start the elevated disk-image helper.",
                requiresElevation: true,
                nativeExitCode: ex.NativeErrorCode,
                innerException: ex);
        }
    }

    private static string ResolveElevatedMountHelperPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", ElevatedMountHelperExecutable),
            Path.Combine(AppContext.BaseDirectory, ElevatedMountHelperExecutable)
        };

        var helperPath = candidates.FirstOrDefault(File.Exists);
        if (helperPath is not null)
            return helperPath;

        throw new MountOperationException(
            "The packaged elevated disk-image helper is missing. Reinstall or re-extract Dragon DiskForge before retrying.",
            requiresElevation: true);
    }

    private static string ValidateImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new ArgumentException("Image path cannot be empty.", nameof(imagePath));

        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Disk image was not found.", fullPath);

        return fullPath;
    }

    private void EnsureSupported(string path)
    {
        if (!CanHandle(path))
            throw new MountOperationException("Native Windows mounting currently supports ISO, VHD and VHDX images only.");
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native disk-image mounting is available only on Windows.");
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
