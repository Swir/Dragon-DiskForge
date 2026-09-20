[CmdletBinding()]
param(
    [ValidateSet("verify", "self-test")]
    [string]$Mode = "verify",

    [string]$BeforeEvidencePath = "artifacts/manual-qa/beta-qa-uac-before.json",
    [string]$AfterEvidencePath = "artifacts/manual-qa/beta-qa-uac-after.json",
    [string]$PackagePath = "",
    [string]$ChecksumFile = "",
    [ValidateRange(1, 240)]
    [int]$MaxPairMinutes = 60,
    [string]$ExpectedVersion = "0.5.0-beta.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:WitnessSchemaVersion = 1
$Script:WitnessKind = "DragonDiskForgeBetaQaUacWitness"
$Script:AllowedChecks = @(
    "uac.iso-no-prompt",
    "uac.vhd-cancel",
    "uac.vhd-approve-readonly",
    "uac.vhdx-cancel",
    "uac.vhdx-approve-readonly",
    "uac.unmount-refresh"
)

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
    Write-Utf8NoBom -Path "$resolved.sha256" -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($resolved), [Environment]::NewLine)
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

function Assert-HexSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Label is not a valid SHA-256 value."
    }
}

function Assert-Property {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $Name)) {
        throw "$Context is missing required property '$Name'."
    }
}

