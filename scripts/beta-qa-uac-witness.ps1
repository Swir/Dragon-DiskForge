[CmdletBinding()]
param(
    [ValidateSet("capture", "verify", "self-test")]
    [string]$Mode = "verify",

    [string]$EvidencePath = "artifacts/manual-qa/beta-qa-uac-witness.json",
    [string]$PackagePath = "",
    [string]$ChecksumFile = "",
    [ValidateSet("before", "after")]
    [string]$Phase = "before",
    [ValidateSet(
        "uac.iso-no-prompt",
        "uac.vhd-cancel",
        "uac.vhd-approve-readonly",
        "uac.vhdx-cancel",
        "uac.vhdx-approve-readonly",
        "uac.unmount-refresh"
    )]
    [string]$Check = "uac.iso-no-prompt",
    [string]$ImagePath = "",
    [string]$ExpectedVersion = "0.5.0-beta.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:SchemaVersion = 1
$Script:Kind = "DragonDiskForgeBetaQaUacWitness"

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Write-Sha256Sidecar {
    param([Parameter(Mandatory = $true)][string]$Path)
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    $sidecar = "$resolved.sha256"
    Write-Utf8NoBom -Path $sidecar -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($resolved), [Environment]::NewLine)
    return $hash
}

function Assert-Sha256Sidecar {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$SidecarPath = ""
    )
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if ([string]::IsNullOrWhiteSpace($SidecarPath)) {
        $SidecarPath = "$resolved.sha256"
    }
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path
    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "SHA-256 sidecar has an invalid format: $sidecar"
    }
    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    $fileName = [System.IO.Path]::GetFileName($resolved)
    if ($declaredName -ne $fileName) {
        throw "SHA-256 sidecar targets '$declaredName' instead of '$fileName'."
    }
    $actualHash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "SHA-256 mismatch for '$fileName'. Expected '$declaredHash', actual '$actualHash'."
    }
    return $actualHash
}

function Get-RunningToolSha256 {
    if ([string]::IsNullOrWhiteSpace([string]$PSCommandPath) -or -not (Test-Path -LiteralPath $PSCommandPath -PathType Leaf)) {
        return $null
    }
    return (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-IsElevated {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $null }
    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $null
    }
}

function Get-RegistryDword {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )
    try {
        return [int](Get-ItemPropertyValue -LiteralPath $Path -Name $Name -ErrorAction Stop)
    }
    catch {
        return $null
    }
}

function Get-UacEnvironment {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw "UAC witness capture requires Windows."
    }

    $sessionId = -1
    try { $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId } catch { $sessionId = -1 }

    $policyPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
    $environment = [ordered]@{
        osVersion = [Environment]::OSVersion.VersionString
        osBuild = [Environment]::OSVersion.Version.Build.ToString()
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        userInteractive = [Environment]::UserInteractive
        processElevated = (Test-IsElevated)
        sessionId = $sessionId
        enableLUA = (Get-RegistryDword -Path $policyPath -Name "EnableLUA")
        consentPromptBehaviorAdmin = (Get-RegistryDword -Path $policyPath -Name "ConsentPromptBehaviorAdmin")
        consentPromptBehaviorUser = (Get-RegistryDword -Path $policyPath -Name "ConsentPromptBehaviorUser")
        promptOnSecureDesktop = (Get-RegistryDword -Path $policyPath -Name "PromptOnSecureDesktop")
        filterAdministratorToken = (Get-RegistryDword -Path $policyPath -Name "FilterAdministratorToken")
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
    }

    if (-not [bool]$environment.userInteractive) {
        throw "UAC witness capture requires an interactive Windows desktop session."
    }
    if ([int]$environment.sessionId -le 0) {
        throw "UAC witness capture requires a non-service interactive Windows session."
    }
    if ($null -eq $environment.processElevated) {
        throw "Cannot determine whether the witness process is elevated."
    }
    if ([bool]$environment.processElevated) {
        throw "UAC witness capture must run from a normal unelevated session."
    }
    if ($null -eq $environment.enableLUA -or [int]$environment.enableLUA -ne 1) {
        throw "UAC witness capture requires Windows UAC (EnableLUA=1)."
    }

    return [pscustomobject]$environment
}

