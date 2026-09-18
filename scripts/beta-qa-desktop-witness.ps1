[CmdletBinding()]
param(
    [ValidateSet("baseline", "observe", "verify", "self-test")]
    [string]$Mode = "verify",
    [string]$WorkspacePath = "",
    [string]$WitnessPath = "",
    [int]$MaxItems = 2048,
    [long]$MaxHashedBytes = 268435456
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:WitnessSchemaVersion = 1
$Script:SessionSchemaVersion = 1
$Script:EvidenceSchemaVersion = 3
$Script:EvidenceGateName = "Dragon DiskForge interactive beta QA"
$Script:SessionFileName = "beta-qa-session.json"
$Script:DefaultWitnessFileName = "beta-qa-desktop-witness.json"

function Write-Utf8NoBom {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Text)
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Get-Sha256Text {
    param([Parameter(Mandatory = $true)][string]$Text)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant() }
    finally { $sha.Dispose() }
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
    $declaredHash = $Matches[1].ToLowerInvariant()
    if ($Matches[2].Trim() -ne [System.IO.Path]::GetFileName($payload)) { throw "SHA-256 sidecar targets the wrong file." }
    $actualHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) { throw "SHA-256 mismatch for '$payload'." }
    return $actualHash
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
    try { return ([int](Get-ItemPropertyValue -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name EnableLUA -ErrorAction Stop) -ne 0) }
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
        osBuild = $build
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        userInteractive = [Environment]::UserInteractive
        processElevated = (Get-IsElevated)
        sessionId = $sessionId
        uacEnabled = (Get-UacEnabled)
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
}

function Assert-InteractiveSessionMatches {
    param([Parameter(Mandatory = $true)]$EnvironmentInfo, [Parameter(Mandatory = $true)]$Session)
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw "Desktop witness modes are supported only on Windows." }
    if (-not [bool]$EnvironmentInfo.userInteractive -or [int]$EnvironmentInfo.sessionId -le 0) { throw "Desktop witness requires an interactive Windows session." }
    if ($null -eq $EnvironmentInfo.processElevated -or [bool]$EnvironmentInfo.processElevated) { throw "Desktop witness must run from the normal unelevated beta-QA session." }
    if ($null -eq $EnvironmentInfo.uacEnabled -or -not [bool]$EnvironmentInfo.uacEnabled) { throw "Desktop witness requires UAC to be enabled." }
    if ([int]$EnvironmentInfo.sessionId -ne [int]$Session.environment.sessionId) { throw "Current session id differs from the prepared beta-QA session." }
    if ([string]$EnvironmentInfo.osBuild -ne [string]$Session.environment.osBuild) { throw "Current Windows build differs from the prepared beta-QA session." }
    if ([string]$EnvironmentInfo.processArchitecture -ne [string]$Session.environment.processArchitecture) { throw "Current process architecture differs from the prepared beta-QA session." }
}

