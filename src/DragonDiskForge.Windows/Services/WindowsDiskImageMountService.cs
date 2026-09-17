using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using DragonDiskForge.Core.Models;
using DragonDiskForge.Core.Services;

namespace DragonDiskForge.Windows.Services;

public sealed class WindowsDiskImageMountService : IMountService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".vhd", ".vhdx"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

        var output = await RunPowerShellCaptureAsync(BuildStateScript(path), cancellationToken);
        var payload = JsonSerializer.Deserialize<DiskImageStatePayload>(output, JsonOptions)
            ?? throw new MountOperationException("Windows returned an empty disk-image state.");

        return CreateMountState(path, payload);
    }

    public async Task<IReadOnlyList<MountState>> GetMountedAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureWindows();
        cancellationToken.ThrowIfCancellationRequested();

        var output = await RunPowerShellCaptureAsync(BuildMountedImagesScript(), cancellationToken);
        var payloads = JsonSerializer.Deserialize<DiskImageStatePayload[]>(output, JsonOptions)
            ?? Array.Empty<DiskImageStatePayload>();

        return payloads
            .Where(payload => payload.Attached
                && !string.IsNullOrWhiteSpace(payload.ImagePath)
                && CanHandle(payload.ImagePath))
            .Select(payload => CreateMountState(Path.GetFullPath(payload.ImagePath!), payload))
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
        var current = await GetStateAsync(path, cancellationToken);
        if (current.IsMounted)
        {
            progress?.Report(1d);
            return current;
        }

        progress?.Report(0.2d);
        var readOnly = request.ReadOnly || Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase);
        var script = BuildMountScript(path, readOnly, request.NoDriveLetter);
        var requestElevation = RequiresElevation(path) && !IsCurrentProcessElevated();
        await RunPowerShellActionAsync(script, requestElevation, cancellationToken);

        progress?.Report(0.75d);
        var mounted = await WaitForCommittedStateAsync(path, true);
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
        var current = await GetStateAsync(path, cancellationToken);
        if (!current.IsMounted)
        {
            progress?.Report(1d);
            return current;
        }

        progress?.Report(0.25d);
        var requestElevation = RequiresElevation(path) && !IsCurrentProcessElevated();
        await RunPowerShellActionAsync(BuildUnmountScript(path), requestElevation, cancellationToken);

        progress?.Report(0.75d);
        var detached = await WaitForCommittedStateAsync(path, false);
        progress?.Report(1d);
        return detached;
    }

    private MountState CreateMountState(string path, DiskImageStatePayload payload)
        => new(
            path,
            payload.Attached,
            string.IsNullOrWhiteSpace(payload.DevicePath) ? null : payload.DevicePath,
            payload.DriveLetters?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>(),
            RequiresElevation(path));

    private async Task<MountState> WaitForStateAsync(
        string path,
        bool mounted,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await GetStateAsync(path, cancellationToken);
            if (state.IsMounted == mounted)
                return state;

            await Task.Delay(150, cancellationToken);
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
            return await WaitForStateAsync(path, mounted, reconciliationTimeout.Token);
        }
        catch (OperationCanceledException) when (reconciliationTimeout.IsCancellationRequested)
        {
            throw new MountOperationException(
                "Windows completed the native disk-image command, but Dragon DiskForge could not confirm the resulting state in time. Refresh Mounted before retrying.");
        }
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

    private static string BuildStateScript(string path)
    {
        var literal = ToPowerShellLiteral(path);
        return $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            $WarningPreference = 'SilentlyContinue'
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $img = Get-DiskImage -ImagePath {{literal}} -ErrorAction Stop
            $letters = @()
            if ($img.Attached) {
                try {
                    $letters = @($img | Get-Volume -ErrorAction Stop | Where-Object { $_.DriveLetter } | ForEach-Object { "$($_.DriveLetter):" })
                } catch {}
                if ($letters.Count -eq 0) {
                    try {
                        $letters = @($img | Get-Disk -ErrorAction Stop | Get-Partition -ErrorAction Stop | Get-Volume -ErrorAction Stop | Where-Object { $_.DriveLetter } | ForEach-Object { "$($_.DriveLetter):" })
                    } catch {}
                }
            }
            [pscustomobject]@{
                ImagePath = [string]$img.ImagePath
                Attached = [bool]$img.Attached
                DevicePath = [string]$img.DevicePath
                DriveLetters = @($letters)
            } | ConvertTo-Json -Compress -Depth 3
            """;
    }

    private static string BuildMountedImagesScript()
        => """
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            $WarningPreference = 'SilentlyContinue'
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

            $images = @()
            foreach ($volume in @(Get-Volume -ErrorAction SilentlyContinue)) {
                try {
                    $candidate = Get-DiskImage -Volume $volume -ErrorAction Stop
                    if ($candidate -and $candidate.Attached -and $candidate.ImagePath) {
                        $images += $candidate
                    }
                } catch {}
            }

            $images = @($images | Sort-Object ImagePath -Unique)
            $result = @()
            foreach ($img in $images) {
                $letters = @()
                try {
                    $letters = @($img | Get-Volume -ErrorAction Stop | Where-Object { $_.DriveLetter } | ForEach-Object { "$($_.DriveLetter):" })
                } catch {}
                if ($letters.Count -eq 0) {
                    try {
                        $letters = @($img | Get-Disk -ErrorAction Stop | Get-Partition -ErrorAction Stop | Get-Volume -ErrorAction Stop | Where-Object { $_.DriveLetter } | ForEach-Object { "$($_.DriveLetter):" })
                    } catch {}
                }

                $result += [pscustomobject]@{
                    ImagePath = [string]$img.ImagePath
                    Attached = [bool]$img.Attached
                    DevicePath = [string]$img.DevicePath
                    DriveLetters = @($letters)
                }
            }
            ConvertTo-Json -InputObject @($result) -Compress -Depth 4
            """;

    private static string BuildMountScript(string path, bool readOnly, bool noDriveLetter)
    {
        var literal = ToPowerShellLiteral(path);
        var access = readOnly ? "ReadOnly" : "ReadWrite";
        var noLetter = noDriveLetter ? " -NoDriveLetter" : string.Empty;
        return $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            Mount-DiskImage -ImagePath {{literal}} -Access {{access}}{{noLetter}} -ErrorAction Stop | Out-Null
            """;
    }

    private static string BuildUnmountScript(string path)
    {
        var literal = ToPowerShellLiteral(path);
        return $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            Dismount-DiskImage -ImagePath {{literal}} -ErrorAction Stop | Out-Null
            """;
    }

    private static string ToPowerShellLiteral(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string EncodePowerShell(string script)
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static async Task<string> RunPowerShellCaptureAsync(
        string script,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {EncodePowerShell(script)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();

            if (process.ExitCode != 0)
                throw BuildOperationException(error, process.ExitCode);

            if (string.IsNullOrWhiteSpace(output))
                throw new MountOperationException("Windows Storage returned no disk-image information.");

            return output;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Win32Exception ex)
        {
            throw new MountOperationException("Windows PowerShell could not be started.", innerException: ex);
        }
    }

    private static async Task RunPowerShellActionAsync(
        string script,
        bool requestElevation,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = "powershell.exe";
        process.StartInfo.Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {EncodePowerShell(script)}";

        if (requestElevation)
        {
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.Verb = "runas";
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }
        else
        {
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        }

        // Native mount/dismount has an explicit commit boundary: cancellation is honored
        // before launch. Once Windows starts the storage mutation we let it finish, then
        // reconcile the real storage state instead of reporting a potentially false cancel.
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            process.Start();
            Task<string>? errorTask = null;
            if (!requestElevation)
                errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            await process.WaitForExitAsync(CancellationToken.None);
            var error = errorTask is null ? string.Empty : (await errorTask).Trim();

            if (process.ExitCode != 0)
                throw BuildOperationException(error, process.ExitCode, requestElevation);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new MountOperationException(
                "Administrator approval was cancelled. The disk image was not changed.",
                requiresElevation: requestElevation,
                nativeExitCode: ex.NativeErrorCode,
                innerException: ex);
        }
        catch (Win32Exception ex)
        {
            throw new MountOperationException(
                "Windows could not start the native disk-image operation.",
                requiresElevation: requestElevation,
                nativeExitCode: ex.NativeErrorCode,
                innerException: ex);
        }
    }

    private static MountOperationException BuildOperationException(
        string error,
        int exitCode,
        bool requiresElevation = false)
    {
        var text = string.IsNullOrWhiteSpace(error) ? "Windows disk-image operation failed." : error;
        var lower = text.ToLowerInvariant();

        var friendly = lower switch
        {
            _ when lower.Contains("access is denied") || lower.Contains("administrator")
                => "Windows requires administrator approval for this disk-image operation.",
            _ when lower.Contains("being used by another process") || lower.Contains("already") && lower.Contains("mount")
                => "The disk image is already mounted or currently in use.",
            _ when lower.Contains("cannot find") || lower.Contains("not found")
                => "Windows could not find the disk image or one of its required storage objects.",
            _ => text
        };

        return new MountOperationException(friendly, requiresElevation, exitCode);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cancellation for read-only state capture only.
        }
    }

    private sealed class DiskImageStatePayload
    {
        public string? ImagePath { get; init; }
        public bool Attached { get; init; }
        public string? DevicePath { get; init; }
        public string[]? DriveLetters { get; init; }
    }
}
