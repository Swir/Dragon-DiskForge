[CmdletBinding()]
param(
    [string]$ManifestPath = "beta-qa-archive-kit.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-ExactCommit {
    param([Parameter(Mandatory = $true)][string]$Commit)
    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Archive kit source commit must be an exact 40-character Git SHA."
    }
    return $Commit.ToLowerInvariant()
}

function Assert-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Value, [Parameter(Mandatory = $true)][string]$Label)
    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Label must be a 64-character SHA-256 value."
    }
    return $Value.ToLowerInvariant()
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

$runningVerifier = Assert-FileSidecar -FilePath $PSCommandPath -Label "Archive kit verifier"
$manifestProof = Assert-FileSidecar -FilePath $ManifestPath -Label "Archive kit manifest"
$manifest = Get-Content -LiteralPath $manifestProof.path -Raw | ConvertFrom-Json

if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported beta QA archive kit schema '$($manifest.schemaVersion)'; expected schema 1." }
if ([string]$manifest.kind -ne "DragonDiskForgeBetaQaArchiveKit") { throw "Unexpected archive kit kind '$($manifest.kind)'." }
if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected archive kit product '$($manifest.product)'." }
if ([string]$manifest.version -ne "0.5.0-beta.1") { throw "Unexpected archive kit version '$($manifest.version)'." }
if ([string]$manifest.architecture -ne "x64") { throw "Archive kit architecture must be x64." }
$sourceCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit)
if ([string]$manifest.workflowRunId -notmatch '^[1-9][0-9]*$') { throw "Archive kit workflowRunId is missing or invalid." }
if ([bool]$manifest.publicRelease) { throw "Archive kit must remain non-public retained-candidate evidence." }
if ([bool]$manifest.humanGateClaimed) { throw "Archive kit may never claim a human beta gate passed." }
$packageSha256 = Assert-Sha256 -Value ([string]$manifest.packageSha256) -Label "Archive kit package SHA-256"

$directory = Split-Path -Parent $manifestProof.path
$coreManifestName = Assert-SafeLeafName -Name ([string]$manifest.qaKitManifestFile) -Label "Core QA kit manifest"
$witnessManifestName = Assert-SafeLeafName -Name ([string]$manifest.witnessKitManifestFile) -Label "Desktop witness kit manifest"
$verifierName = Assert-SafeLeafName -Name ([string]$manifest.verifierFile) -Label "Archive kit verifier"
$helperName = Assert-SafeLeafName -Name ([string]$manifest.archiveHelperFile) -Label "Evidence archive helper"
$guideName = Assert-SafeLeafName -Name ([string]$manifest.archiveGuideFile) -Label "Evidence archive guide"

if ($runningVerifier.fileName -ne $verifierName) { throw "Running archive verifier file name does not match the manifest." }
if ($runningVerifier.sha256 -ne (Assert-Sha256 -Value ([string]$manifest.verifierSha256) -Label "Archive kit verifier SHA-256")) {
    throw "Running archive verifier SHA-256 does not match the manifest."
}

$coreProof = Assert-FileSidecar -FilePath (Join-Path $directory $coreManifestName) -Label "Core QA kit manifest"
$witnessProof = Assert-FileSidecar -FilePath (Join-Path $directory $witnessManifestName) -Label "Desktop witness kit manifest"
$helperProof = Assert-FileSidecar -FilePath (Join-Path $directory $helperName) -Label "Evidence archive helper"
$guideProof = Assert-FileSidecar -FilePath (Join-Path $directory $guideName) -Label "Evidence archive guide"

if ($coreProof.sha256 -ne (Assert-Sha256 -Value ([string]$manifest.qaKitManifestSha256) -Label "Core QA kit manifest SHA-256")) {
    throw "Core QA kit manifest SHA-256 does not match the archive kit."
}
if ($witnessProof.sha256 -ne (Assert-Sha256 -Value ([string]$manifest.witnessKitManifestSha256) -Label "Desktop witness kit manifest SHA-256")) {
    throw "Desktop witness kit manifest SHA-256 does not match the archive kit."
}
if ($helperProof.sha256 -ne (Assert-Sha256 -Value ([string]$manifest.archiveHelperSha256) -Label "Evidence archive helper SHA-256")) {
    throw "Evidence archive helper SHA-256 does not match the archive kit."
}
if ($guideProof.sha256 -ne (Assert-Sha256 -Value ([string]$manifest.archiveGuideSha256) -Label "Evidence archive guide SHA-256")) {
    throw "Evidence archive guide SHA-256 does not match the archive kit."
}