function Get-EvidenceIdentity {
    param([Parameter(Mandatory = $true)]$Evidence, [Parameter(Mandatory = $true)]$Session)
    if ([int]$Evidence.schemaVersion -ne $Script:EvidenceSchemaVersion -or [string]$Evidence.gate -ne $Script:EvidenceGateName) { throw "Manual-QA evidence identity/schema is invalid." }
    if ([string]$Evidence.targetVersion -ne [string]$Session.package.version -or
        [string]$Evidence.package.version -ne [string]$Session.package.version -or
        [string]$Evidence.package.sha256 -ne ([string]$Session.package.sha256).ToLowerInvariant() -or
        [string]$Evidence.package.entryPointSha256 -ne [string]$Session.package.entryPointSha256 -or
        [string]$Evidence.package.betaManualQaEntryPointSha256 -ne [string]$Session.package.qaToolSha256 -or
        [string]$Evidence.createdQaToolSha256 -ne [string]$Session.package.qaToolSha256) {
        throw "Manual-QA evidence is bound to a different candidate identity than the QA session."
    }
    $created = $Evidence.createdEnvironment
    $canonical = @(
        [string]$Evidence.schemaVersion,
        [string]$Evidence.gate,
        [string]$Evidence.targetVersion,
        ([string]$Evidence.package.sha256).ToLowerInvariant(),
        ([string]$Evidence.package.entryPointSha256).ToLowerInvariant(),
        ([string]$Evidence.package.betaManualQaEntryPointSha256).ToLowerInvariant(),
        ([string]$Evidence.createdQaToolSha256).ToLowerInvariant(),
        [string]$Evidence.createdUtc,
        [string]$created.osBuild,
        [string]$created.processArchitecture,
        [string]$created.sessionId,
        [string]$created.userInteractive,
        [string]$created.processElevated,
        [string]$created.uacEnabled
    ) -join "|"
    return Get-Sha256Text -Text $canonical
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

    $dropTarget = [System.IO.Path]::GetFullPath([string]$session.dropTarget)
    if (-not (Test-PathInside -Child $dropTarget -Parent $workspaceFull) -or -not (Test-Path -LiteralPath $dropTarget -PathType Container)) { throw "Explorer drop target is missing or escapes the QA workspace." }
    $evidencePath = [System.IO.Path]::GetFullPath([string]$session.evidencePath)
    if (-not (Test-PathInside -Child $evidencePath -Parent $workspaceFull) -or -not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { throw "Manual-QA evidence is missing or escapes the QA workspace." }
    $evidenceHash = Assert-Sidecar -PayloadPath $evidencePath
    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json

    $packagePath = (Resolve-Path -LiteralPath ([string]$session.package.path)).Path
    $packageSidecar = (Resolve-Path -LiteralPath ([string]$session.package.checksumFile)).Path
    $packageHash = Assert-Sidecar -PayloadPath $packagePath -SidecarPath $packageSidecar
    if ($packageHash -ne ([string]$session.package.sha256).ToLowerInvariant()) { throw "Candidate package no longer matches session metadata." }
    $evidenceIdentity = Get-EvidenceIdentity -Evidence $evidence -Session $session

    return [pscustomobject]@{
        workspace = $workspaceFull
        sessionPath = $sessionPath
        sessionSha256 = (Get-FileHash -LiteralPath $sessionPath -Algorithm SHA256).Hash.ToLowerInvariant()
        session = $session
        dropTarget = $dropTarget
        evidencePath = $evidencePath
        evidenceSha256 = $evidenceHash
        evidenceIdentitySha256 = $evidenceIdentity
        packageSha256 = $packageHash
    }
}

function Ensure-WindowInterop {
    if (-not ("DragonDiskForge.DesktopWitness.WindowProbe" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace DragonDiskForge.DesktopWitness {
    public static class WindowProbe {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        public static IntPtr FindVisibleTopLevelWindow(uint targetProcessId) {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == targetProcessId && IsWindowVisible(hWnd)) { found = hWnd; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
"@
    }
}

function Get-VisibleAppWindow {
    param([Parameter(Mandatory = $true)]$Session)
    Ensure-WindowInterop
    $processId = [int]$Session.appProcessId
    if ($processId -le 0) { throw "QA session does not contain a valid application process id." }
    try { $process = Get-Process -Id $processId -ErrorAction Stop } catch { throw "Recorded Dragon DiskForge process is not running." }
    $actualPath = ""
    try { $actualPath = [string]$process.Path } catch { $actualPath = "" }
    if ([string]::IsNullOrWhiteSpace($actualPath) -or -not [string]::Equals([System.IO.Path]::GetFullPath($actualPath), [System.IO.Path]::GetFullPath([string]$Session.appPath), [StringComparison]::OrdinalIgnoreCase)) { throw "Recorded process id no longer resolves to the prepared Dragon DiskForge executable." }
    $handle = [DragonDiskForge.DesktopWitness.WindowProbe]::FindVisibleTopLevelWindow([uint32]$processId)
    if ($handle -eq [IntPtr]::Zero) { throw "Dragon DiskForge is running but no visible top-level window is currently observable." }
    return [pscustomobject]@{
        processId = $processId
        processStartUtc = $process.StartTime.ToUniversalTime().ToString("O")
        windowHandle = $handle.ToInt64()
        visible = $true
    }
}

function Get-DropSnapshot {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][int]$ItemLimit, [Parameter(Mandatory = $true)][long]$ByteLimit)
    if ($ItemLimit -lt 1 -or $ItemLimit -gt 10000) { throw "MaxItems must be between 1 and 10000." }
    if ($ByteLimit -lt 0 -or $ByteLimit -gt 2147483648) { throw "MaxHashedBytes must be between 0 and 2147483648." }
    $rootFull = (Resolve-Path -LiteralPath $Root).Path.TrimEnd([char]92, [char]47)
    $items = @(Get-ChildItem -LiteralPath $rootFull -Recurse -Force -ErrorAction Stop | Sort-Object FullName)
    if ($items.Count -gt $ItemLimit) { throw "Explorer drop target contains $($items.Count) items; witness limit is $ItemLimit." }

    $records = @()
    [long]$hashedBytes = 0
    foreach ($item in $items) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Explorer drop target contains a reparse point; refusing ambiguous witness input." }
        $full = [System.IO.Path]::GetFullPath($item.FullName)
        if (-not (Test-PathInside -Child $full -Parent $rootFull)) { throw "Explorer drop target traversal escaped the prepared root." }
        $relative = $full.Substring($rootFull.Length).TrimStart([char]92, [char]47).Replace([char]92, [char]47)
        $pathHash = Get-Sha256Text -Text $relative
        if ($item.PSIsContainer) {
            $records += [pscustomobject]@{ pathSha256 = $pathHash; kind = "directory"; length = [int64]0; contentSha256 = $null }
        }
        else {
            $length = [int64]$item.Length
            if (($hashedBytes + $length) -gt $ByteLimit) { throw "Explorer drop target exceeds the bounded hash budget of $ByteLimit bytes." }
            $records += [pscustomobject]@{ pathSha256 = $pathHash; kind = "file"; length = $length; contentSha256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant() }
            $hashedBytes += $length
        }
    }
    $canonical = @($records | ForEach-Object { "{0}|{1}|{2}|{3}" -f $_.pathSha256, $_.kind, $_.length, ([string]$_.contentSha256) }) -join "`n"
    return [pscustomobject]@{
        itemCount = $records.Count
        fileCount = @($records | Where-Object { $_.kind -eq "file" }).Count
        directoryCount = @($records | Where-Object { $_.kind -eq "directory" }).Count
        hashedBytes = $hashedBytes
        treeSha256 = (Get-Sha256Text -Text $canonical)
        records = $records
    }
}

function Resolve-WitnessPath {
    param([Parameter(Mandatory = $true)][string]$Workspace)
    $path = if ([string]::IsNullOrWhiteSpace($WitnessPath)) { Join-Path $Workspace $Script:DefaultWitnessFileName } else { [System.IO.Path]::GetFullPath($WitnessPath) }
    if (-not (Test-PathInside -Child $path -Parent $Workspace)) { throw "Witness path must remain inside the disposable QA workspace." }
    return $path
}

function Load-Witness {
    param([Parameter(Mandatory = $true)][string]$Path)
    Assert-Sidecar -PayloadPath $Path | Out-Null
    $value = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$value.schemaVersion -ne $Script:WitnessSchemaVersion) { throw "Unsupported desktop witness schema '$($value.schemaVersion)'." }
    if ([bool]$value.humanGateClaimed) { throw "Desktop witness metadata may not claim a human release gate passed." }
    return $value
}

function Assert-WitnessBinding {
    param([Parameter(Mandatory = $true)]$Witness, [Parameter(Mandatory = $true)]$Context, [Parameter(Mandatory = $true)]$Window)
    if ([string]$Witness.packageSha256 -ne [string]$Context.packageSha256) { throw "Desktop witness is bound to a different candidate package." }
    if ([string]$Witness.sessionSha256 -ne [string]$Context.sessionSha256) { throw "Desktop witness is bound to different QA session metadata." }
    if ([string]$Witness.evidenceIdentitySha256 -ne [string]$Context.evidenceIdentitySha256) { throw "Desktop witness is bound to a different immutable manual-QA evidence identity." }
    if (-not [string]::Equals([System.IO.Path]::GetFullPath([string]$Witness.dropTarget), [System.IO.Path]::GetFullPath([string]$Context.dropTarget), [StringComparison]::OrdinalIgnoreCase)) { throw "Desktop witness is bound to a different Explorer drop target." }
    if ([int]$Witness.app.processId -ne [int]$Window.processId -or [string]$Witness.app.processStartUtc -ne [string]$Window.processStartUtc) { throw "Dragon DiskForge process identity changed after the witness baseline." }
}

function Invoke-Baseline {
    if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required for -Mode baseline." }
    $context = Get-WorkspaceContext -Workspace $WorkspacePath
    $environment = Get-EnvironmentSnapshot
    Assert-InteractiveSessionMatches -EnvironmentInfo $environment -Session $context.session
    $window = Get-VisibleAppWindow -Session $context.session
    $snapshot = Get-DropSnapshot -Root $context.dropTarget -ItemLimit $MaxItems -ByteLimit $MaxHashedBytes
    if ([int]$snapshot.itemCount -ne 0) { throw "Explorer drop target must be empty when the witness baseline is created." }
    $witness = [ordered]@{
        schemaVersion = $Script:WitnessSchemaVersion
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        packageSha256 = $context.packageSha256
        sessionSha256 = $context.sessionSha256
        evidenceIdentitySha256 = $context.evidenceIdentitySha256
        evidenceSha256AtBaseline = $context.evidenceSha256
        environment = $environment
        app = $window
        dropTarget = $context.dropTarget
        baseline = [ordered]@{ itemCount = 0; treeSha256 = $snapshot.treeSha256 }
        observation = $null
        humanGateClaimed = $false
        note = "Objective destination/window witness only; the real cross-process gesture, Copy semantics and source preservation still require explicit human confirmation through the packaged QA tool."
    }
    $saved = Save-JsonAtomic -Value $witness -Path (Resolve-WitnessPath -Workspace $context.workspace)
    Write-Host "Desktop witness baseline created against a visible Dragon DiskForge window and empty drop target."
    Write-Host "Witness: $($saved.path)"
    Write-Host "This does not complete any manual beta gate."
}

function Invoke-Observe {
    if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required for -Mode observe." }
    $context = Get-WorkspaceContext -Workspace $WorkspacePath
    $environment = Get-EnvironmentSnapshot
    Assert-InteractiveSessionMatches -EnvironmentInfo $environment -Session $context.session
    $window = Get-VisibleAppWindow -Session $context.session
    $path = Resolve-WitnessPath -Workspace $context.workspace
    $witness = Load-Witness -Path $path
    Assert-WitnessBinding -Witness $witness -Context $context -Window $window
    if ($null -ne $witness.observation) { throw "Desktop witness already contains an observation; create a fresh baseline for another gesture." }
    $snapshot = Get-DropSnapshot -Root $context.dropTarget -ItemLimit $MaxItems -ByteLimit $MaxHashedBytes
    if ([int]$snapshot.itemCount -le 0) { throw "No destination items are present after the gesture; refusing positive witness evidence." }
    $witness.observation = [pscustomobject]@{
        observedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        evidenceSha256AtObservation = $context.evidenceSha256
        appProcessId = $window.processId
        appProcessStartUtc = $window.processStartUtc
        appWindowHandle = $window.windowHandle
        itemCount = $snapshot.itemCount
        fileCount = $snapshot.fileCount
        directoryCount = $snapshot.directoryCount
        hashedBytes = $snapshot.hashedBytes
        treeSha256 = $snapshot.treeSha256
        records = $snapshot.records
    }
    $saved = Save-JsonAtomic -Value $witness -Path $path
    Write-Host "Desktop witness observation captured: $($snapshot.itemCount) destination item(s), $($snapshot.hashedBytes) hashed byte(s)."
    Write-Host "Destination tree SHA-256: $($snapshot.treeSha256)"
    Write-Host "Witness SHA-256: $($saved.sha256)"
    Write-Host "Human confirmation is still required for the actual Explorer drag gesture, Copy semantics and source preservation."
}

function Invoke-Verify {
    if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required for -Mode verify." }
    $context = Get-WorkspaceContext -Workspace $WorkspacePath
    $environment = Get-EnvironmentSnapshot
    Assert-InteractiveSessionMatches -EnvironmentInfo $environment -Session $context.session
    $window = Get-VisibleAppWindow -Session $context.session
    $path = Resolve-WitnessPath -Workspace $context.workspace
    $witnessHash = Assert-Sidecar -PayloadPath $path
    $witness = Load-Witness -Path $path
    Assert-WitnessBinding -Witness $witness -Context $context -Window $window
    if ($null -eq $witness.observation) { throw "Desktop witness has no captured observation." }
    $snapshot = Get-DropSnapshot -Root $context.dropTarget -ItemLimit $MaxItems -ByteLimit $MaxHashedBytes
    if ([string]$snapshot.treeSha256 -ne [string]$witness.observation.treeSha256 -or [int]$snapshot.itemCount -ne [int]$witness.observation.itemCount -or [long]$snapshot.hashedBytes -ne [long]$witness.observation.hashedBytes) { throw "Explorer drop target changed after the recorded observation." }
    Write-Host "Desktop witness verifies against the same candidate, prepared session, immutable QA evidence identity, app process and destination tree."
    Write-Host "Visible Dragon DiskForge window: true"
    Write-Host "Destination tree SHA-256: $($snapshot.treeSha256)"
    Write-Host "Witness SHA-256: $witnessHash"
    Write-Host "Human gate claimed: false"
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-desktop-witness-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $drop = Join-Path $tempRoot "drop"
        New-Item -ItemType Directory -Path $drop -Force | Out-Null
        $empty = Get-DropSnapshot -Root $drop -ItemLimit 16 -ByteLimit 1048576
        if ($empty.itemCount -ne 0) { throw "Self-test failed: empty drop target is not empty." }
        Write-Utf8NoBom -Path (Join-Path $drop "sample.txt") -Text "Dragon DiskForge desktop witness"
        New-Item -ItemType Directory -Path (Join-Path $drop "folder") -Force | Out-Null
        Write-Utf8NoBom -Path (Join-Path $drop "folder\nested.bin") -Text "nested"
        $snapshot = Get-DropSnapshot -Root $drop -ItemLimit 16 -ByteLimit 1048576
        if ($snapshot.itemCount -ne 3 -or $snapshot.fileCount -ne 2 -or $snapshot.directoryCount -ne 1 -or $snapshot.hashedBytes -le 0 -or [string]$snapshot.treeSha256 -notmatch '^[0-9a-f]{64}$') { throw "Self-test failed: bounded destination snapshot is invalid." }

        $session = [pscustomobject]@{ package = [pscustomobject]@{ version = "0.5.0-beta.1"; sha256 = ("a" * 64); entryPointSha256 = ("b" * 64); qaToolSha256 = ("c" * 64) } }
        $evidence = [pscustomobject]@{
            schemaVersion = 3; gate = $Script:EvidenceGateName; targetVersion = "0.5.0-beta.1"; createdUtc = "2026-09-18T00:00:00Z"; createdQaToolSha256 = ("c" * 64)
            package = [pscustomobject]@{ version = "0.5.0-beta.1"; sha256 = ("a" * 64); entryPointSha256 = ("b" * 64); betaManualQaEntryPointSha256 = ("c" * 64) }
            createdEnvironment = [pscustomobject]@{ osBuild = "26100"; processArchitecture = "X64"; sessionId = 1; userInteractive = $true; processElevated = $false; uacEnabled = $true }
            checks = @([pscustomobject]@{ id = "drag.file-explorer-copy"; status = "pending" })
        }
        $identityBefore = Get-EvidenceIdentity -Evidence $evidence -Session $session
        $evidence.checks[0].status = "pass"
        $identityAfterMutableRecord = Get-EvidenceIdentity -Evidence $evidence -Session $session
        if ($identityBefore -ne $identityAfterMutableRecord) { throw "Self-test failed: mutable QA observations changed the immutable evidence identity." }
        $evidence.package.sha256 = ("d" * 64)
        $identityMutationRejected = $false
        try { Get-EvidenceIdentity -Evidence $evidence -Session $session | Out-Null } catch { $identityMutationRejected = $true }
        if (-not $identityMutationRejected) { throw "Self-test failed: candidate identity mutation was accepted." }

        $witnessPath = Join-Path $tempRoot "witness.json"
        $saved = Save-JsonAtomic -Value ([ordered]@{ schemaVersion = 1; humanGateClaimed = $false; observation = [ordered]@{ treeSha256 = $snapshot.treeSha256 } }) -Path $witnessPath
        if ((Assert-Sidecar -PayloadPath $saved.path) -ne $saved.sha256) { throw "Self-test failed: witness sidecar did not round-trip." }
        Add-Content -LiteralPath $saved.path -Value "tamper"
        $tamperRejected = $false
        try { Assert-Sidecar -PayloadPath $saved.path | Out-Null } catch { $tamperRejected = $true }
        if (-not $tamperRejected) { throw "Self-test failed: tampered witness did not fail closed." }
        $limitRejected = $false
        try { Get-DropSnapshot -Root $drop -ItemLimit 2 -ByteLimit 1048576 | Out-Null } catch { $limitRejected = $true }
        if (-not $limitRejected) { throw "Self-test failed: item limit was not enforced." }
        $outside = Join-Path ([System.IO.Path]::GetTempPath()) "outside-witness.json"
        if (Test-PathInside -Child $outside -Parent $tempRoot) { throw "Self-test failed: path containment accepted an external path." }
        Write-Host "Dragon DiskForge desktop witness self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

switch ($Mode) {
    "baseline" { Invoke-Baseline; break }
    "observe" { Invoke-Observe; break }
    "verify" { Invoke-Verify; break }
    "self-test" { Invoke-SelfTest; break }
    default { throw "Unsupported mode '$Mode'." }
}
