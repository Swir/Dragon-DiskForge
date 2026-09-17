[CmdletBinding()]
param(
    [ValidateSet("new", "record", "list", "verify", "self-test")]
    [string]$Mode = "verify",

    [string]$EvidencePath = "artifacts/manual-qa/beta-manual-qa.json",
    [string]$PackagePath = "",
    [string]$ChecksumFile = "",
    [string]$Check = "",
    [string]$Result = "",
    [switch]$HumanConfirmed,
    [string]$Note = "",
    [string]$ExpectedVersion = "0.5.0-beta.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:SchemaVersion = 3
$Script:GateName = "Dragon DiskForge interactive beta QA"
$Script:ScriptPath = $PSCommandPath

function Get-RequiredChecks {
    return @(
        [pscustomobject]@{ id = "desktop.clean-launch"; group = "desktop"; description = "Clean supported Windows desktop launch succeeds from the packaged app without developer tooling." },
        [pscustomobject]@{ id = "desktop.basic-regression"; group = "desktop"; description = "Open, mount, explore, verify and analyze complete successfully in the packaged WinUI app." },
        [pscustomobject]@{ id = "uac.iso-no-prompt"; group = "uac"; description = "ISO mount/unmount completes without an unnecessary administrator prompt." },
        [pscustomobject]@{ id = "uac.vhd-cancel"; group = "uac"; description = "Cancelling VHD elevation leaves the image detached and reports cancellation cleanly." },
        [pscustomobject]@{ id = "uac.vhd-approve-readonly"; group = "uac"; description = "Approving VHD elevation mounts read-only and refreshes the real mounted state." },
        [pscustomobject]@{ id = "uac.vhdx-cancel"; group = "uac"; description = "Cancelling VHDX elevation leaves the image detached and reports cancellation cleanly." },
        [pscustomobject]@{ id = "uac.vhdx-approve-readonly"; group = "uac"; description = "Approving VHDX elevation mounts read-only and refreshes the real mounted state." },
        [pscustomobject]@{ id = "uac.unmount-refresh"; group = "uac"; description = "VHD/VHDX unmount requests elevation only when required and refreshes to the real detached state." },
        [pscustomobject]@{ id = "drag.file-explorer-copy"; group = "drag"; description = "A file dragged from Dragon Explorer to Windows Explorer lands as Copy and source remains unchanged." },
        [pscustomobject]@{ id = "drag.folder-explorer-copy"; group = "drag"; description = "A folder dragged from Dragon Explorer to Windows Explorer lands as Copy and source remains unchanged." },
        [pscustomobject]@{ id = "drag.desktop-copy"; group = "drag"; description = "Drag-out to the Windows desktop behaves as Copy and preserves the mounted source." },
        [pscustomobject]@{ id = "drag.cancel-no-mutation"; group = "drag"; description = "Cancelling an in-progress drag creates no destination item and no false success state." },
        [pscustomobject]@{ id = "drag.reparse-blocked"; group = "drag"; description = "A reparse point or junction source is blocked from drag-out." },
        [pscustomobject]@{ id = "drag.stale-blocked"; group = "drag"; description = "A stale or invalidated source cannot be dragged successfully after state refresh." }
    )
}

function Test-IsElevated {
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

function Get-SessionEnvironment {
    $build = ""
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        try {
            $build = [string](Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction Stop).CurrentBuildNumber
        }
        catch {
            $build = [Environment]::OSVersion.Version.Build.ToString()
        }
    }

    $sessionId = -1
    try {
        $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    }
    catch {
        $sessionId = -1
    }

    return [pscustomobject]@{
        osVersion = [Environment]::OSVersion.VersionString
        osBuild = $build
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        userInteractive = [Environment]::UserInteractive
        processElevated = (Test-IsElevated)
        sessionId = $sessionId
        uacEnabled = (Get-UacEnabled)
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
}

function Assert-InteractiveUnelevated {
    param(
        [Parameter(Mandatory = $true)]$EnvironmentInfo,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if (-not [bool]$EnvironmentInfo.userInteractive) {
        throw "$Context requires an interactive Windows desktop session."
    }
    if ([int]$EnvironmentInfo.sessionId -le 0) {
        throw "$Context requires a non-service interactive Windows session (session id must be greater than zero)."
    }
    if ($null -eq $EnvironmentInfo.processElevated) {
        throw "$Context cannot determine whether the current process is elevated; refusing to record release evidence."
    }
    if ([bool]$EnvironmentInfo.processElevated) {
        throw "$Context must run from a normal unelevated session so UAC and Explorer boundaries remain observable."
    }
    if ($null -eq $EnvironmentInfo.uacEnabled) {
        throw "$Context cannot determine whether Windows UAC (EnableLUA) is enabled; refusing to record release evidence."
    }
    if (-not [bool]$EnvironmentInfo.uacEnabled) {
        throw "$Context cannot prove the UAC gate while Windows UAC (EnableLUA) is disabled."
    }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Save-Evidence {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $tempPath = "$fullPath.tmp.$([guid]::NewGuid().ToString('N'))"
    try {
        $json = $Evidence | ConvertTo-Json -Depth 12
        Write-Utf8NoBom -Path $tempPath -Text ($json + [Environment]::NewLine)
        Move-Item -LiteralPath $tempPath -Destination $fullPath -Force

        $hash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $sidecar = "$fullPath.sha256"
        Write-Utf8NoBom -Path $sidecar -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($fullPath), [Environment]::NewLine)
        return [pscustomobject]@{ path = $fullPath; sha256 = $hash; sidecar = $sidecar }
    }
    finally {
        if (Test-Path $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Load-Evidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = (Resolve-Path -LiteralPath $Path).Path
    return (Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json)
}

function Assert-EvidenceSidecar {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = (Resolve-Path -LiteralPath $Path).Path
    $sidecar = "$fullPath.sha256"
    if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) {
        throw "Evidence SHA-256 sidecar is missing: $sidecar"
    }

    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "Evidence SHA-256 sidecar has an invalid format."
    }

    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    if ($declaredName -ne [System.IO.Path]::GetFileName($fullPath)) {
        throw "Evidence SHA-256 sidecar targets '$declaredName' instead of '$([System.IO.Path]::GetFileName($fullPath))'."
    }

    $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "Evidence SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'."
    }

    return $actualHash
}

function Get-RunningToolSha256 {
    if ([string]::IsNullOrWhiteSpace([string]$Script:ScriptPath)) {
        return $null
    }
    if (-not (Test-Path -LiteralPath $Script:ScriptPath -PathType Leaf)) {
        return $null
    }

    try {
        return (Get-FileHash -LiteralPath $Script:ScriptPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    catch {
        return $null
    }
}

function Assert-RunningToolMatchesPackage {
    param(
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [string]$CurrentToolSha256 = ""
    )

    $actual = $CurrentToolSha256
    if ([string]::IsNullOrWhiteSpace($actual)) {
        $actual = Get-RunningToolSha256
    }
    if ([string]::IsNullOrWhiteSpace([string]$actual)) {
        throw "Cannot determine the SHA-256 of the beta manual-QA script that is currently running."
    }

    $actual = $actual.ToLowerInvariant()
    $expected = ([string]$PackageIdentity.betaManualQaEntryPointSha256).ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($expected) -or $expected -notmatch '^[0-9a-f]{64}$') {
        throw "The candidate package does not expose a valid beta manual-QA tool identity."
    }
    if ($actual -ne $expected) {
        throw "The running beta manual-QA script SHA-256 does not match the exact tool packaged in this release candidate. Run the tools/beta-manual-qa.ps1 copy extracted from the verified candidate ZIP."
    }

    return $actual
}

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [string]$SidecarPath = ""
    )

    $zip = (Resolve-Path -LiteralPath $ZipPath).Path
    if ([string]::IsNullOrWhiteSpace($SidecarPath)) {
        $SidecarPath = "$zip.sha256"
    }
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path

    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "Package checksum sidecar has an invalid format."
    }
    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    if ($declaredName -ne [System.IO.Path]::GetFileName($zip)) {
        throw "Package checksum sidecar targets '$declaredName' instead of '$([System.IO.Path]::GetFileName($zip))'."
    }

    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($zipHash -ne $declaredHash) {
        throw "Package ZIP SHA-256 mismatch. Expected '$declaredHash', actual '$zipHash'."
    }

    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $zip -DestinationPath $workspace -Force
        $manifestPath = Join-Path $workspace "package-manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Package manifest is missing."
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -lt 5) {
            throw "Package manifest schema '$($manifest.schemaVersion)' predates the beta-manual-QA contract."
        }
        if ([string]$manifest.product -ne "Dragon DiskForge") {
            throw "Unexpected package product '$($manifest.product)'."
        }
        if ([string]$manifest.architecture -ne "x64") {
            throw "Unexpected package architecture '$($manifest.architecture)'."
        }

        $entryPoint = Join-Path $workspace ([string]$manifest.entryPoint)
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
            throw "Package entry point is missing: $entryPoint"
        }
        $entryHash = (Get-FileHash -LiteralPath $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($entryHash -ne ([string]$manifest.entryPointSha256).ToLowerInvariant()) {
            throw "Package entry-point SHA-256 does not match the manifest."
        }

        $qaRelative = ([string]$manifest.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $qaPath = Join-Path $workspace $qaRelative
        if (-not (Test-Path -LiteralPath $qaPath -PathType Leaf)) {
            throw "Packaged beta manual-QA tool is missing: $qaRelative"
        }
        $qaHash = (Get-FileHash -LiteralPath $qaPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($qaHash -ne ([string]$manifest.betaManualQaEntryPointSha256).ToLowerInvariant()) {
            throw "Packaged beta manual-QA tool SHA-256 does not match the manifest."
        }

        return [pscustomobject]@{
            fileName = [System.IO.Path]::GetFileName($zip)
            sha256 = $zipHash
            version = [string]$manifest.version
            architecture = [string]$manifest.architecture
            entryPointSha256 = $entryHash
            betaManualQaEntryPointSha256 = $qaHash
        }
    }
    finally {
        if (Test-Path $workspace) {
            Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Assert-PackageMatchesEvidence {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ([string]$PackageIdentity.version -ne $Version) {
        throw "Package version '$($PackageIdentity.version)' does not match required version '$Version'."
    }
    if ([string]$Evidence.package.version -ne [string]$PackageIdentity.version) {
        throw "Evidence package version '$($Evidence.package.version)' differs from the supplied package '$($PackageIdentity.version)'."
    }
    if ([string]$Evidence.package.sha256 -ne [string]$PackageIdentity.sha256) {
        throw "Evidence is bound to a different package SHA-256."
    }
    if ([string]$Evidence.package.entryPointSha256 -ne [string]$PackageIdentity.entryPointSha256) {
        throw "Evidence is bound to a different desktop entry point."
    }
    if ([string]$Evidence.package.betaManualQaEntryPointSha256 -ne [string]$PackageIdentity.betaManualQaEntryPointSha256) {
        throw "Evidence is bound to a different packaged manual-QA tool."
    }
}

function New-EvidenceObject {
    param(
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)]$EnvironmentInfo,
        [Parameter(Mandatory = $true)][string]$RunningToolSha256
    )

    Assert-InteractiveUnelevated -EnvironmentInfo $EnvironmentInfo -Context "Manual beta QA evidence initialization"

    $checks = @()
    foreach ($definition in Get-RequiredChecks) {
        $checks += [pscustomobject]@{
            id = $definition.id
            group = $definition.group
            description = $definition.description
            status = "pending"
            humanConfirmed = $false
            packageVersion = $null
            packageSha256 = $null
            packageEntryPointSha256 = $null
            runningQaToolSha256 = $null
            observedUtc = $null
            observedOsBuild = $null
            observedProcessArchitecture = $null
            observedSessionId = $null
            observedInteractive = $null
            observedProcessElevated = $null
            observedUacEnabled = $null
            note = ""
        }
    }

    $utc = [DateTimeOffset]::UtcNow.ToString("O")
    return [pscustomobject]@{
        schemaVersion = $Script:SchemaVersion
        gate = $Script:GateName
        targetVersion = "0.5.0-beta.1"
        createdUtc = $utc
        updatedUtc = $utc
        package = $PackageIdentity
        createdQaToolSha256 = $RunningToolSha256
        createdEnvironment = $EnvironmentInfo
        checks = $checks
    }
}

function Assert-EvidenceObject {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ([int]$Evidence.schemaVersion -ne $Script:SchemaVersion) {
        throw "Unsupported manual-QA evidence schema '$($Evidence.schemaVersion)'. Reinitialize evidence with this package/tool version."
    }
    if ([string]$Evidence.gate -ne $Script:GateName) {
        throw "Unexpected manual-QA gate '$($Evidence.gate)'."
    }
    if ([string]$Evidence.targetVersion -ne "0.5.0-beta.1") {
        throw "Manual-QA evidence targets '$($Evidence.targetVersion)' instead of 0.5.0-beta.1."
    }

    Assert-PackageMatchesEvidence -Evidence $Evidence -PackageIdentity $PackageIdentity -Version $Version

    if ([string]$Evidence.createdQaToolSha256 -ne [string]$PackageIdentity.betaManualQaEntryPointSha256) {
        throw "Evidence initialization was not performed by the exact beta manual-QA tool packaged in this candidate."
    }
    if (-not [bool]$Evidence.createdEnvironment.userInteractive) {
        throw "Evidence was not initialized from an interactive desktop session."
    }
    if ([int]$Evidence.createdEnvironment.sessionId -le 0) {
        throw "Evidence was initialized from a service/non-interactive Windows session."
    }
    if ($null -eq $Evidence.createdEnvironment.processElevated) {
        throw "Evidence cannot prove whether its initialization process was elevated."
    }
    if ([bool]$Evidence.createdEnvironment.processElevated) {
        throw "Evidence was initialized from an elevated session and cannot prove the normal-user UAC gate."
    }
    if ($null -eq $Evidence.createdEnvironment.uacEnabled) {
        throw "Evidence cannot prove that UAC was enabled when it was initialized."
    }
    if (-not [bool]$Evidence.createdEnvironment.uacEnabled) {
        throw "Evidence was initialized while UAC was disabled and cannot prove the normal-user UAC gate."
    }

    $required = @(Get-RequiredChecks)
    $evidenceChecks = @($Evidence.checks)
    if ($evidenceChecks.Count -ne $required.Count) {
        throw "Evidence check catalog has $($evidenceChecks.Count) items; expected $($required.Count)."
    }

    foreach ($definition in $required) {
        $matches = @($evidenceChecks | Where-Object { [string]$_.id -eq [string]$definition.id })
        if ($matches.Count -ne 1) {
            throw "Evidence must contain exactly one '$($definition.id)' record."
        }

        $record = $matches[0]
        if ([string]$record.group -ne [string]$definition.group) {
            throw "Evidence check '$($definition.id)' has an unexpected group."
        }
        if ([string]$record.status -ne "pass") {
            throw "Manual QA check '$($definition.id)' is '$($record.status)', not pass."
        }
        if (-not [bool]$record.humanConfirmed) {
            throw "Manual QA check '$($definition.id)' was not explicitly human-confirmed."
        }
        if ([string]$record.packageVersion -ne [string]$PackageIdentity.version -or [string]$record.packageSha256 -ne [string]$PackageIdentity.sha256) {
            throw "Manual QA check '$($definition.id)' was not recorded against this exact package."
        }
        if ([string]$record.packageEntryPointSha256 -ne [string]$PackageIdentity.entryPointSha256) {
            throw "Manual QA check '$($definition.id)' was recorded against a different desktop entry point."
        }
        if ([string]$record.runningQaToolSha256 -ne [string]$PackageIdentity.betaManualQaEntryPointSha256) {
            throw "Manual QA check '$($definition.id)' was not recorded by the exact beta manual-QA tool packaged in this candidate."
        }
        if (-not [bool]$record.observedInteractive) {
            throw "Manual QA check '$($definition.id)' was not recorded from an interactive desktop session."
        }
        if ([int]$record.observedSessionId -le 0) {
            throw "Manual QA check '$($definition.id)' was recorded from a service/non-interactive Windows session."
        }
        if ($null -eq $record.observedProcessElevated) {
            throw "Manual QA check '$($definition.id)' cannot prove whether its observation process was elevated."
        }
        if ([bool]$record.observedProcessElevated) {
            throw "Manual QA check '$($definition.id)' was recorded from an elevated session."
        }
        if ($null -eq $record.observedUacEnabled) {
            throw "Manual QA check '$($definition.id)' cannot prove that UAC was enabled during the observation."
        }
        if (-not [bool]$record.observedUacEnabled) {
            throw "Manual QA check '$($definition.id)' was recorded while UAC was disabled."
        }
        if ([string]$record.observedOsBuild -ne [string]$Evidence.createdEnvironment.osBuild) {
            throw "Manual QA check '$($definition.id)' was recorded on Windows build '$($record.observedOsBuild)' instead of the evidence baseline '$($Evidence.createdEnvironment.osBuild)'."
        }
        if ([string]$record.observedProcessArchitecture -ne [string]$Evidence.createdEnvironment.processArchitecture) {
            throw "Manual QA check '$($definition.id)' was recorded with process architecture '$($record.observedProcessArchitecture)' instead of '$($Evidence.createdEnvironment.processArchitecture)'."
        }
    }
}

function Invoke-SelfTest {
    $catalog = @(Get-RequiredChecks)
    if ($catalog.Count -ne 14) {
        throw "Self-test expected 14 required checks, found $($catalog.Count)."
    }
    if (@($catalog.id | Select-Object -Unique).Count -ne $catalog.Count) {
        throw "Self-test found duplicate manual-QA check IDs."
    }

    $fakePackage = [pscustomobject]@{
        fileName = "DragonDiskForge-win-x64.zip"
        sha256 = ("a" * 64)
        version = "0.5.0-beta.1"
        architecture = "x64"
        entryPointSha256 = ("b" * 64)
        betaManualQaEntryPointSha256 = ("c" * 64)
    }
    $fakeEnvironment = [pscustomobject]@{
        osVersion = "Windows self-test"
        osBuild = "self-test-build"
        processArchitecture = "X64"
        userInteractive = $true
        processElevated = $false
        sessionId = 1
        uacEnabled = $true
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
    }

    $runningToolHash = Assert-RunningToolMatchesPackage -PackageIdentity $fakePackage -CurrentToolSha256 $fakePackage.betaManualQaEntryPointSha256
    $evidence = New-EvidenceObject -PackageIdentity $fakePackage -EnvironmentInfo $fakeEnvironment -RunningToolSha256 $runningToolHash
    foreach ($record in @($evidence.checks)) {
        $record.status = "pass"
        $record.humanConfirmed = $true
        $record.packageVersion = $fakePackage.version
        $record.packageSha256 = $fakePackage.sha256
        $record.packageEntryPointSha256 = $fakePackage.entryPointSha256
        $record.runningQaToolSha256 = $runningToolHash
        $record.observedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        $record.observedOsBuild = $fakeEnvironment.osBuild
        $record.observedProcessArchitecture = $fakeEnvironment.processArchitecture
        $record.observedSessionId = $fakeEnvironment.sessionId
        $record.observedInteractive = $true
        $record.observedProcessElevated = $false
        $record.observedUacEnabled = $true
    }

    Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1"

    $toolMismatchRejected = $false
    try { Assert-RunningToolMatchesPackage -PackageIdentity $fakePackage -CurrentToolSha256 ("d" * 64) } catch { $toolMismatchRejected = $true }
    if (-not $toolMismatchRejected) { throw "Self-test failed: a running manual-QA tool hash mismatch did not fail closed." }

    $evidence.createdQaToolSha256 = ("d" * 64)
    $failedCreationToolBinding = $false
    try { Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1" } catch { $failedCreationToolBinding = $true }
    if (-not $failedCreationToolBinding) { throw "Self-test failed: evidence creation-tool mismatch did not fail closed." }
    $evidence.createdQaToolSha256 = $runningToolHash

    $evidence.checks[0].runningQaToolSha256 = ("d" * 64)
    $failedObservationToolBinding = $false
    try { Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1" } catch { $failedObservationToolBinding = $true }
    if (-not $failedObservationToolBinding) { throw "Self-test failed: per-observation running-tool mismatch did not fail closed." }
    $evidence.checks[0].runningQaToolSha256 = $runningToolHash

    $evidence.checks[0].status = "pending"
    $failedClosed = $false
    try { Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1" } catch { $failedClosed = $true }
    if (-not $failedClosed) { throw "Self-test failed: pending evidence did not fail closed." }
    $evidence.checks[0].status = "pass"

    $evidence.checks[1].packageSha256 = ("d" * 64)
    $failedPackageBinding = $false
    try { Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1" } catch { $failedPackageBinding = $true }
    if (-not $failedPackageBinding) { throw "Self-test failed: per-observation package mismatch did not fail closed." }
    $evidence.checks[1].packageSha256 = $fakePackage.sha256

    $evidence.checks[2].observedOsBuild = "different-build"
    $failedEnvironmentBinding = $false
    try { Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1" } catch { $failedEnvironmentBinding = $true }
    if (-not $failedEnvironmentBinding) { throw "Self-test failed: per-observation OS build mismatch did not fail closed." }
    $evidence.checks[2].observedOsBuild = $fakeEnvironment.osBuild

    $evidence.checks[3].observedProcessElevated = $true
    $failedElevated = $false
    try { Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1" } catch { $failedElevated = $true }
    if (-not $failedElevated) { throw "Self-test failed: elevated-session evidence did not fail closed." }
    $evidence.checks[3].observedProcessElevated = $false

    $evidence.checks[4].observedUacEnabled = $null
    $failedUnknownObservedUac = $false
    try { Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1" } catch { $failedUnknownObservedUac = $true }
    if (-not $failedUnknownObservedUac) { throw "Self-test failed: unknown observed UAC state did not fail closed." }
    $evidence.checks[4].observedUacEnabled = $true

    $fakeEnvironment.uacEnabled = $null
    $failedUnknownUac = $false
    try { Assert-InteractiveUnelevated -EnvironmentInfo $fakeEnvironment -Context "Self-test unknown UAC" } catch { $failedUnknownUac = $true }
    if (-not $failedUnknownUac) { throw "Self-test failed: unknown UAC state did not fail closed." }
    $fakeEnvironment.uacEnabled = $true

    $fakeEnvironment.processElevated = $null
    $failedUnknownElevation = $false
    try { Assert-InteractiveUnelevated -EnvironmentInfo $fakeEnvironment -Context "Self-test unknown elevation" } catch { $failedUnknownElevation = $true }
    if (-not $failedUnknownElevation) { throw "Self-test failed: unknown elevation state did not fail closed." }
    $fakeEnvironment.processElevated = $false

    $fakeEnvironment.sessionId = 0
    $failedSessionZero = $false
    try { Assert-InteractiveUnelevated -EnvironmentInfo $fakeEnvironment -Context "Self-test session zero" } catch { $failedSessionZero = $true }
    if (-not $failedSessionZero) { throw "Self-test failed: session zero did not fail closed." }
    $fakeEnvironment.sessionId = 1

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $path = Join-Path $tempRoot "evidence.json"
        $saved = Save-Evidence -Evidence $evidence -Path $path
        $sidecarHash = Assert-EvidenceSidecar -Path $saved.path
        if ($sidecarHash -ne $saved.sha256) {
            throw "Self-test failed: evidence sidecar hash mismatch."
        }
        $loaded = Load-Evidence -Path $saved.path
        Assert-EvidenceObject -Evidence $loaded -PackageIdentity $fakePackage -Version "0.5.0-beta.1"
    }
    finally {
        if (Test-Path $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Dragon DiskForge beta manual-QA evidence schema v3 self-test passed."
}

switch ($Mode) {
    "self-test" {
        Invoke-SelfTest
        exit 0
    }

    "new" {
        if ([string]::IsNullOrWhiteSpace($PackagePath)) {
            throw "-PackagePath is required for -Mode new."
        }
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
            throw "Interactive beta QA is supported only on Windows."
        }
        $identity = Get-PackageIdentity -ZipPath $PackagePath -SidecarPath $ChecksumFile
        if ([string]$identity.version -ne $ExpectedVersion) {
            throw "Manual beta QA must be initialized against version '$ExpectedVersion'; supplied package is '$($identity.version)'."
        }
        $runningToolHash = Assert-RunningToolMatchesPackage -PackageIdentity $identity
        $environment = Get-SessionEnvironment
        $evidence = New-EvidenceObject -PackageIdentity $identity -EnvironmentInfo $environment -RunningToolSha256 $runningToolHash
        $saved = Save-Evidence -Evidence $evidence -Path $EvidencePath
        Write-Host "Initialized interactive beta QA evidence schema v3."
        Write-Host "Package version: $($identity.version)"
        Write-Host "Package SHA-256: $($identity.sha256)"
        Write-Host "Running QA tool SHA-256: $runningToolHash"
        Write-Host "Windows build: $($environment.osBuild)"
        Write-Host "UAC enabled: $($environment.uacEnabled)"
        Write-Host "Evidence: $($saved.path)"
        Write-Host "Every subsequent record operation must supply the same -PackagePath so each observation is rebound to the exact candidate and packaged QA tool."
        exit 0
    }

    "record" {
        if ([string]::IsNullOrWhiteSpace($Check)) {
            throw "-Check is required for -Mode record."
        }
        if ([string]::IsNullOrWhiteSpace($PackagePath)) {
            throw "-PackagePath is required for -Mode record so every observation is revalidated against the exact candidate."
        }
        if ($Result -notin @("pass", "fail")) {
            throw "-Result must be 'pass' or 'fail' for -Mode record."
        }
        if ($Result -eq "pass" -and -not $HumanConfirmed) {
            throw "A passing manual QA result requires -HumanConfirmed."
        }
        if ($Note.Length -gt 1000) {
            throw "-Note is limited to 1000 characters."
        }
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
            throw "Interactive beta QA is supported only on Windows."
        }

        Assert-EvidenceSidecar -Path $EvidencePath | Out-Null
        $evidence = Load-Evidence -Path $EvidencePath
        $identity = Get-PackageIdentity -ZipPath $PackagePath -SidecarPath $ChecksumFile
        $runningToolHash = Assert-RunningToolMatchesPackage -PackageIdentity $identity
        if ([int]$evidence.schemaVersion -ne $Script:SchemaVersion) {
            throw "Evidence schema '$($evidence.schemaVersion)' is not schema v$Script:SchemaVersion. Reinitialize evidence before recording new observations."
        }
        Assert-PackageMatchesEvidence -Evidence $evidence -PackageIdentity $identity -Version $ExpectedVersion

        $matches = @($evidence.checks | Where-Object { [string]$_.id -eq $Check })
        if ($matches.Count -ne 1) {
            throw "Unknown or duplicate manual QA check '$Check'. Run -Mode list to see valid IDs."
        }

        $environment = Get-SessionEnvironment
        Assert-InteractiveUnelevated -EnvironmentInfo $environment -Context "Manual QA observation recording"
        if ([string]$environment.osBuild -ne [string]$evidence.createdEnvironment.osBuild) {
            throw "Current Windows build '$($environment.osBuild)' differs from evidence baseline '$($evidence.createdEnvironment.osBuild)'. Reinitialize evidence on the machine/build being qualified."
        }
        if ([string]$environment.processArchitecture -ne [string]$evidence.createdEnvironment.processArchitecture) {
            throw "Current process architecture '$($environment.processArchitecture)' differs from evidence baseline '$($evidence.createdEnvironment.processArchitecture)'."
        }

        $record = $matches[0]
        $record.status = $Result
        $record.humanConfirmed = [bool]$HumanConfirmed
        $record.packageVersion = [string]$identity.version
        $record.packageSha256 = [string]$identity.sha256
        $record.packageEntryPointSha256 = [string]$identity.entryPointSha256
        $record.runningQaToolSha256 = $runningToolHash
        $record.observedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        $record.observedOsBuild = [string]$environment.osBuild
        $record.observedProcessArchitecture = [string]$environment.processArchitecture
        $record.observedSessionId = [int]$environment.sessionId
        $record.observedInteractive = [bool]$environment.userInteractive
        $record.observedProcessElevated = [bool]$environment.processElevated
        $record.observedUacEnabled = $environment.uacEnabled
        $record.note = $Note
        $evidence.updatedUtc = [DateTimeOffset]::UtcNow.ToString("O")

        $saved = Save-Evidence -Evidence $evidence -Path $EvidencePath
        Write-Host "Recorded '$Check' as '$Result' against package $($identity.sha256) with packaged QA tool $runningToolHash."
        Write-Host "Evidence SHA-256: $($saved.sha256)"
        exit 0
    }

    "list" {
        Assert-EvidenceSidecar -Path $EvidencePath | Out-Null
        $evidence = Load-Evidence -Path $EvidencePath
        Write-Host "Evidence schema: $($evidence.schemaVersion)"
        Write-Host "Package SHA-256: $($evidence.package.sha256)"
        foreach ($record in @($evidence.checks)) {
            $binding = "unbound"
            if (-not [string]::IsNullOrWhiteSpace([string]$record.packageSha256)) { $binding = ([string]$record.packageSha256).Substring(0, 12) }
            Write-Host ("{0,-34} {1,-8} {2,-12} {3}" -f $record.id, $record.status, $binding, $record.description)
        }
        exit 0
    }

    "verify" {
        if ([string]::IsNullOrWhiteSpace($PackagePath)) {
            throw "-PackagePath is required for -Mode verify so evidence stays bound to the exact release candidate."
        }
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
            throw "Interactive beta QA is supported only on Windows."
        }

        $evidenceHash = Assert-EvidenceSidecar -Path $EvidencePath
        $evidence = Load-Evidence -Path $EvidencePath
        $identity = Get-PackageIdentity -ZipPath $PackagePath -SidecarPath $ChecksumFile
        $runningToolHash = Assert-RunningToolMatchesPackage -PackageIdentity $identity
        Assert-EvidenceObject -Evidence $evidence -PackageIdentity $identity -Version $ExpectedVersion

        Write-Host "Interactive beta QA evidence is COMPLETE for the exact package and packaged QA tool."
        Write-Host "Evidence schema: $($evidence.schemaVersion)"
        Write-Host "Version: $($identity.version)"
        Write-Host "Package SHA-256: $($identity.sha256)"
        Write-Host "Running QA tool SHA-256: $runningToolHash"
        Write-Host "Evidence SHA-256: $evidenceHash"
        exit 0
    }
}
