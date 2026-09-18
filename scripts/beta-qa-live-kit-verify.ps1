[CmdletBinding()]
param(
    [string]$ManifestPath = "beta-qa-live-kit.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-ExactCommit {
    param([Parameter(Mandatory = $true)][string]$Commit)
    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Live-session companion source commit must be an exact 40-character Git SHA."
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

$runningVerifier = Assert-FileSidecar -FilePath $PSCommandPath -Label "Live-session companion verifier"
$manifestProof = Assert-FileSidecar -FilePath $ManifestPath -Label "Live-session companion manifest"
$manifest = Get-Content -LiteralPath $manifestProof.path -Raw | ConvertFrom-Json

if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported beta QA live-session companion schema '$($manifest.schemaVersion)'; expected schema 1." }
if ([string]$manifest.kind -ne "DragonDiskForgeBetaQaLiveKit") { throw "Unexpected live-session companion kind '$($manifest.kind)'." }
if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected live-session companion product '$($manifest.product)'." }
if ([string]$manifest.version -ne "0.5.0-beta.1") { throw "Unexpected live-session companion version '$($manifest.version)'." }
if ([string]$manifest.architecture -ne "x64") { throw "Live-session companion architecture must be x64." }
$sourceCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit)
if ([string]$manifest.workflowRunId -notmatch '^[0-9]+$') { throw "Live-session companion workflowRunId is missing or invalid." }
if ([bool]$manifest.publicRelease) { throw "Live-session companion must remain non-public retained-candidate evidence." }
if ([bool]$manifest.humanGateClaimed) { throw "Live-session companion may never claim a human beta gate passed." }
if ([string]$manifest.packageSha256 -notmatch '^[0-9a-f]{64}$') { throw "Live-session companion package SHA-256 is missing or invalid." }

$directory = Split-Path -Parent $manifestProof.path
$coreManifestName = Assert-SafeLeafName -Name ([string]$manifest.qaKitManifestFile) -Label "Core QA kit manifest"
$verifierName = Assert-SafeLeafName -Name ([string]$manifest.verifierFile) -Label "Live-session companion verifier"
$helperName = Assert-SafeLeafName -Name ([string]$manifest.liveSessionHelperFile) -Label "Live-session continuity helper"

if ($runningVerifier.fileName -ne $verifierName) { throw "Running live-session verifier file name does not match the manifest." }
if ($runningVerifier.sha256 -ne ([string]$manifest.verifierSha256).ToLowerInvariant()) { throw "Running live-session verifier SHA-256 does not match the manifest." }

$coreProof = Assert-FileSidecar -FilePath (Join-Path $directory $coreManifestName) -Label "Core QA kit manifest"
$helperProof = Assert-FileSidecar -FilePath (Join-Path $directory $helperName) -Label "Live-session continuity helper"

if ($coreProof.sha256 -ne ([string]$manifest.qaKitManifestSha256).ToLowerInvariant()) { throw "Core QA kit manifest SHA-256 does not match the live-session companion." }
if ($helperProof.sha256 -ne ([string]$manifest.liveSessionHelperSha256).ToLowerInvariant()) { throw "Live-session helper SHA-256 does not match the live-session companion." }

$core = Get-Content -LiteralPath $coreProof.path -Raw | ConvertFrom-Json
if ([int]$core.schemaVersion -ne 2) { throw "Live-session companion requires core beta QA kit schema 2." }
if ([string]$core.kind -ne "DragonDiskForgeBetaQaKit" -or [string]$core.product -ne "Dragon DiskForge") { throw "Core QA kit identity is invalid." }
if ([string]$core.version -ne [string]$manifest.version -or [string]$core.architecture -ne [string]$manifest.architecture) { throw "Live-session companion version/architecture does not match the core QA kit." }
if ((Assert-ExactCommit -Commit ([string]$core.sourceCommit)) -ne $sourceCommit) { throw "Live-session companion source commit does not match the core QA kit." }
if ([string]$core.workflowRunId -ne [string]$manifest.workflowRunId) { throw "Live-session companion workflow run does not match the core QA kit." }
if ([bool]$core.publicRelease) { throw "Core QA kit unexpectedly claims a public release." }
if ([string]$core.packageFile -ne [string]$manifest.packageFile) { throw "Live-session companion package file does not match the core QA kit." }
if (([string]$core.packageSha256).ToLowerInvariant() -ne ([string]$manifest.packageSha256).ToLowerInvariant()) { throw "Live-session companion package SHA-256 does not match the core QA kit." }

Write-Host "Dragon DiskForge beta QA live-session companion verification passed."
Write-Host "Version: $($manifest.version) / $($manifest.architecture)"
Write-Host "Source commit: $sourceCommit"
Write-Host "Workflow run: $($manifest.workflowRunId)"
Write-Host "Package SHA-256: $($manifest.packageSha256)"
Write-Host "Core QA kit manifest SHA-256: $($coreProof.sha256)"
Write-Host "Live-session helper SHA-256: $($helperProof.sha256)"
Write-Host "Human gate claimed: false"