function Get-PathFingerprint {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Path.ToLowerInvariant())
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-DiskImageSnapshot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $extension = [System.IO.Path]::GetExtension($resolved).ToLowerInvariant()
    if ($extension -notin @(".iso", ".vhd", ".vhdx")) {
        throw "UAC witness image must be an ISO, VHD or VHDX file."
    }

    $item = Get-Item -LiteralPath $resolved
    $snapshot = [ordered]@{
        imageLeaf = $item.Name
        imagePathSha256 = Get-PathFingerprint -Path $resolved
        extension = $extension
        length = [int64]$item.Length
        diskImageQueryAvailable = $false
        attached = $null
        devicePath = ""
        imageType = ""
        diskNumber = $null
        diskIsReadOnly = $null
    }

    $getDiskImage = Get-Command Get-DiskImage -ErrorAction SilentlyContinue
    if ($null -eq $getDiskImage) {
        throw "Windows Get-DiskImage is unavailable; refusing to create ambiguous UAC witness evidence."
    }

    $snapshot.diskImageQueryAvailable = $true
    $image = Get-DiskImage -ImagePath $resolved -ErrorAction Stop
    $snapshot.attached = [bool]$image.Attached
    $snapshot.devicePath = [string]$image.DevicePath
    $snapshot.imageType = [string]$image.ImageType
    if ($image.PSObject.Properties.Name -contains "Number" -and $null -ne $image.Number) {
        $snapshot.diskNumber = [int]$image.Number
        try {
            $disk = Get-Disk -Number ([int]$image.Number) -ErrorAction Stop
            $snapshot.diskIsReadOnly = [bool]$disk.IsReadOnly
        }
        catch {
            $snapshot.diskIsReadOnly = $null
        }
    }

    return [pscustomobject]$snapshot
}

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][string]$Version
    )

    $zip = (Resolve-Path -LiteralPath $ZipPath).Path
    $zipHash = Assert-Sha256Sidecar -Path $zip -SidecarPath $SidecarPath
    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-uac-witness-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null

    try {
        Expand-Archive -LiteralPath $zip -DestinationPath $workspace -Force
        $manifestPath = Join-Path $workspace "package-manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Candidate package manifest is missing."
        }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -lt 5) { throw "Candidate package manifest schema must be at least 5." }
        if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected package product '$($manifest.product)'." }
        if ([string]$manifest.version -ne $Version) { throw "Unexpected package version '$($manifest.version)'." }
        if ([string]$manifest.architecture -ne "x64") { throw "Candidate package architecture must be x64." }

        $entryRelative = ([string]$manifest.entryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $entryPath = Join-Path $workspace $entryRelative
        if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) { throw "Candidate desktop entry point is missing." }
        $entryHash = (Get-FileHash -LiteralPath $entryPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($entryHash -ne ([string]$manifest.entryPointSha256).ToLowerInvariant()) {
            throw "Candidate desktop entry point SHA-256 does not match the manifest."
        }

        $qaRelative = ([string]$manifest.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $qaPath = Join-Path $workspace $qaRelative
        if (-not (Test-Path -LiteralPath $qaPath -PathType Leaf)) { throw "Candidate beta manual-QA tool is missing." }
        $qaHash = (Get-FileHash -LiteralPath $qaPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($qaHash -ne ([string]$manifest.betaManualQaEntryPointSha256).ToLowerInvariant()) {
            throw "Candidate beta manual-QA tool SHA-256 does not match the manifest."
        }

        return [pscustomobject]@{
            packageFile = [System.IO.Path]::GetFileName($zip)
            packageSha256 = $zipHash
            version = [string]$manifest.version
            architecture = [string]$manifest.architecture
            packageManifestSchema = [int]$manifest.schemaVersion
            entryPointSha256 = $entryHash
            betaManualQaEntryPointSha256 = $qaHash
        }
    }
    finally {
        Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Save-Evidence {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    Write-Utf8NoBom -Path $fullPath -Text (($Evidence | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
    $hash = Write-Sha256Sidecar -Path $fullPath
    Write-Host "UAC witness captured: $fullPath"
    Write-Host "Witness SHA-256: $hash"
}

function Assert-Evidence {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$ZipPath = "",
        [string]$SidecarPath = "",
        [string]$ExpectedImagePath = ""
    )

    $fullPath = (Resolve-Path -LiteralPath $Path).Path
    $null = Assert-Sha256Sidecar -Path $fullPath
    $evidence = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json

    if ([int]$evidence.schemaVersion -ne $Script:SchemaVersion) { throw "Unsupported UAC witness schema '$($evidence.schemaVersion)'." }
    if ([string]$evidence.kind -ne $Script:Kind) { throw "Unexpected UAC witness kind '$($evidence.kind)'." }
    if ([string]$evidence.product -ne "Dragon DiskForge") { throw "Unexpected UAC witness product." }
    if ([string]$evidence.version -ne $ExpectedVersion) { throw "UAC witness version does not match expected beta version." }
    if ([string]$evidence.architecture -ne "x64") { throw "UAC witness architecture must be x64." }
    if ([bool]$evidence.humanGateClaimed) { throw "UAC witness must never claim a human gate passed." }
    if ([bool]$evidence.publicRelease) { throw "UAC witness must never claim a public release." }
    if ([string]$evidence.packageSha256 -notmatch '^[0-9a-f]{64}$') { throw "UAC witness package SHA-256 is invalid." }
    if ([string]$evidence.toolSha256 -notmatch '^[0-9a-f]{64}$') { throw "UAC witness tool SHA-256 is invalid." }

    $runningHash = Get-RunningToolSha256
    if (-not [string]::IsNullOrWhiteSpace($runningHash) -and $runningHash -ne ([string]$evidence.toolSha256).ToLowerInvariant()) {
        throw "Running UAC witness tool SHA-256 does not match the evidence."
    }

    if (-not [string]::IsNullOrWhiteSpace($ZipPath)) {
        $package = Get-PackageIdentity -ZipPath $ZipPath -SidecarPath $SidecarPath -Version $ExpectedVersion
        if ($package.packageSha256 -ne ([string]$evidence.packageSha256).ToLowerInvariant()) {
            throw "UAC witness is bound to a different candidate package."
        }
        if ($package.entryPointSha256 -ne ([string]$evidence.entryPointSha256).ToLowerInvariant()) {
            throw "UAC witness desktop entry-point identity does not match the candidate package."
        }
        if ($package.betaManualQaEntryPointSha256 -ne ([string]$evidence.betaManualQaEntryPointSha256).ToLowerInvariant()) {
            throw "UAC witness beta manual-QA tool identity does not match the candidate package."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedImagePath)) {
        $resolved = (Resolve-Path -LiteralPath $ExpectedImagePath).Path
        $fingerprint = Get-PathFingerprint -Path $resolved
        if ($fingerprint -ne ([string]$evidence.diskImage.imagePathSha256).ToLowerInvariant()) {
            throw "UAC witness image path fingerprint does not match the expected image."
        }
    }

    Write-Host "Dragon DiskForge UAC witness verification passed."
    Write-Host "Check: $($evidence.checkId) / phase $($evidence.phase)"
    Write-Host "Package SHA-256: $($evidence.packageSha256)"
    Write-Host "Human gate claimed: false"
}

function Invoke-Capture {
    if ([string]::IsNullOrWhiteSpace($PackagePath)) { throw "-PackagePath is required for UAC witness capture." }
    if ([string]::IsNullOrWhiteSpace($ImagePath)) { throw "-ImagePath is required for UAC witness capture." }

    $environment = Get-UacEnvironment
    $package = Get-PackageIdentity -ZipPath $PackagePath -SidecarPath $ChecksumFile -Version $ExpectedVersion
    $diskImage = Get-DiskImageSnapshot -Path $ImagePath
    $toolHash = Get-RunningToolSha256
    if ([string]::IsNullOrWhiteSpace($toolHash)) { throw "Cannot determine the running UAC witness tool SHA-256." }

    $evidence = [ordered]@{
        schemaVersion = $Script:SchemaVersion
        kind = $Script:Kind
        product = "Dragon DiskForge"
        version = $package.version
        architecture = $package.architecture
        packageFile = $package.packageFile
        packageSha256 = $package.packageSha256
        packageManifestSchema = $package.packageManifestSchema
        entryPointSha256 = $package.entryPointSha256
        betaManualQaEntryPointSha256 = $package.betaManualQaEntryPointSha256
        toolSha256 = $toolHash
        checkId = $Check
        phase = $Phase
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        environment = $environment
        diskImage = $diskImage
        humanGateClaimed = $false
        publicRelease = $false
    }

    Save-Evidence -Evidence $evidence -Path $EvidencePath
    Write-Host "This objective witness does not pass the UAC gate; record the human observation separately with the packaged beta-manual-qa.ps1 tool."
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-uac-witness-selftest-" + [guid]::NewGuid().ToString("N"))
    $packageRoot = Join-Path $root "package"
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "tools") -Force | Out-Null

    try {
        $entry = Join-Path $packageRoot "DragonDiskForge.App.exe"
        $qa = Join-Path $packageRoot "tools/beta-manual-qa.ps1"
        Write-Utf8NoBom -Path $entry -Text "dummy-desktop"
        Write-Utf8NoBom -Path $qa -Text "Write-Host 'dummy qa'"
        $entryHash = (Get-FileHash -LiteralPath $entry -Algorithm SHA256).Hash.ToLowerInvariant()
        $qaHash = (Get-FileHash -LiteralPath $qa -Algorithm SHA256).Hash.ToLowerInvariant()

        $manifest = [ordered]@{
            schemaVersion = 5
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            entryPoint = "DragonDiskForge.App.exe"
            entryPointSha256 = $entryHash
            betaManualQaEntryPoint = "tools/beta-manual-qa.ps1"
            betaManualQaEntryPointSha256 = $qaHash
        }
        Write-Utf8NoBom -Path (Join-Path $packageRoot "package-manifest.json") -Text (($manifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine)

        $zip = Join-Path $root "DragonDiskForge-win-x64.zip"
        Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zip -Force
        $null = Write-Sha256Sidecar -Path $zip

        $identity = Get-PackageIdentity -ZipPath $zip -Version "0.5.0-beta.1"
        if ($identity.packageManifestSchema -ne 5 -or $identity.architecture -ne "x64") {
            throw "Self-test failed: valid package identity was not accepted."
        }

        $badSidecar = "$zip.sha256"
        Write-Utf8NoBom -Path $badSidecar -Text (("{0}  {1}{2}" -f ('0' * 64), [System.IO.Path]::GetFileName($zip), [Environment]::NewLine))
        $rejected = $false
        try { $null = Get-PackageIdentity -ZipPath $zip -Version "0.5.0-beta.1" } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: tampered package sidecar was accepted." }

        $null = Write-Sha256Sidecar -Path $zip
        $fakeEvidence = [ordered]@{
            schemaVersion = 1
            kind = $Script:Kind
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            packageFile = [System.IO.Path]::GetFileName($zip)
            packageSha256 = $identity.packageSha256
            packageManifestSchema = 5
            entryPointSha256 = $identity.entryPointSha256
            betaManualQaEntryPointSha256 = $identity.betaManualQaEntryPointSha256
            toolSha256 = (Get-RunningToolSha256)
            checkId = "uac.vhd-cancel"
            phase = "before"
            createdUtc = "2026-09-19T00:00:00Z"
            environment = [ordered]@{
                userInteractive = $true
                processElevated = $false
                sessionId = 1
                enableLUA = 1
            }
            diskImage = [ordered]@{
                imageLeaf = "fixture.vhd"
                imagePathSha256 = ('a' * 64)
                extension = ".vhd"
            }
            humanGateClaimed = $false
            publicRelease = $false
        }
        $evidencePath = Join-Path $root "witness.json"
        Write-Utf8NoBom -Path $evidencePath -Text (($fakeEvidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $null = Write-Sha256Sidecar -Path $evidencePath
        Assert-Evidence -Path $evidencePath -ZipPath $zip

        $fakeEvidence.humanGateClaimed = $true
        Write-Utf8NoBom -Path $evidencePath -Text (($fakeEvidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $null = Write-Sha256Sidecar -Path $evidencePath
        $rejected = $false
        try { Assert-Evidence -Path $evidencePath -ZipPath $zip } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: humanGateClaimed=true was accepted." }

        Write-Host "Dragon DiskForge UAC witness self-test passed."
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Mode -eq "self-test") {
    Invoke-SelfTest
    exit 0
}
if ($Mode -eq "capture") {
    Invoke-Capture
    exit 0
}

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    throw "-PackagePath is required for UAC witness verification."
}
Assert-Evidence -Path $EvidencePath -ZipPath $PackagePath -SidecarPath $ChecksumFile -ExpectedImagePath $ImagePath
