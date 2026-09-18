[CmdletBinding()]
param(
    [ValidateSet("verify", "self-test")]
    [string]$Mode = "verify",

    [string]$WorkspacePath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:SessionFileName = "beta-qa-session.json"
$Script:ExpectedSessionSchema = 1

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Get-NormalizedFullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $rootFull = Get-NormalizedFullPath -Path $Root
    $pathFull = Get-NormalizedFullPath -Path $Path
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $rootPrefix = $rootFull.TrimEnd([char[]]@('\', '/')) + $separator

    if ([string]::Equals($rootFull, $pathFull, [StringComparison]::OrdinalIgnoreCase) -or
        -not $pathFull.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Context is not a contained path below the prepared beta-QA root."
    }

    return $pathFull
}

function Assert-Sha256Text {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Context is not a valid SHA-256 digest."
    }
    return $Value.ToLowerInvariant()
}

function Get-IsElevated {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        return $null
    }

    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $null
    }
}

function Get-UacEnabled {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        return $null
    }

    try {
        $value = Get-ItemPropertyValue -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name EnableLUA -ErrorAction Stop
        return ([int]$value -ne 0)
    }
    catch {
        return $null
    }
}

function Get-LiveEnvironment {
    $sessionId = -1
    try {
        $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    }
    catch {
        $sessionId = -1
    }

    $build = ""
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        try {
            $build = [string](Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction Stop).CurrentBuildNumber
        }
        catch {
            $build = [Environment]::OSVersion.Version.Build.ToString()
        }
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

function Read-Session {
    param([Parameter(Mandatory = $true)][string]$Workspace)

    if (-not (Test-Path -LiteralPath $Workspace -PathType Container)) {
        throw "Prepared beta-QA workspace is missing: $Workspace"
    }

    $workspaceFull = (Resolve-Path -LiteralPath $Workspace).Path
    $sessionPath = Join-Path $workspaceFull $Script:SessionFileName
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) {
        throw "Prepared beta-QA session metadata is missing: $sessionPath"
    }

    try {
        $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Prepared beta-QA session metadata is not valid JSON: $($_.Exception.Message)"
    }

    if ([int]$session.schemaVersion -ne $Script:ExpectedSessionSchema) {
        throw "Unsupported beta-QA session schema '$($session.schemaVersion)'."
    }

    return [pscustomobject]@{
        workspace = $workspaceFull
        path = $sessionPath
        value = $session
    }
}

function Get-RecordedAppProcessSnapshot {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    if ($ProcessId -le 0) {
        throw "Prepared beta-QA session does not contain a valid application process id."
    }

    try {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        $process.Refresh()
        if ($process.HasExited) {
            throw "Recorded Dragon DiskForge process has already exited."
        }

        $processPath = ""
        try {
            $processPath = [string]$process.Path
        }
        catch {
            throw "Cannot read the recorded Dragon DiskForge process path; live identity cannot be proven."
        }
        if ([string]::IsNullOrWhiteSpace($processPath)) {
            throw "Recorded Dragon DiskForge process path is empty; live identity cannot be proven."
        }

        return [pscustomobject]@{
            exists = $true
            id = $process.Id
            sessionId = $process.SessionId
            path = $processPath
            hasExited = $false
        }
    }
    catch {
        if ($_.Exception.Message -match '^Recorded Dragon DiskForge process' -or
            $_.Exception.Message -match '^Cannot read the recorded Dragon DiskForge process path' -or
            $_.Exception.Message -match '^Recorded Dragon DiskForge process path is empty') {
            throw
        }
        throw "Recorded Dragon DiskForge process id $ProcessId is not running."
    }
}