$core = Get-Content -LiteralPath $coreProof.path -Raw | ConvertFrom-Json
if ([int]$core.schemaVersion -ne 2 -or [string]$core.kind -ne "DragonDiskForgeBetaQaKit" -or [string]$core.product -ne "Dragon DiskForge") {
    throw "Core QA kit identity/schema is invalid."
}
if ([bool]$core.publicRelease) { throw "Core QA kit unexpectedly claims a public release." }
if ([string]$core.version -ne [string]$manifest.version -or [string]$core.architecture -ne [string]$manifest.architecture) {
    throw "Archive kit version/architecture does not match the core QA kit."
}
if ((Assert-ExactCommit -Commit ([string]$core.sourceCommit)) -ne $sourceCommit) { throw "Archive kit source commit does not match the core QA kit." }
if ([string]$core.workflowRunId -ne [string]$manifest.workflowRunId) { throw "Archive kit workflow run does not match the core QA kit." }
if ([string]$core.packageFile -ne [string]$manifest.packageFile) { throw "Archive kit package file does not match the core QA kit." }
if ((Assert-Sha256 -Value ([string]$core.packageSha256) -Label "Core QA kit package SHA-256") -ne $packageSha256) {
    throw "Archive kit package SHA-256 does not match the core QA kit."
}

$witness = Get-Content -LiteralPath $witnessProof.path -Raw | ConvertFrom-Json
if ([int]$witness.schemaVersion -ne 1 -or [string]$witness.kind -ne "DragonDiskForgeBetaQaWitnessKit" -or [string]$witness.product -ne "Dragon DiskForge") {
    throw "Desktop witness kit identity/schema is invalid."
}
if ([bool]$witness.publicRelease -or [bool]$witness.humanGateClaimed) {
    throw "Desktop witness kit contains an unsafe release/human-gate claim."
}
if ([string]$witness.version -ne [string]$manifest.version -or [string]$witness.architecture -ne [string]$manifest.architecture) {
    throw "Archive kit version/architecture does not match the desktop witness kit."
}
if ((Assert-ExactCommit -Commit ([string]$witness.sourceCommit)) -ne $sourceCommit) { throw "Archive kit source commit does not match the desktop witness kit." }
if ([string]$witness.workflowRunId -ne [string]$manifest.workflowRunId) { throw "Archive kit workflow run does not match the desktop witness kit." }
if ([string]$witness.packageFile -ne [string]$manifest.packageFile) { throw "Archive kit package file does not match the desktop witness kit." }
if ((Assert-Sha256 -Value ([string]$witness.packageSha256) -Label "Desktop witness kit package SHA-256") -ne $packageSha256) {
    throw "Archive kit package SHA-256 does not match the desktop witness kit."
}
if ((Assert-Sha256 -Value ([string]$witness.qaKitManifestSha256) -Label "Witness-bound core QA kit SHA-256") -ne $coreProof.sha256) {
    throw "Desktop witness kit is not bound to the same core QA kit manifest."
}

Write-Host "Dragon DiskForge beta QA portable evidence-archive kit verification passed."
Write-Host "Version: $($manifest.version) / $($manifest.architecture)"
Write-Host "Source commit: $sourceCommit"
Write-Host "Workflow run: $($manifest.workflowRunId)"
Write-Host "Package SHA-256: $packageSha256"
Write-Host "Core QA kit manifest SHA-256: $($coreProof.sha256)"
Write-Host "Desktop witness kit manifest SHA-256: $($witnessProof.sha256)"
Write-Host "Evidence archive helper SHA-256: $($helperProof.sha256)"
Write-Host "Evidence archive guide SHA-256: $($guideProof.sha256)"
Write-Host "Human gate claimed: false"