[CmdletBinding()]
param(
    [string]$ManifestPath = "beta-qa-kit.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-ExactCommit {
    param([Parameter(Mandatory = $true)][string]$Commit)
    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "QA kit source commit must be an exact 40-character Git SHA."
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
    if ([System.IO.Path]::GetFileName($Name) -ne $Name -or $Name.Contains('/') -or $Name.Contains('\\')) {
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

$manifestProof = Assert-FileSidecar -FilePath $ManifestPath -Label "QA kit manifest"
$manifest = Get-Content -LiteralPath $manifestProof.path -Raw | ConvertFrom-Json

if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported beta QA kit schema '$($manifest.schemaVersion)'." }
if ([string]$manifest.kind -ne "DragonDiskForgeBetaQaKit") { throw "Unexpected beta QA kit kind '$($manifest.kind)'." }
if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected beta QA kit product '$($manifest.product)'." }
if ([string]$manifest.version -ne "0.5.0-beta.1") { throw "Unexpected beta QA kit version '$($manifest.version)'." }
if ([string]$manifest.architecture -ne "x64") { throw "Beta QA kit architecture must be x64." }
$sourceCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit)
if ([string]$manifest.workflowRunId -notmatch '^[0-9]+$') { throw "QA kit workflowRunId is missing or invalid." }
if ([bool]$manifest.publicRelease) { throw "QA kit must describe a retained non-public candidate, not a public release." }

$directory = Split-Path -Parent $manifestProof.path
$packageName = Assert-SafeLeafName -Name ([string]$manifest.packageFile) -Label "Package"
$metadataName = Assert-SafeLeafName -Name ([string]$manifest.candidateMetadataFile) -Label "Candidate metadata"
$sessionName = Assert-SafeLeafName -Name ([string]$manifest.sessionHelperFile) -Label "Session helper"
$manualName = Assert-SafeLeafName -Name ([string]$manifest.manualValidationFile) -Label "Manual validation guide"
$verifierName = Assert-SafeLeafName -Name ([string]$manifest.verifierFile) -Label "QA kit verifier"

$packageProof = Assert-FileSidecar -FilePath (Join-Path $directory $packageName) -Label "Candidate package"
$metadataProof = Assert-FileSidecar -FilePath (Join-Path $directory $metadataName) -Label "Candidate metadata"
$sessionProof = Assert-FileSidecar -FilePath (Join-Path $directory $sessionName) -Label "Session helper"
$manualProof = Assert-FileSidecar -FilePath (Join-Path $directory $manualName) -Label "Manual validation guide"
$verifierProof = Assert-FileSidecar -FilePath (Join-Path $directory $verifierName) -Label "QA kit verifier"

if ($packageProof.sha256 -ne ([string]$manifest.packageSha256).ToLowerInvariant()) { throw "QA kit package SHA-256 does not match its manifest." }
if ($metadataProof.sha256 -ne ([string]$manifest.candidateMetadataSha256).ToLowerInvariant()) { throw "QA kit candidate-metadata SHA-256 does not match its manifest." }
if ($sessionProof.sha256 -ne ([string]$manifest.sessionHelperSha256).ToLowerInvariant()) { throw "QA kit session-helper SHA-256 does not match its manifest." }
if ($manualProof.sha256 -ne ([string]$manifest.manualValidationSha256).ToLowerInvariant()) { throw "QA kit manual-validation SHA-256 does not match its manifest." }
if ($verifierProof.sha256 -ne ([string]$manifest.verifierSha256).ToLowerInvariant()) { throw "QA kit verifier SHA-256 does not match its manifest." }

$currentVerifier = (Resolve-Path -LiteralPath $PSCommandPath).Path
$currentVerifierHash = (Get-FileHash -LiteralPath $currentVerifier -Algorithm SHA256).Hash.ToLowerInvariant()
if ($currentVerifierHash -ne $verifierProof.sha256) {
    throw "The running QA kit verifier is not the hash-bound verifier shipped with this kit."
}

$candidate = Get-Content -LiteralPath $metadataProof.path -Raw | ConvertFrom-Json
if ([int]$candidate.schemaVersion -ne 1 -or [string]$candidate.kind -ne "DragonDiskForgeBetaCandidate") {
    throw "QA kit candidate metadata has an unsupported identity."
}
if ([string]$candidate.product -ne "Dragon DiskForge" -or [string]$candidate.version -ne [string]$manifest.version -or [string]$candidate.architecture -ne [string]$manifest.architecture) {
    throw "QA kit candidate product/version/architecture does not match its manifest."
}
if ((Assert-ExactCommit -Commit ([string]$candidate.sourceCommit)) -ne $sourceCommit) { throw "QA kit source commit does not match candidate metadata." }
if ([string]$candidate.workflowRunId -ne [string]$manifest.workflowRunId) { throw "QA kit workflow run does not match candidate metadata." }
if ([bool]$candidate.publicRelease) { throw "Candidate metadata unexpectedly claims a public release." }
if ([string]$candidate.packageFile -ne $packageName) { throw "QA kit package file does not match candidate metadata." }
if ([string]$candidate.packageSha256 -ne $packageProof.sha256) { throw "QA kit package hash does not match candidate metadata." }
if ([string]$candidate.entryPointSha256 -ne [string]$manifest.entryPointSha256) { throw "QA kit desktop entry-point hash does not match candidate metadata." }
if ([string]$candidate.betaManualQaEntryPointSha256 -ne [string]$manifest.betaManualQaEntryPointSha256) { throw "QA kit packaged manual-QA tool hash does not match candidate metadata." }

Write-Host "Dragon DiskForge beta QA kit verification passed."
Write-Host "Version: $($manifest.version) / $($manifest.architecture)"
Write-Host "Source commit: $sourceCommit"
Write-Host "Workflow run: $($manifest.workflowRunId)"
Write-Host "Package SHA-256: $($packageProof.sha256)"
Write-Host "Session helper SHA-256: $($sessionProof.sha256)"
exit 0