function Assert-SameValue {
    param(
        [AllowNull()]$Left,
        [AllowNull()]$Right,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ([string]$Left -cne [string]$Right) {
        throw "UAC witness pair mismatch: $Label changed between before and after captures."
    }
}

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][string]$Version
    )

    $zip = (Resolve-Path -LiteralPath $ZipPath).Path
    $zipHash = Assert-Sha256Sidecar -Path $zip -SidecarPath $SidecarPath
    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-uac-pair-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null

    try {
        Expand-Archive -LiteralPath $zip -DestinationPath $workspace -Force
        $manifestPath = Join-Path $workspace "package-manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Candidate package manifest is missing."
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        foreach ($name in @(
            "schemaVersion", "product", "version", "architecture",
            "entryPoint", "entryPointSha256",
            "betaManualQaEntryPoint", "betaManualQaEntryPointSha256",
            "betaUacWitnessEntryPoint", "betaUacWitnessEntryPointSha256"
        )) {
            Assert-Property -Object $manifest -Name $name -Context "Candidate package manifest"
        }

        if ([int]$manifest.schemaVersion -lt 6) { throw "Candidate package manifest schema must be at least 6." }
        if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected package product '$($manifest.product)'." }
        if ([string]$manifest.version -ne $Version) { throw "Unexpected package version '$($manifest.version)'." }
        if ([string]$manifest.architecture -ne "x64") { throw "Candidate package architecture must be x64." }

        $entries = @(
            [pscustomobject]@{ Path = [string]$manifest.entryPoint; Hash = [string]$manifest.entryPointSha256; Label = "desktop entry point" },
            [pscustomobject]@{ Path = [string]$manifest.betaManualQaEntryPoint; Hash = [string]$manifest.betaManualQaEntryPointSha256; Label = "beta manual-QA tool" },
            [pscustomobject]@{ Path = [string]$manifest.betaUacWitnessEntryPoint; Hash = [string]$manifest.betaUacWitnessEntryPointSha256; Label = "UAC witness tool" }
        )

        foreach ($entry in $entries) {
            Assert-HexSha256 -Value $entry.Hash -Label ("Manifest {0} SHA-256" -f $entry.Label)
            $relative = $entry.Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $path = Join-Path $workspace $relative
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "Candidate package $($entry.Label) is missing."
            }
            $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actualHash -ne $entry.Hash.ToLowerInvariant()) {
                throw "Candidate package $($entry.Label) SHA-256 does not match the manifest."
            }
        }

        return [pscustomobject]@{
            packageFile = [System.IO.Path]::GetFileName($zip)
            packageSha256 = $zipHash
            version = [string]$manifest.version
            architecture = [string]$manifest.architecture
            packageManifestSchema = [int]$manifest.schemaVersion
            entryPointSha256 = ([string]$manifest.entryPointSha256).ToLowerInvariant()
            betaManualQaEntryPointSha256 = ([string]$manifest.betaManualQaEntryPointSha256).ToLowerInvariant()
            betaUacWitnessEntryPointSha256 = ([string]$manifest.betaUacWitnessEntryPointSha256).ToLowerInvariant()
        }
    }
    finally {
        Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Read-Witness {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $null = Assert-Sha256Sidecar -Path $resolved
    $witness = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json

    foreach ($name in @(
        "schemaVersion", "kind", "product", "version", "architecture", "packageFile",
        "packageSha256", "packageManifestSchema", "entryPointSha256", "betaManualQaEntryPointSha256",
        "toolSha256", "checkId", "phase", "createdUtc", "environment", "diskImage",
        "humanGateClaimed", "publicRelease"
    )) {
        Assert-Property -Object $witness -Name $name -Context $Label
    }

    if ([int]$witness.schemaVersion -ne $Script:WitnessSchemaVersion) { throw "$Label has unsupported witness schema '$($witness.schemaVersion)'." }
    if ([string]$witness.kind -ne $Script:WitnessKind) { throw "$Label has unexpected witness kind '$($witness.kind)'." }
    if ([string]$witness.product -ne "Dragon DiskForge") { throw "$Label has unexpected product." }
    if ([string]$witness.version -ne $Version) { throw "$Label version does not match '$Version'." }
    if ([string]$witness.architecture -ne "x64") { throw "$Label architecture must be x64." }
    if ([int]$witness.packageManifestSchema -lt 6) { throw "$Label must be bound to package manifest schema 6 or newer." }
    if ([bool]$witness.humanGateClaimed) { throw "$Label must not claim a human gate passed." }
    if ([bool]$witness.publicRelease) { throw "$Label must not claim a public release." }
    if ([string]$witness.checkId -notin $Script:AllowedChecks) { throw "$Label has unsupported checkId '$($witness.checkId)'." }

    foreach ($hashField in @("packageSha256", "entryPointSha256", "betaManualQaEntryPointSha256", "toolSha256")) {
        Assert-HexSha256 -Value ([string]$witness.$hashField) -Label "$Label $hashField"
    }

    foreach ($name in @(
        "osBuild", "processArchitecture", "userInteractive", "processElevated", "sessionId",
        "enableLUA", "consentPromptBehaviorAdmin", "consentPromptBehaviorUser",
        "promptOnSecureDesktop", "filterAdministratorToken"
    )) {
        Assert-Property -Object $witness.environment -Name $name -Context "$Label environment"
    }
    if (-not [bool]$witness.environment.userInteractive) { throw "$Label was not captured from an interactive session." }
    if ([bool]$witness.environment.processElevated) { throw "$Label was captured from an elevated process." }
    if ([int]$witness.environment.sessionId -le 0) { throw "$Label has an invalid desktop session id." }
    if ([int]$witness.environment.enableLUA -ne 1) { throw "$Label was captured with UAC disabled." }

    foreach ($name in @("imageLeaf", "imagePathSha256", "extension", "length", "attached", "diskIsReadOnly")) {
        Assert-Property -Object $witness.diskImage -Name $name -Context "$Label diskImage"
    }
    Assert-HexSha256 -Value ([string]$witness.diskImage.imagePathSha256) -Label "$Label imagePathSha256"
    if ([int64]$witness.diskImage.length -lt 0) { throw "$Label image length is invalid." }
    if ($null -eq $witness.diskImage.attached) { throw "$Label attached state is unknown." }

    $parsedCreatedUtc = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$witness.createdUtc, [ref]$parsedCreatedUtc)) {
        throw "$Label createdUtc is invalid."
    }

    return [pscustomobject]@{
        path = $resolved
        createdUtc = $parsedCreatedUtc.ToUniversalTime()
        data = $witness
    }
}

