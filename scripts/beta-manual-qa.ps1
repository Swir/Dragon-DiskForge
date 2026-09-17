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

$Script:SchemaVersion = 1
$Script:GateName = "Dragon DiskForge interactive beta QA"

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
        return $false
    }

    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
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

    return [pscustomobject]@{
        osVersion = [Environment]::OSVersion.VersionString
        osBuild = $build
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        userInteractive = [Environment]::UserInteractive
        processElevated = (Test-IsElevated)
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
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
        $json = $Evidence | ConvertTo-Json -Depth 10
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

function New-EvidenceObject {
    param(
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)]$EnvironmentInfo
    )

    if (-not [bool]$EnvironmentInfo.userInteractive) {
        throw "Manual beta QA evidence must be initialized from an interactive Windows desktop session."
    }
    if ([bool]$EnvironmentInfo.processElevated) {
        throw "Manual beta QA evidence must be initialized from a normal unelevated session so UAC behavior is observable."
    }

    $checks = @()
    foreach ($definition in Get-RequiredChecks) {
        $checks += [pscustomobject]@{
            id = $definition.id
            group = $definition.group
            description = $definition.description
            status = "pending"
            humanConfirmed = $false
            observedUtc = $null
            observedInteractive = $null
            observedProcessElevated = $null
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
        throw "Unsupported manual-QA evidence schema '$($Evidence.schemaVersion)'."
    }
    if ([string]$Evidence.gate -ne $Script:GateName) {
        throw "Unexpected manual-QA gate '$($Evidence.gate)'."
    }
    if ([string]$Evidence.targetVersion -ne "0.5.0-beta.1") {
        throw "Manual-QA evidence targets '$($Evidence.targetVersion)' instead of 0.5.0-beta.1."
    }
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
    if (-not [bool]$Evidence.createdEnvironment.userInteractive) {
        throw "Evidence was not initialized from an interactive desktop session."
    }
    if ([bool]$Evidence.createdEnvironment.processElevated) {
        throw "Evidence was initialized from an elevated session and cannot prove the normal-user UAC gate."
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
        if (-not [bool]$record.observedInteractive) {
            throw "Manual QA check '$($definition.id)' was not recorded from an interactive desktop session."
        }
        if ([bool]$record.observedProcessElevated) {
            throw "Manual QA check '$($definition.id)' was recorded from an elevated session."
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

    $fakeHash = ("a" * 64)
    $fakePackage = [pscustomobject]@{
        fileName = "DragonDiskForge-win-x64.zip"
        sha256 = $fakeHash
        version = "0.5.0-beta.1"
        architecture = "x64"
        entryPointSha256 = ("b" * 64)
        betaManualQaEntryPointSha256 = ("c" * 64)
    }
    $fakeEnvironment = [pscustomobject]@{
        osVersion = "Windows self-test"
        osBuild = "self-test"
        processArchitecture = "X64"
        userInteractive = $true
        processElevated = $false
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
    $evidence = New-EvidenceObject -PackageIdentity $fakePackage -EnvironmentInfo $fakeEnvironment
    foreach ($record in @($evidence.checks)) {
        $record.status = "pass"
        $record.humanConfirmed = $true
        $record.observedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        $record.observedInteractive = $true
        $record.observedProcessElevated = $false
    }

    Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1"

    $evidence.checks[0].status = "pending"
    $failedClosed = $false
    try {
        Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1"
    }
    catch {
        $failedClosed = $true
    }
    if (-not $failedClosed) {
        throw "Self-test failed: pending evidence did not fail closed."
    }
    $evidence.checks[0].status = "pass"

    $evidence.checks[2].observedProcessElevated = $true
    $failedElevated = $false
    try {
        Assert-EvidenceObject -Evidence $evidence -PackageIdentity $fakePackage -Version "0.5.0-beta.1"
    }
    catch {
        $failedElevated = $true
    }
    if (-not $failedElevated) {
        throw "Self-test failed: elevated-session evidence did not fail closed."
    }
    $evidence.checks[2].observedProcessElevated = $false

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

    Write-Host "Dragon DiskForge beta manual-QA evidence self-test passed."
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
        $environment = Get-SessionEnvironment
        $evidence = New-EvidenceObject -PackageIdentity $identity -EnvironmentInfo $environment
        $saved = Save-Evidence -Evidence $evidence -Path $EvidencePath
        Write-Host "Initialized interactive beta QA evidence."
        Write-Host "Package version: $($identity.version)"
        Write-Host "Package SHA-256: $($identity.sha256)"
        Write-Host "Evidence: $($saved.path)"
        Write-Host "Run with -Mode list to see the required check IDs."
        exit 0
    }

    "record" {
        if ([string]::IsNullOrWhiteSpace($Check)) {
            throw "-Check is required for -Mode record."
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
        $matches = @($evidence.checks | Where-Object { [string]$_.id -eq $Check })
        if ($matches.Count -ne 1) {
            throw "Unknown or duplicate manual QA check '$Check'. Run -Mode list to see valid IDs."
        }

        $environment = Get-SessionEnvironment
        if ($Result -eq "pass") {
            if (-not [bool]$environment.userInteractive) {
                throw "Passing manual QA evidence must be recorded from an interactive desktop session."
            }
            if ([bool]$environment.processElevated) {
                throw "Passing manual QA evidence must be recorded from a normal unelevated session."
            }
        }

        $record = $matches[0]
        $record.status = $Result
        $record.humanConfirmed = [bool]$HumanConfirmed
        $record.observedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        $record.observedInteractive = [bool]$environment.userInteractive
        $record.observedProcessElevated = [bool]$environment.processElevated
        $record.note = $Note
        $evidence.updatedUtc = [DateTimeOffset]::UtcNow.ToString("O")

        $saved = Save-Evidence -Evidence $evidence -Path $EvidencePath
        Write-Host "Recorded '$Check' as '$Result'."
        Write-Host "Evidence SHA-256: $($saved.sha256)"
        exit 0
    }

    "list" {
        Assert-EvidenceSidecar -Path $EvidencePath | Out-Null
        $evidence = Load-Evidence -Path $EvidencePath
        foreach ($record in @($evidence.checks)) {
            Write-Host ("{0,-34} {1,-8} {2}" -f $record.id, $record.status, $record.description)
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
        Assert-EvidenceObject -Evidence $evidence -PackageIdentity $identity -Version $ExpectedVersion

        Write-Host "Interactive beta QA evidence is COMPLETE for the exact package."
        Write-Host "Version: $($identity.version)"
        Write-Host "Package SHA-256: $($identity.sha256)"
        Write-Host "Evidence SHA-256: $evidenceHash"
        exit 0
    }
}