function Assert-LiveSessionSnapshot {
    param(
        [Parameter(Mandatory = $true)]$Session,
        [Parameter(Mandatory = $true)][string]$Workspace,
        [Parameter(Mandatory = $true)]$CurrentEnvironment,
        [Parameter(Mandatory = $true)]$AppProcess
    )

    $workspaceFull = Get-NormalizedFullPath -Path $Workspace
    $recordedWorkspace = Get-NormalizedFullPath -Path ([string]$Session.workspace)
    if (-not [string]::Equals($workspaceFull, $recordedWorkspace, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Session workspace no longer matches the workspace being verified."
    }

    if (-not [bool]$Session.launchProbePassed -or [bool]$Session.manualGatePassed) {
        throw "Prepared-session metadata has an invalid launch/manual-gate state."
    }

    $extractRoot = Assert-PathWithinRoot -Root $workspaceFull -Path ([string]$Session.extractRoot) -Context "Extracted package path"
    $appPath = Assert-PathWithinRoot -Root $extractRoot -Path ([string]$Session.appPath) -Context "Recorded desktop entry point"
    $dropTarget = Assert-PathWithinRoot -Root $workspaceFull -Path ([string]$Session.dropTarget) -Context "Explorer drop target"

    if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
        throw "Recorded Dragon DiskForge executable is missing: $appPath"
    }
    $expectedAppHash = Assert-Sha256Text -Value ([string]$Session.package.entryPointSha256) -Context "Recorded desktop entry-point digest"
    $actualAppHash = (Get-FileHash -LiteralPath $appPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualAppHash -ne $expectedAppHash) {
        throw "Prepared Dragon DiskForge executable SHA-256 no longer matches the session package identity."
    }

    if (-not (Test-Path -LiteralPath $dropTarget -PathType Container)) {
        throw "Explorer drop target is missing: $dropTarget"
    }
    $dropTargetItem = Get-Item -LiteralPath $dropTarget -Force
    if (($dropTargetItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Explorer drop target became a reparse point; refusing to continue the prepared session."
    }

    if (-not [bool]$Session.environment.userInteractive -or [int]$Session.environment.sessionId -le 0) {
        throw "Prepared session metadata does not describe a normal interactive desktop session."
    }
    if ($null -eq $Session.environment.processElevated -or [bool]$Session.environment.processElevated) {
        throw "Prepared session metadata does not prove an unelevated baseline."
    }
    if ($null -eq $Session.environment.uacEnabled -or -not [bool]$Session.environment.uacEnabled) {
        throw "Prepared session metadata does not prove that UAC was enabled."
    }

    if (-not [bool]$CurrentEnvironment.userInteractive -or [int]$CurrentEnvironment.sessionId -le 0) {
        throw "Current shell is not running in a normal interactive Windows desktop session."
    }
    if ($null -eq $CurrentEnvironment.processElevated -or [bool]$CurrentEnvironment.processElevated) {
        throw "Current shell is elevated or elevation cannot be disproven; human UAC observations must continue unelevated."
    }
    if ($null -eq $CurrentEnvironment.uacEnabled -or -not [bool]$CurrentEnvironment.uacEnabled) {
        throw "Windows UAC is disabled or its enabled state cannot be proven."
    }

    if ([int]$CurrentEnvironment.sessionId -ne [int]$Session.environment.sessionId) {
        throw "Current shell moved to a different Windows desktop session than the prepared beta-QA baseline."
    }
    if ([string]$CurrentEnvironment.osBuild -ne [string]$Session.environment.osBuild) {
        throw "Current Windows build differs from the prepared beta-QA baseline."
    }
    if ([string]$CurrentEnvironment.processArchitecture -ne [string]$Session.environment.processArchitecture) {
        throw "Current PowerShell process architecture differs from the prepared beta-QA baseline."
    }

    if (-not [bool]$AppProcess.exists -or [bool]$AppProcess.hasExited) {
        throw "Recorded Dragon DiskForge process is no longer live."
    }
    if ([int]$AppProcess.id -ne [int]$Session.appProcessId) {
        throw "Live application process id does not match the prepared beta-QA session."
    }
    if ([int]$AppProcess.sessionId -ne [int]$Session.environment.sessionId) {
        throw "Dragon DiskForge is no longer running in the prepared Windows desktop session."
    }

    $liveProcessPath = Get-NormalizedFullPath -Path ([string]$AppProcess.path)
    if (-not [string]::Equals($liveProcessPath, $appPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Live process id is not bound to the recorded Dragon DiskForge executable path."
    }

    return [pscustomobject]@{
        workspace = $workspaceFull
        appProcessId = [int]$AppProcess.id
        appPath = $appPath
        appSha256 = $actualAppHash
        sessionId = [int]$CurrentEnvironment.sessionId
        dropTarget = $dropTarget
    }
}

function Invoke-Verify {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw "Live beta-QA session verification is supported only on Windows."
    }
    if ([string]::IsNullOrWhiteSpace($WorkspacePath)) {
        throw "-WorkspacePath is required for -Mode verify."
    }

    $loaded = Read-Session -Workspace $WorkspacePath
    $session = $loaded.value
    $currentEnvironment = Get-LiveEnvironment
    $appProcess = Get-RecordedAppProcessSnapshot -ProcessId ([int]$session.appProcessId)
    $verified = Assert-LiveSessionSnapshot -Session $session -Workspace $loaded.workspace -CurrentEnvironment $currentEnvironment -AppProcess $appProcess

    Write-Host "Prepared beta-QA live session is VALID for continuing human observation."
    Write-Host "Workspace: $($verified.workspace)"
    Write-Host "Session id: $($verified.sessionId)"
    Write-Host "Dragon DiskForge process id: $($verified.appProcessId)"
    Write-Host "Entry-point SHA-256: $($verified.appSha256)"
    Write-Host "This continuity check does not mark any human beta gate as passed."
}

function New-SelfTestFixture {
    param([Parameter(Mandatory = $true)][string]$Root)

    $workspace = Join-Path $Root "workspace"
    $extractRoot = Join-Path $workspace "package"
    $dropTarget = Join-Path $workspace "explorer-drop-target"
    New-Item -ItemType Directory -Path $extractRoot, $dropTarget -Force | Out-Null

    $appPath = Join-Path $extractRoot "DragonDiskForge.App.exe"
    Write-Utf8NoBom -Path $appPath -Text "live-session-self-test-app"
    $appHash = (Get-FileHash -LiteralPath $appPath -Algorithm SHA256).Hash.ToLowerInvariant()

    $environment = [pscustomobject]@{
        userInteractive = $true
        processElevated = $false
        uacEnabled = $true
        sessionId = 7
        osBuild = "26100"
        processArchitecture = "X64"
    }
    $session = [pscustomobject]@{
        schemaVersion = 1
        package = [pscustomobject]@{
            entryPointSha256 = $appHash
        }
        environment = $environment
        workspace = [System.IO.Path]::GetFullPath($workspace)
        extractRoot = [System.IO.Path]::GetFullPath($extractRoot)
        dropTarget = [System.IO.Path]::GetFullPath($dropTarget)
        appProcessId = 4242
        appPath = [System.IO.Path]::GetFullPath($appPath)
        launchProbePassed = $true
        manualGatePassed = $false
    }
    $process = [pscustomobject]@{
        exists = $true
        id = 4242
        sessionId = 7
        path = [System.IO.Path]::GetFullPath($appPath)
        hasExited = $false
    }

    return [pscustomobject]@{
        workspace = $workspace
        appPath = $appPath
        appContent = "live-session-self-test-app"
        session = $session
        environment = $environment
        process = $process
    }
}

function Assert-SelfTestRejects {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $rejected = $false
    try {
        & $Action
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Self-test failed: $Context did not fail closed."
    }
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-live-session-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    try {
        $fixture = New-SelfTestFixture -Root $tempRoot
        $valid = Assert-LiveSessionSnapshot -Session $fixture.session -Workspace $fixture.workspace -CurrentEnvironment $fixture.environment -AppProcess $fixture.process
        if ($valid.appProcessId -ne 4242 -or [string]::IsNullOrWhiteSpace([string]$valid.appSha256)) {
            throw "Self-test failed: valid continuity snapshot returned incomplete identity."
        }

        $wrongSessionEnvironment = $fixture.environment.PSObject.Copy()
        $wrongSessionEnvironment.sessionId = 8
        Assert-SelfTestRejects -Context "desktop-session mismatch" -Action {
            Assert-LiveSessionSnapshot -Session $fixture.session -Workspace $fixture.workspace -CurrentEnvironment $wrongSessionEnvironment -AppProcess $fixture.process | Out-Null
        }

        $elevatedEnvironment = $fixture.environment.PSObject.Copy()
        $elevatedEnvironment.processElevated = $true
        Assert-SelfTestRejects -Context "elevated continuation shell" -Action {
            Assert-LiveSessionSnapshot -Session $fixture.session -Workspace $fixture.workspace -CurrentEnvironment $elevatedEnvironment -AppProcess $fixture.process | Out-Null
        }

        $uacOffEnvironment = $fixture.environment.PSObject.Copy()
        $uacOffEnvironment.uacEnabled = $false
        Assert-SelfTestRejects -Context "UAC-disabled continuation shell" -Action {
            Assert-LiveSessionSnapshot -Session $fixture.session -Workspace $fixture.workspace -CurrentEnvironment $uacOffEnvironment -AppProcess $fixture.process | Out-Null
        }

        $deadProcess = $fixture.process.PSObject.Copy()
        $deadProcess.hasExited = $true
        Assert-SelfTestRejects -Context "exited candidate process" -Action {
            Assert-LiveSessionSnapshot -Session $fixture.session -Workspace $fixture.workspace -CurrentEnvironment $fixture.environment -AppProcess $deadProcess | Out-Null
        }

        $wrongProcess = $fixture.process.PSObject.Copy()
        $wrongProcess.path = Join-Path $fixture.workspace "not-dragon.exe"
        Assert-SelfTestRejects -Context "process-path rebinding" -Action {
            Assert-LiveSessionSnapshot -Session $fixture.session -Workspace $fixture.workspace -CurrentEnvironment $fixture.environment -AppProcess $wrongProcess | Out-Null
        }

        Write-Utf8NoBom -Path $fixture.appPath -Text "tampered-live-session-app"
        Assert-SelfTestRejects -Context "prepared executable tampering" -Action {
            Assert-LiveSessionSnapshot -Session $fixture.session -Workspace $fixture.workspace -CurrentEnvironment $fixture.environment -AppProcess $fixture.process | Out-Null
        }

        Write-Host "Dragon DiskForge live beta-QA session continuity self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    "verify" { Invoke-Verify; break }
    "self-test" { Invoke-SelfTest; break }
    default { throw "Unsupported mode '$Mode'." }
}