function Assert-Pair {
    param(
        [Parameter(Mandatory = $true)]$BeforeRecord,
        [Parameter(Mandatory = $true)]$AfterRecord,
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)][int]$MaximumMinutes
    )

    $beforeData = $BeforeRecord.data
    $afterData = $AfterRecord.data

    if ([string]$beforeData.phase -ne "before") { throw "The first UAC witness must have phase 'before'." }
    if ([string]$afterData.phase -ne "after") { throw "The second UAC witness must have phase 'after'." }

    foreach ($field in @(
        "schemaVersion", "kind", "product", "version", "architecture", "packageFile",
        "packageSha256", "packageManifestSchema", "entryPointSha256", "betaManualQaEntryPointSha256",
        "toolSha256", "checkId"
    )) {
        Assert-SameValue -Left $beforeData.$field -Right $afterData.$field -Label $field
    }

    if ([string]$beforeData.packageSha256 -cne [string]$PackageIdentity.packageSha256) { throw "Before witness is bound to a different candidate package." }
    if ([string]$afterData.packageSha256 -cne [string]$PackageIdentity.packageSha256) { throw "After witness is bound to a different candidate package." }
    if ([string]$beforeData.entryPointSha256 -cne [string]$PackageIdentity.entryPointSha256) { throw "Before witness desktop identity does not match the candidate package." }
    if ([string]$afterData.entryPointSha256 -cne [string]$PackageIdentity.entryPointSha256) { throw "After witness desktop identity does not match the candidate package." }
    if ([string]$beforeData.betaManualQaEntryPointSha256 -cne [string]$PackageIdentity.betaManualQaEntryPointSha256) { throw "Before witness manual-QA identity does not match the candidate package." }
    if ([string]$afterData.betaManualQaEntryPointSha256 -cne [string]$PackageIdentity.betaManualQaEntryPointSha256) { throw "After witness manual-QA identity does not match the candidate package." }
    if ([string]$beforeData.toolSha256 -cne [string]$PackageIdentity.betaUacWitnessEntryPointSha256) { throw "Before witness was not produced by the packaged UAC witness tool." }
    if ([string]$afterData.toolSha256 -cne [string]$PackageIdentity.betaUacWitnessEntryPointSha256) { throw "After witness was not produced by the packaged UAC witness tool." }

    foreach ($field in @(
        "osBuild", "processArchitecture", "sessionId", "enableLUA",
        "consentPromptBehaviorAdmin", "consentPromptBehaviorUser", "promptOnSecureDesktop",
        "filterAdministratorToken"
    )) {
        Assert-SameValue -Left $beforeData.environment.$field -Right $afterData.environment.$field -Label "environment.$field"
    }

    foreach ($field in @("imageLeaf", "imagePathSha256", "extension", "length")) {
        Assert-SameValue -Left $beforeData.diskImage.$field -Right $afterData.diskImage.$field -Label "diskImage.$field"
    }

    if ($AfterRecord.createdUtc -lt $BeforeRecord.createdUtc) {
        throw "UAC witness pair timestamps are reversed."
    }
    $elapsed = $AfterRecord.createdUtc - $BeforeRecord.createdUtc
    if ($elapsed.TotalMinutes -gt $MaximumMinutes) {
        throw "UAC witness pair exceeds the allowed continuity window of $MaximumMinutes minutes."
    }

    $check = [string]$beforeData.checkId
    $extension = ([string]$beforeData.diskImage.extension).ToLowerInvariant()
    $beforeAttached = [bool]$beforeData.diskImage.attached
    $afterAttached = [bool]$afterData.diskImage.attached

    switch ($check) {
        "uac.iso-no-prompt" {
            if ($extension -ne ".iso") { throw "uac.iso-no-prompt requires an ISO image." }
            if ($beforeAttached -or -not $afterAttached) { throw "ISO no-prompt witness must prove detached -> attached state." }
        }
        "uac.vhd-cancel" {
            if ($extension -ne ".vhd") { throw "uac.vhd-cancel requires a VHD image." }
            if ($beforeAttached -or $afterAttached) { throw "VHD cancel witness must remain detached before and after cancellation." }
        }
        "uac.vhdx-cancel" {
            if ($extension -ne ".vhdx") { throw "uac.vhdx-cancel requires a VHDX image." }
            if ($beforeAttached -or $afterAttached) { throw "VHDX cancel witness must remain detached before and after cancellation." }
        }
        "uac.vhd-approve-readonly" {
            if ($extension -ne ".vhd") { throw "uac.vhd-approve-readonly requires a VHD image." }
            if ($beforeAttached -or -not $afterAttached) { throw "VHD approval witness must prove detached -> attached state." }
            if ($null -eq $afterData.diskImage.diskIsReadOnly -or -not [bool]$afterData.diskImage.diskIsReadOnly) {
                throw "VHD approval witness must prove the attached disk is read-only."
            }
        }
        "uac.vhdx-approve-readonly" {
            if ($extension -ne ".vhdx") { throw "uac.vhdx-approve-readonly requires a VHDX image." }
            if ($beforeAttached -or -not $afterAttached) { throw "VHDX approval witness must prove detached -> attached state." }
            if ($null -eq $afterData.diskImage.diskIsReadOnly -or -not [bool]$afterData.diskImage.diskIsReadOnly) {
                throw "VHDX approval witness must prove the attached disk is read-only."
            }
        }
        "uac.unmount-refresh" {
            if (-not $beforeAttached -or $afterAttached) { throw "Unmount refresh witness must prove attached -> detached state." }
        }
        default {
            throw "Unsupported UAC witness check '$check'."
        }
    }

    Write-Host "Dragon DiskForge UAC before/after pair verification passed."
    Write-Host "Check: $check"
    Write-Host "Session: $($beforeData.environment.sessionId)"
    Write-Host "Elapsed: $([math]::Round($elapsed.TotalSeconds, 1)) seconds"
    Write-Host "Package SHA-256: $($PackageIdentity.packageSha256)"
    Write-Host "Human gate claimed: false"
}

