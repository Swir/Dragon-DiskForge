[CmdletBinding()]
param(
    [ValidateSet("verify", "self-test")]
    [string]$Mode = "verify",
    [string]$EvidencePath = "docs/retained-beta-candidate.json",
    [string]$RepositoryRoot = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$StartMarker = '<!-- retained-beta-candidate:start -->'
$EndMarker = '<!-- retained-beta-candidate:end -->'

function Resolve-RepositoryRoot {
    param([string]$RequestedRoot)
    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        return (Resolve-Path -LiteralPath $RequestedRoot).Path
    }
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}

function Assert-HexSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )
    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Label must be a 64-character SHA-256 value."
    }
    return $Value.ToLowerInvariant()
}

function Read-ArchiveBoundEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained candidate evidence is missing: $Path"
    }

    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -lt 2) {
        throw "Archive binding requires witness-capable retained evidence schema 2 or newer."
    }
    if ([string]$evidence.kind -ne "DragonDiskForgeRetainedBetaCandidateEvidence") {
        throw "Unexpected retained candidate evidence kind."
    }
    if ([string]$evidence.product -ne "Dragon DiskForge") {
        throw "Unexpected retained candidate product."
    }
    if ([string]$evidence.version -ne "0.5.0-beta.1") {
        throw "Unexpected retained candidate version '$($evidence.version)'."
    }
    if ([string]$evidence.architecture -ne "x64") {
        throw "Retained candidate architecture must be x64."
    }
    if ([string]$evidence.sourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "sourceCommit must be an exact 40-character Git SHA."
    }
    if ([string]$evidence.workflowRunId -notmatch '^[1-9][0-9]*$') {
        throw "workflowRunId must be a positive integer string."
    }
    if ([int64]$evidence.workflowRunNumber -le 0) {
        throw "workflowRunNumber must be positive."
    }

    $expectedArtifactName = "DragonDiskForge-$($evidence.version)-win-x64-candidate-$($evidence.workflowRunId)"
    if ([string]$evidence.artifactName -ne $expectedArtifactName) {
        throw "artifactName does not match the version/run identity."
    }

    if ([int]$evidence.archiveKitSchema -ne 1) {
        throw "Retained candidate must bind portable evidence archive kit schema 1."
    }

    $archiveManifest = Assert-HexSha256 -Value ([string]$evidence.archiveKitManifestSha256) -Label "archiveKitManifestSha256"
    $archiveVerifier = Assert-HexSha256 -Value ([string]$evidence.archiveVerifierSha256) -Label "archiveVerifierSha256"
    $archiveHelper = Assert-HexSha256 -Value ([string]$evidence.archiveHelperSha256) -Label "archiveHelperSha256"
    $archiveGuide = Assert-HexSha256 -Value ([string]$evidence.archiveGuideSha256) -Label "archiveGuideSha256"
    $packageSha = Assert-HexSha256 -Value ([string]$evidence.packageSha256) -Label "packageSha256"

    if ([bool]$evidence.publicRelease) {
        throw "Archive-bound retained candidate must not claim a public release."
    }
    if ([bool]$evidence.betaReady) {
        throw "Archive binding must not claim beta readiness while interactive gates remain open."
    }

    return [pscustomobject]@{
        version = [string]$evidence.version
        sourceCommit = ([string]$evidence.sourceCommit).ToLowerInvariant()
        workflowRunId = [string]$evidence.workflowRunId
        workflowRunNumber = [int64]$evidence.workflowRunNumber
        artifactName = [string]$evidence.artifactName
        packageSha256 = $packageSha
        archiveKitSchema = [int]$evidence.archiveKitSchema
        archiveKitManifestSha256 = $archiveManifest
        archiveVerifierSha256 = $archiveVerifier
        archiveHelperSha256 = $archiveHelper
        archiveGuideSha256 = $archiveGuide
    }
}

function Get-RetainedBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $pattern = '(?s)' + [regex]::Escape($StartMarker) + '(.*?)' + [regex]::Escape($EndMarker)
    $matches = [regex]::Matches($Text, $pattern)
    if ($matches.Count -ne 1) {
        throw "$Path must contain exactly one retained beta candidate block; found $($matches.Count)."
    }
    return $matches[0].Groups[1].Value
}

