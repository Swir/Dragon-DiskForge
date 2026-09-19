[CmdletBinding()]
param(
    [ValidateSet("capture", "verify", "status", "self-test")]
    [string]$Mode = "verify",
    [string]$WorkspacePath = "",
    [string]$BindingPath = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:BindingSchemaVersion = 1
$Script:SessionSchemaVersion = 1
$Script:SessionFileName = "beta-qa-session.json"
$Script:DefaultBindingFileName = "beta-qa-explorer-witness.json"

function Write-Utf8NoBom {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Text)
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Save-JsonAtomic {
    param([Parameter(Mandatory = $true)]$Value, [Parameter(Mandatory = $true)][string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $tempPath = "$fullPath.tmp.$([guid]::NewGuid().ToString('N'))"
    try {
        Write-Utf8NoBom -Path $tempPath -Text (($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
        Move-Item -LiteralPath $tempPath -Destination $fullPath -Force
        $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Utf8NoBom -Path "$fullPath.sha256" -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($fullPath), [Environment]::NewLine)
        return [pscustomobject]@{ path = $fullPath; sha256 = $hash; sidecar = "$fullPath.sha256" }
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
    }
}

function Assert-Sidecar {
    param([Parameter(Mandatory = $true)][string]$PayloadPath, [string]$SidecarPath = "")
    $payload = (Resolve-Path -LiteralPath $PayloadPath).Path
    if ([string]::IsNullOrWhiteSpace($SidecarPath)) { $SidecarPath = "$payload.sha256" }
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path
    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') { throw "SHA-256 sidecar has an invalid format: $sidecar" }
    $declared = $Matches[1].ToLowerInvariant()
    if ($Matches[2].Trim() -ne [System.IO.Path]::GetFileName($payload)) { throw "SHA-256 sidecar targets the wrong file." }
    $actual = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $declared) { throw "SHA-256 mismatch for '$payload'." }
    return $actual
}

function Test-PathInside {
    param([Parameter(Mandatory = $true)][string]$Child, [Parameter(Mandatory = $true)][string]$Parent)
    $childFull = [System.IO.Path]::GetFullPath($Child).TrimEnd([char]92, [char]47)
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd([char]92, [char]47)
    if ([string]::Equals($childFull, $parentFull, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $childFull.StartsWith(($parentFull + [System.IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)
}

function Get-IsElevated {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $null }
    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch { return $null }
}

function Get-UacEnabled {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $null }
    try {
        return ([int](Get-ItemPropertyValue -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name EnableLUA -ErrorAction Stop) -ne 0)
    }
    catch { return $null }
}

function Get-EnvironmentSnapshot {
    $sessionId = -1
    try { $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId } catch { $sessionId = -1 }
    $build = ""
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        try { $build = [string](Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction Stop).CurrentBuildNumber }
        catch { $build = [Environment]::OSVersion.Version.Build.ToString() }
    }
    return [pscustomobject]@{
        userInteractive = [Environment]::UserInteractive
        processElevated = (Get-IsElevated)
        uacEnabled = (Get-UacEnabled)
        sessionId = $sessionId
        osBuild = $build
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }
}

function Assert-InteractiveSessionMatches {
    param([Parameter(Mandatory = $true)]$EnvironmentInfo, [Parameter(Mandatory = $true)]$Session)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw "Explorer witness modes are supported only on Windows." }
    if (-not [bool]$EnvironmentInfo.userInteractive -or [int]$EnvironmentInfo.sessionId -le 0) { throw "Explorer witness requires an interactive Windows desktop session." }
    if ($null -eq $EnvironmentInfo.processElevated -or [bool]$EnvironmentInfo.processElevated) { throw "Explorer witness must run from the normal unelevated beta-QA session." }
    if ($null -eq $EnvironmentInfo.uacEnabled -or -not [bool]$EnvironmentInfo.uacEnabled) { throw "Explorer witness requires UAC to be enabled." }
    if ([int]$EnvironmentInfo.sessionId -ne [int]$Session.environment.sessionId) { throw "Current session id differs from the prepared beta-QA session." }
    if ([string]$EnvironmentInfo.osBuild -ne [string]$Session.environment.osBuild) { throw "Current Windows build differs from the prepared beta-QA session." }
    if ([string]$EnvironmentInfo.processArchitecture -ne [string]$Session.environment.processArchitecture) { throw "Current process architecture differs from the prepared beta-QA session." }
}

function Resolve-BindingPath {
    param([Parameter(Mandatory = $true)][string]$Workspace)
    $path = if ([string]::IsNullOrWhiteSpace($BindingPath)) { Join-Path $Workspace $Script:DefaultBindingFileName } else { [System.IO.Path]::GetFullPath($BindingPath) }
    if (-not (Test-PathInside -Child $path -Parent $Workspace)) { throw "Explorer witness path must remain inside the disposable QA workspace." }
    return $path
}

function Get-WorkspaceContext {
    param([Parameter(Mandatory = $true)][string]$Workspace)
    $workspaceFull = (Resolve-Path -LiteralPath $Workspace).Path
    if (-not (Test-Path -LiteralPath $workspaceFull -PathType Container)) { throw "QA workspace is not a directory." }
    $sessionPath = Join-Path $workspaceFull $Script:SessionFileName
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) { throw "QA session metadata is missing: $sessionPath" }
    $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    if ([int]$session.schemaVersion -ne $Script:SessionSchemaVersion) { throw "Unsupported beta QA session schema '$($session.schemaVersion)'." }
    if ([bool]$session.manualGatePassed) { throw "QA session metadata must not claim a manual release gate passed." }
    if (-not [string]::Equals([System.IO.Path]::GetFullPath([string]$session.workspace), $workspaceFull, [StringComparison]::OrdinalIgnoreCase)) { throw "Session workspace no longer matches the workspace being verified." }

    $dropTarget = [System.IO.Path]::GetFullPath([string]$session.dropTarget)
    if (-not (Test-PathInside -Child $dropTarget -Parent $workspaceFull) -or -not (Test-Path -LiteralPath $dropTarget -PathType Container)) { throw "Explorer drop target is missing or escapes the QA workspace." }
    $dropItem = Get-Item -LiteralPath $dropTarget -Force
    if (($dropItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Explorer drop target is a reparse point; refusing ambiguous witness input." }

    $packagePath = (Resolve-Path -LiteralPath ([string]$session.package.path)).Path
    $checksumPath = (Resolve-Path -LiteralPath ([string]$session.package.checksumFile)).Path
    $packageHash = Assert-Sidecar -PayloadPath $packagePath -SidecarPath $checksumPath
    if ($packageHash -ne ([string]$session.package.sha256).ToLowerInvariant()) { throw "Candidate package no longer matches session metadata." }

    return [pscustomobject]@{
        workspace = $workspaceFull
        sessionPath = $sessionPath
        sessionSha256 = (Get-FileHash -LiteralPath $sessionPath -Algorithm SHA256).Hash.ToLowerInvariant()
        session = $session
        packageSha256 = $packageHash
        dropTarget = $dropTarget
    }
}

function Ensure-WindowInterop {
    if (-not ("DragonDiskForge.ExplorerWitness.WindowProbe" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace DragonDiskForge.ExplorerWitness {
    public static class WindowProbe {
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);
    }
}
"@
    }
}

function Get-ExplorerDropTargetWindow {
    param([Parameter(Mandatory = $true)][string]$DropTarget, [Parameter(Mandatory = $true)][int]$ExpectedSessionId)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw "Explorer window discovery is supported only on Windows." }
    Ensure-WindowInterop
    $target = [System.IO.Path]::GetFullPath($DropTarget).TrimEnd([char]92, [char]47)
    $shell = $null
    $windows = $null
    $matches = New-Object 'System.Collections.Generic.List[object]'
    try {
        $shell = New-Object -ComObject Shell.Application
        $windows = $shell.Windows()
        for ($i = 0; $i -lt $windows.Count; $i++) {
            $window = $null
            try {
                $window = $windows.Item($i)
                if ($null -eq $window) { continue }
                $folder = [string]$window.Document.Folder.Self.Path
                if ([string]::IsNullOrWhiteSpace($folder)) { continue }
                $folderFull = [System.IO.Path]::GetFullPath($folder).TrimEnd([char]92, [char]47)
                if (-not [string]::Equals($folderFull, $target, [StringComparison]::OrdinalIgnoreCase)) { continue }
                $hwndValue = [int64]$window.HWND
                if ($hwndValue -le 0) { continue }
                $hwnd = New-Object IntPtr($hwndValue)
                if (-not [DragonDiskForge.ExplorerWitness.WindowProbe]::IsWindowVisible($hwnd)) { continue }
                [uint32]$processId = 0
                [void][DragonDiskForge.ExplorerWitness.WindowProbe]::GetWindowThreadProcessId($hwnd, [ref]$processId)
                if ($processId -le 0) { continue }
                $process = Get-Process -Id ([int]$processId) -ErrorAction Stop
                $process.Refresh()
                if ($process.HasExited -or [string]$process.ProcessName -ne "explorer") { continue }
                if ([int]$process.SessionId -ne $ExpectedSessionId) { continue }
                $processPath = ""
                try { $processPath = [string]$process.Path } catch { $processPath = "" }
                if ([string]::IsNullOrWhiteSpace($processPath) -or [System.IO.Path]::GetFileName($processPath) -ne "explorer.exe") { continue }
                $startUtc = $process.StartTime.ToUniversalTime().ToString("O")
                $matches.Add([pscustomobject]@{
                    processId = [int]$process.Id
                    processStartUtc = $startUtc
                    sessionId = [int]$process.SessionId
                    windowHandle = $hwndValue
                    folderPath = $folderFull
                    executablePath = [System.IO.Path]::GetFullPath($processPath)
                })
            }
            catch {
            }
            finally {
                if ($null -ne $window -and [Runtime.InteropServices.Marshal]::IsComObject($window)) {
                    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($window)
                }
            }
        }
    }
    finally {
        if ($null -ne $windows -and [Runtime.InteropServices.Marshal]::IsComObject($windows)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($windows) }
        if ($null -ne $shell -and [Runtime.InteropServices.Marshal]::IsComObject($shell)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    }

    $unique = @($matches | Sort-Object processId, windowHandle, folderPath -Unique)
    if ($unique.Count -eq 0) { throw "No visible File Explorer window is currently showing the prepared drop-target folder." }
    if ($unique.Count -ne 1) { throw "Multiple visible File Explorer windows match the prepared drop target; close duplicates so the destination is unambiguous." }
    return $unique[0]
}

function Load-Binding {
    param([Parameter(Mandatory = $true)][string]$Path)
    Assert-Sidecar -PayloadPath $Path | Out-Null
    $binding = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$binding.schemaVersion -ne $Script:BindingSchemaVersion) { throw "Unsupported Explorer witness schema '$($binding.schemaVersion)'." }
    if ([bool]$binding.humanGateClaimed) { throw "Explorer witness metadata may not claim a human release gate passed." }
    return $binding
}

function Assert-ExplorerBinding {
    param([Parameter(Mandatory = $true)]$Binding, [Parameter(Mandatory = $true)]$Context, [Parameter(Mandatory = $true)]$CurrentExplorer)
    if ([string]$Binding.packageSha256 -ne [string]$Context.packageSha256) { throw "Explorer witness is bound to a different candidate package." }
    if ([string]$Binding.sessionSha256 -ne [string]$Context.sessionSha256) { throw "Explorer witness is bound to different QA session metadata." }
    if (-not [string]::Equals([System.IO.Path]::GetFullPath([string]$Binding.dropTarget), [System.IO.Path]::GetFullPath([string]$Context.dropTarget), [StringComparison]::OrdinalIgnoreCase)) { throw "Explorer witness is bound to a different drop target." }
    if ([int]$Binding.explorer.processId -ne [int]$CurrentExplorer.processId) { throw "File Explorer process identity changed after capture." }
    if ([string]$Binding.explorer.processStartUtc -ne [string]$CurrentExplorer.processStartUtc) { throw "File Explorer process start time changed after capture." }
    if ([int64]$Binding.explorer.windowHandle -ne [int64]$CurrentExplorer.windowHandle) { throw "File Explorer destination window changed after capture." }
    if (-not [string]::Equals([System.IO.Path]::GetFullPath([string]$Binding.explorer.folderPath), [System.IO.Path]::GetFullPath([string]$CurrentExplorer.folderPath), [StringComparison]::OrdinalIgnoreCase)) { throw "File Explorer destination folder changed after capture." }
    if ([int]$Binding.explorer.sessionId -ne [int]$CurrentExplorer.sessionId) { throw "File Explorer moved to a different Windows session." }
}

function Invoke-Capture {
    if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required for -Mode capture." }
    $context = Get-WorkspaceContext -Workspace $WorkspacePath
    $environment = Get-EnvironmentSnapshot
    Assert-InteractiveSessionMatches -EnvironmentInfo $environment -Session $context.session
    $path = Resolve-BindingPath -Workspace $context.workspace
    if ((Test-Path -LiteralPath $path) -and -not $Force) { throw "Explorer witness already exists: $path. Use -Force only when deliberately starting a fresh observation." }
    $explorer = Get-ExplorerDropTargetWindow -DropTarget $context.dropTarget -ExpectedSessionId ([int]$environment.sessionId)
    $binding = [ordered]@{
        schemaVersion = $Script:BindingSchemaVersion
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        packageSha256 = $context.packageSha256
        sessionSha256 = $context.sessionSha256
        dropTarget = $context.dropTarget
        environment = $environment
        explorer = $explorer
        humanGateClaimed = $false
        note = "Objective File Explorer destination binding only; the real drag gesture, Copy semantics and source preservation still require explicit human confirmation through the packaged QA tool."
    }
    $saved = Save-JsonAtomic -Value $binding -Path $path
    Write-Host "Explorer destination witness captured."
    Write-Host "Explorer PID: $($explorer.processId)"
    Write-Host "Explorer window handle: $($explorer.windowHandle)"
    Write-Host "Explorer folder: $($explorer.folderPath)"
    Write-Host "Witness SHA-256: $($saved.sha256)"
    Write-Host "Human gate claimed: false"
}

function Invoke-Verify {
    if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required for -Mode verify." }
    $context = Get-WorkspaceContext -Workspace $WorkspacePath
    $environment = Get-EnvironmentSnapshot
    Assert-InteractiveSessionMatches -EnvironmentInfo $environment -Session $context.session
    $path = Resolve-BindingPath -Workspace $context.workspace
    $bindingHash = Assert-Sidecar -PayloadPath $path
    $binding = Load-Binding -Path $path
    $explorer = Get-ExplorerDropTargetWindow -DropTarget $context.dropTarget -ExpectedSessionId ([int]$environment.sessionId)
    Assert-ExplorerBinding -Binding $binding -Context $context -CurrentExplorer $explorer
    Write-Host "Explorer destination witness verifies against the same candidate, prepared QA session, Explorer process and visible destination window."
    Write-Host "Explorer PID: $($explorer.processId)"
    Write-Host "Explorer process start UTC: $($explorer.processStartUtc)"
    Write-Host "Explorer window handle: $($explorer.windowHandle)"
    Write-Host "Witness SHA-256: $bindingHash"
    Write-Host "Human gate claimed: false"
}

function Invoke-Status {
    if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required for -Mode status." }
    $context = Get-WorkspaceContext -Workspace $WorkspacePath
    $path = Resolve-BindingPath -Workspace $context.workspace
    $hash = Assert-Sidecar -PayloadPath $path
    $binding = Load-Binding -Path $path
    Write-Host "Explorer witness: $path"
    Write-Host "Witness SHA-256: $hash"
    Write-Host "Package SHA-256: $($binding.packageSha256)"
    Write-Host "Explorer PID: $($binding.explorer.processId)"
    Write-Host "Explorer process start UTC: $($binding.explorer.processStartUtc)"
    Write-Host "Explorer window handle: $($binding.explorer.windowHandle)"
    Write-Host "Human gate claimed: $($binding.humanGateClaimed)"
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-explorer-witness-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $payload = Join-Path $tempRoot "payload.json"
        $saved = Save-JsonAtomic -Value ([ordered]@{ schemaVersion = 1; humanGateClaimed = $false }) -Path $payload
        if ((Assert-Sidecar -PayloadPath $saved.path) -ne $saved.sha256) { throw "Self-test failed: sidecar did not round-trip." }
        Add-Content -LiteralPath $saved.path -Value "tamper"
        $tamperRejected = $false
        try { Assert-Sidecar -PayloadPath $saved.path | Out-Null } catch { $tamperRejected = $true }
        if (-not $tamperRejected) { throw "Self-test failed: tampered payload was accepted." }

        $inside = Join-Path $tempRoot "child\binding.json"
        if (-not (Test-PathInside -Child $inside -Parent $tempRoot)) { throw "Self-test failed: contained path was rejected." }
        $outside = Join-Path ([System.IO.Path]::GetTempPath()) ("outside-" + [guid]::NewGuid().ToString("N") + ".json")
        if (Test-PathInside -Child $outside -Parent $tempRoot) { throw "Self-test failed: external path was accepted." }

        $context = [pscustomobject]@{ packageSha256 = ("a" * 64); sessionSha256 = ("b" * 64); dropTarget = [System.IO.Path]::GetFullPath((Join-Path $tempRoot "drop")) }
        $explorer = [pscustomobject]@{ processId = 42; processStartUtc = "2026-09-19T00:00:00.0000000Z"; sessionId = 7; windowHandle = [int64]1234; folderPath = $context.dropTarget; executablePath = "C:\Windows\explorer.exe" }
        $binding = [pscustomobject]@{ packageSha256 = $context.packageSha256; sessionSha256 = $context.sessionSha256; dropTarget = $context.dropTarget; explorer = $explorer }
        Assert-ExplorerBinding -Binding $binding -Context $context -CurrentExplorer $explorer

        $rebound = [pscustomobject]@{ processId = 42; processStartUtc = "2026-09-19T00:00:01.0000000Z"; sessionId = 7; windowHandle = [int64]1234; folderPath = $context.dropTarget; executablePath = "C:\Windows\explorer.exe" }
        $rebindRejected = $false
        try { Assert-ExplorerBinding -Binding $binding -Context $context -CurrentExplorer $rebound } catch { $rebindRejected = $true }
        if (-not $rebindRejected) { throw "Self-test failed: Explorer process rebinding was accepted." }

        $otherWindow = [pscustomobject]@{ processId = 42; processStartUtc = $explorer.processStartUtc; sessionId = 7; windowHandle = [int64]4321; folderPath = $context.dropTarget; executablePath = "C:\Windows\explorer.exe" }
        $windowRejected = $false
        try { Assert-ExplorerBinding -Binding $binding -Context $context -CurrentExplorer $otherWindow } catch { $windowRejected = $true }
        if (-not $windowRejected) { throw "Self-test failed: Explorer window rebinding was accepted." }

        Write-Host "Dragon DiskForge Explorer witness self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

switch ($Mode) {
    "capture" { Invoke-Capture; break }
    "verify" { Invoke-Verify; break }
    "status" { Invoke-Status; break }
    "self-test" { Invoke-SelfTest; break }
    default { throw "Unsupported mode '$Mode'." }
}