function Invoke-Verification {
    param(
        [Parameter(Mandatory = $true)][string]$BeforePath,
        [Parameter(Mandatory = $true)][string]$AfterPath,
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][int]$MaximumMinutes,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $packageIdentity = Get-PackageIdentity -ZipPath $ZipPath -SidecarPath $SidecarPath -Version $Version
    $beforeRecord = Read-Witness -Path $BeforePath -Label "Before witness" -Version $Version
    $afterRecord = Read-Witness -Path $AfterPath -Label "After witness" -Version $Version
    Assert-Pair -BeforeRecord $beforeRecord -AfterRecord $afterRecord -PackageIdentity $packageIdentity -MaximumMinutes $MaximumMinutes
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    $rejected = $false
    try {
        & $Action
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw $FailureMessage
    }
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-uac-pair-selftest-" + [guid]::NewGuid().ToString("N"))
    $packageRoot = Join-Path $root "package"
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "tools") -Force | Out-Null

    try {
        $entry = Join-Path $packageRoot "DragonDiskForge.App.exe"
        $qa = Join-Path $packageRoot "tools/beta-manual-qa.ps1"
        $uac = Join-Path $packageRoot "tools/beta-qa-uac-witness.ps1"
        Write-Utf8NoBom -Path $entry -Text "dummy-desktop"
        Write-Utf8NoBom -Path $qa -Text "Write-Host 'dummy qa'"
        Write-Utf8NoBom -Path $uac -Text "Write-Host 'dummy uac witness'"

        $entryHash = (Get-FileHash -LiteralPath $entry -Algorithm SHA256).Hash.ToLowerInvariant()
        $qaHash = (Get-FileHash -LiteralPath $qa -Algorithm SHA256).Hash.ToLowerInvariant()
        $uacHash = (Get-FileHash -LiteralPath $uac -Algorithm SHA256).Hash.ToLowerInvariant()

        $manifest = [ordered]@{
            schemaVersion = 6
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            entryPoint = "DragonDiskForge.App.exe"
            entryPointSha256 = $entryHash
            betaManualQaEntryPoint = "tools/beta-manual-qa.ps1"
            betaManualQaEntryPointSha256 = $qaHash
            betaUacWitnessEntryPoint = "tools/beta-qa-uac-witness.ps1"
            betaUacWitnessEntryPointSha256 = $uacHash
        }
        Write-Utf8NoBom -Path (Join-Path $packageRoot "package-manifest.json") -Text (($manifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine)

        $zip = Join-Path $root "DragonDiskForge-win-x64.zip"
        Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zip -Force
        $zipHash = Write-Sha256Sidecar -Path $zip

        $environment = [ordered]@{
            osBuild = "26100"
            processArchitecture = "X64"
            userInteractive = $true
            processElevated = $false
            sessionId = 2
            enableLUA = 1
            consentPromptBehaviorAdmin = 5
            consentPromptBehaviorUser = 3
            promptOnSecureDesktop = 1
            filterAdministratorToken = 0
        }

        $base = [ordered]@{
            schemaVersion = 1
            kind = $Script:WitnessKind
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            packageFile = [System.IO.Path]::GetFileName($zip)
            packageSha256 = $zipHash
            packageManifestSchema = 6
            entryPointSha256 = $entryHash
            betaManualQaEntryPointSha256 = $qaHash
            toolSha256 = $uacHash
            checkId = "uac.vhdx-approve-readonly"
            phase = "before"
            createdUtc = "2026-09-20T00:00:00Z"
            environment = $environment
            diskImage = [ordered]@{
                imageLeaf = "fixture.vhdx"
                imagePathSha256 = ('a' * 64)
                extension = ".vhdx"
                length = 1048576
                attached = $false
                diskIsReadOnly = $null
            }
            humanGateClaimed = $false
            publicRelease = $false
        }

        $beforePath = Join-Path $root "before.json"
        $afterPath = Join-Path $root "after.json"
        Write-Utf8NoBom -Path $beforePath -Text (($base | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $null = Write-Sha256Sidecar -Path $beforePath

        $after = $base | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        $after.phase = "after"
        $after.createdUtc = "2026-09-20T00:05:00Z"
        $after.diskImage.attached = $true
        $after.diskImage.diskIsReadOnly = $true
        Write-Utf8NoBom -Path $afterPath -Text (($after | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $null = Write-Sha256Sidecar -Path $afterPath

        Invoke-Verification -BeforePath $beforePath -AfterPath $afterPath -ZipPath $zip -SidecarPath "$zip.sha256" -MaximumMinutes 60 -Version "0.5.0-beta.1"

        $badAfter = $after | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        $badAfter.environment.sessionId = 3
        Write-Utf8NoBom -Path $afterPath -Text (($badAfter | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $null = Write-Sha256Sidecar -Path $afterPath
        Assert-Rejected -FailureMessage "Self-test failed: cross-session pair was accepted." -Action {
            Invoke-Verification -BeforePath $beforePath -AfterPath $afterPath -ZipPath $zip -MaximumMinutes 60 -Version "0.5.0-beta.1"
        }

        $badAfter = $after | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        $badAfter.diskImage.attached = $false
        Write-Utf8NoBom -Path $afterPath -Text (($badAfter | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $null = Write-Sha256Sidecar -Path $afterPath
        Assert-Rejected -FailureMessage "Self-test failed: invalid state transition was accepted." -Action {
            Invoke-Verification -BeforePath $beforePath -AfterPath $afterPath -ZipPath $zip -MaximumMinutes 60 -Version "0.5.0-beta.1"
        }

        $badAfter = $after | ConvertTo-Json -Depth 10 | ConvertFrom-Json
        $badAfter.createdUtc = "2026-09-19T23:59:59Z"
        Write-Utf8NoBom -Path $afterPath -Text (($badAfter | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $null = Write-Sha256Sidecar -Path $afterPath
        Assert-Rejected -FailureMessage "Self-test failed: reversed timestamps were accepted." -Action {
            Invoke-Verification -BeforePath $beforePath -AfterPath $afterPath -ZipPath $zip -MaximumMinutes 60 -Version "0.5.0-beta.1"
        }

        Write-Utf8NoBom -Path $afterPath -Text (($after | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $null = Write-Sha256Sidecar -Path $afterPath
        Add-Content -LiteralPath $afterPath -Value "tampered"
        Assert-Rejected -FailureMessage "Self-test failed: tampered witness was accepted." -Action {
            Invoke-Verification -BeforePath $beforePath -AfterPath $afterPath -ZipPath $zip -MaximumMinutes 60 -Version "0.5.0-beta.1"
        }

        Write-Host "Dragon DiskForge UAC before/after pair verifier self-test passed."
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Mode -eq "self-test") {
    Invoke-SelfTest
    exit 0
}

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    throw "-PackagePath is required for UAC pair verification."
}

Invoke-Verification -BeforePath $BeforeEvidencePath -AfterPath $AfterEvidencePath -ZipPath $PackagePath -SidecarPath $ChecksumFile -MaximumMinutes $MaxPairMinutes -Version $ExpectedVersion