function Test-ArchiveDocumentation {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)]$Evidence
    )

    $tokens = @(
        ("Beta Candidate run #{0}" -f $Evidence.workflowRunNumber),
        $Evidence.workflowRunId,
        $Evidence.artifactName,
        $Evidence.sourceCommit,
        $Evidence.packageSha256
    )

    $markedDocs = @(
        "README.md",
        "docs/ROADMAP.md",
        "docs/STATUS.md",
        "docs/MILESTONES.md",
        "docs/BETA-RELEASE.md"
    )

    foreach ($relativePath in $markedDocs) {
        $path = Join-Path $Root $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required retained-candidate document is missing: $relativePath"
        }

        $block = Get-RetainedBlock -Text (Get-Content -LiteralPath $path -Raw) -Path $relativePath
        foreach ($token in $tokens) {
            if ($block.IndexOf([string]$token, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                throw "$relativePath retained-candidate block is stale; missing '$token'."
            }
        }
        if ($block -notmatch '(?i)archive-bound|evidence-archive companion|archive companion') {
            throw "$relativePath retained-candidate block must state the portable archive binding."
        }
        if ($block -notmatch '(?i)not\s+(a\s+)?public\s+release|non-public') {
            throw "$relativePath retained-candidate block must remain explicitly non-public."
        }
    }

    $candidateDoc = Join-Path $Root "docs/BETA-CANDIDATE.md"
    if (-not (Test-Path -LiteralPath $candidateDoc -PathType Leaf)) {
        throw "docs/BETA-CANDIDATE.md is missing."
    }
    $candidateText = Get-Content -LiteralPath $candidateDoc -Raw
    foreach ($token in $tokens) {
        if ($candidateText.IndexOf([string]$token, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "docs/BETA-CANDIDATE.md current checkpoint is stale; missing '$token'."
        }
    }
    if ($candidateText -notmatch '(?i)archive-bound|evidence-archive companion|archive companion') {
        throw "docs/BETA-CANDIDATE.md must describe the portable archive binding for the current checkpoint."
    }
}

function Invoke-SelfTest {
    $root = Resolve-RepositoryRoot -RequestedRoot $RepositoryRoot
    $evidenceFile = if ([System.IO.Path]::IsPathRooted($EvidencePath)) { $EvidencePath } else { Join-Path $root $EvidencePath }

    $proof = Read-ArchiveBoundEvidence -Path $evidenceFile
    Test-ArchiveDocumentation -Root $root -Evidence $proof

    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-retained-archive-contract-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    try {
        $goodPath = Join-Path $workspace "good.json"
        Copy-Item -LiteralPath $evidenceFile -Destination $goodPath
        $null = Read-ArchiveBoundEvidence -Path $goodPath

        $badHash = Join-Path $workspace "bad-hash.json"
        $data = Get-Content -LiteralPath $goodPath -Raw | ConvertFrom-Json
        $data.archiveHelperSha256 = "00"
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badHash -Encoding UTF8
        $rejected = $false
        try { $null = Read-ArchiveBoundEvidence -Path $badHash } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: invalid archive helper SHA-256 was accepted." }

        $badSchema = Join-Path $workspace "bad-schema.json"
        $data = Get-Content -LiteralPath $goodPath -Raw | ConvertFrom-Json
        $data.archiveKitSchema = 2
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badSchema -Encoding UTF8
        $rejected = $false
        try { $null = Read-ArchiveBoundEvidence -Path $badSchema } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: unsupported archive-kit schema was accepted." }

        $badReady = Join-Path $workspace "bad-ready.json"
        $data = Get-Content -LiteralPath $goodPath -Raw | ConvertFrom-Json
        $data.betaReady = $true
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badReady -Encoding UTF8
        $rejected = $false
        try { $null = Read-ArchiveBoundEvidence -Path $badReady } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: unsafe beta-ready archive evidence was accepted." }

        Write-Host "Retained beta archive-binding contract self-test passed."
    }
    finally {
        Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Mode -eq "self-test") {
    Invoke-SelfTest
    exit 0
}

$root = Resolve-RepositoryRoot -RequestedRoot $RepositoryRoot
$evidenceFile = if ([System.IO.Path]::IsPathRooted($EvidencePath)) { $EvidencePath } else { Join-Path $root $EvidencePath }
$proof = Read-ArchiveBoundEvidence -Path $evidenceFile
Test-ArchiveDocumentation -Root $root -Evidence $proof
Write-Host ("Retained candidate archive binding verified: run #{0} ({1}) / source {2} / package {3} / archive manifest {4}." -f $proof.workflowRunNumber, $proof.workflowRunId, $proof.sourceCommit, $proof.packageSha256, $proof.archiveKitManifestSha256)
