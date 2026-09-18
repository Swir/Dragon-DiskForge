[CmdletBinding()]
param(
    [string]$ManifestPath = "beta-qa-witness-kit.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-ExactCommit {
    param([Parameter(Mandatory = $true)][string]$Commit)
    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Witness companion source commit must be an exact 40-character Git SHA."
    }
    return $Commit.ToLowerInvariant()
}

function Assert-SafeLeafName {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "$Label file name is empty."
    }
    if ([System.IO.Path]::GetFileName($Name) -ne $Name -or $Name.Contains('/') -or $Name.Contains('\')) {
        throw "$Label must be a leaf file name without path traversal."
    }
    return $Name
}

function Assert-FileSidecar {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $file = (Resolve-Path -LiteralPath $FilePath).Path
    $sidecar = (Resolve-Path -LiteralPath "$file.sha256").Path
    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "$Label SHA-256 sidecar has an invalid format."
    }

    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    $fileName = [System.IO.Path]::GetFileName($file)
    if ($declaredName -ne $fileName) {
        throw "$Label SHA-256 sidecar targets '$declaredName' instead of '$fileName'."
    }

    $actualHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "$Label SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'."
    }

    return [pscustomobject]@{
        path = $file
        fileName = $fileName
        sha256 = $actualHash
    }
}

$runningVerifier = Assert-FileSidecar -FilePath $PSCommandPath -Label "Witness companion verifier"
$manifestProof = Assert-FileSidecar -FilePath $ManifestPath -Label "Witness companion manifest"
$manifest = Get-Content -LiteralPath $manifestProof.path -Raw | ConvertFrom-Json

if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported beta QA witness companion schema '$($manifest.schemaVersion)'; expected schema 1." }
if ([string]$manifest.kind -ne "DragonDiskForgeBetaQaWitnessKit") { throw "Unexpected witness companion kind '$($manifest.kind)'." }
if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected witness companion product '$($manifest.product)'." }
if ([string]$manifest.version -ne "0.5.0-beta.1") { throw "Unexpected witness companion version '$($manifest.version)'." }
if ([string]$manifest.architecture -ne "x64") { throw "Witness companion architecture must be x64." }
$sourceCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit)
if ([string]$manifest.workflowRunId -notmatch '^[0-9]+$') { throw "Witness companion workflowRunId is missing or invalid." }
if ([bool]$manifest.publicRelease) { throw "Witness companion must remain non-public retained-candidate evidence." }
if ([bool]$manifest.humanGateClaimed) { throw "Witness companion may never claim a human beta gate passed." }
if ([string]$manifest.packageSha256 -notmatch '^[0-9a-f]{64}$') { throw "Witness companion package SHA-256 is missing or invalid." }

$directory = Split-Path -Parent $manifestProof.path
$coreManifestName = Assert-SafeLeafName -Name ([string]$manifest.qaKitManifestFile) -Label "Core QA kit manifest"
$verifierName = Assert-SafeLeafName -Name ([string]$manifest.verifierFile) -Label "Witness companion verifier"
$helperName = Assert-SafeLeafName -Name ([string]$manifest.desktopWitnessHelperFile) -Label "Desktop witness helper"
$guideName = Assert-SafeLeafName -Name ([string]$manifest.desktopWitnessGuideFile) -Label "Desktop witness guide"

if ($runningVerifier.fileName -ne $verifierName) { throw "Running witness verifier file name does not match the manifest." }
if ($runningVerifier.sha256 -ne ([string]$manifest.verifierSha256).ToLowerInvariant()) { throw "Running witness verifier SHA-256 does not match the manifest." }

$coreProof = Assert-FileSidecar -FilePath (Join-Path $directory $coreManifestName) -Label "Core QA kit manifest"
$helperProof = Assert-FileSidecar -FilePath (Join-Path $directory $helperName) -Label "Desktop witness helper"
$guideProof = Assert-FileSidecar -FilePath (Join-Path $directory $guideName) -Label "Desktop witness guide"

if ($coreProof.sha256 -ne ([string]$manifest.qaKitManifestSha256).ToLowerInvariant()) { throw "Core QA kit manifest SHA-256 does not match the witness companion." }
if ($helperProof.sha256 -ne ([string]$manifest.desktopWitnessHelperSha256).ToLowerInvariant()) { throw "Desktop witness helper SHA-256 does not match the witness companion." }
if ($guideProof.sha256 -ne ([string]$manifest.desktopWitnessGuideSha256).ToLowerInvariant()) { throw "Desktop witness guide SHA-256 does not match the witness companion." }

$core = Get-Content -LiteralPath $coreProof.path -Raw | ConvertFrom-Json
if ([int]$core.schemaVersion -ne 2) { throw "Witness companion requires core beta QA kit schema 2." }
if ([string]$core.kind -ne "DragonDiskForgeBetaQaKit" -or [string]$core.product -ne "Dragon DiskForge") { throw "Core QA kit identity is invalid." }
if ([string]$core.version -ne [string]$manifest.version -or [string]$core.architecture -ne [string]$manifest.architecture) { throw "Witness companion version/architecture does not match the core QA kit." }
if ((Assert-ExactCommit -Commit ([string]$core.sourceCommit)) -ne $sourceCommit) { throw "Witness companion source commit does not match the core QA kit." }
if ([string]$core.workflowRunId -ne [string]$manifest.workflowRunId) { throw "Witness companion workflow run does not match the core QA kit." }
if ([bool]$core.publicRelease) { throw "Core QA kit unexpectedly claims a public release." }
if ([string]$core.packageFile -ne [string]$manifest.packageFile) { throw "Witness companion package file does not match the core QA kit." }
if (([string]$core.packageSha256).ToLowerInvariant() -ne ([string]$manifest.packageSha256).ToLowerInvariant()) { throw "Witness companion package SHA-256 does not match the core QA kit." }

Write-Host "Dragon DiskForge beta QA witness companion verification passed."
Write-Host "Version: $($manifest.version) / $($manifest.architecture)"
Write-Host "Source commit: $sourceCommit"
Write-Host "Workflow run: $($manifest.workflowRunId)"
Write-Host "Package SHA-256: $($manifest.packageSha256)"
Write-Host "Core QA kit manifest SHA-256: $($coreProof.sha256)"
Write-Host "Desktop witness helper SHA-256: $($helperProof.sha256)"
Write-Host "Desktop witness guide SHA-256: $($guideProof.sha256)"
Write-Host "Human gate claimed: false"