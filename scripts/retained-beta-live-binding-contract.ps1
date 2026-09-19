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

function Read-LiveBoundEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained candidate evidence is missing: $Path"
    }

    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -lt 2) {
        throw "Live-session binding requires retained evidence schema 2 or newer."
    }
    if ([string]$evidence.kind -ne "DragonDiskForgeRetainedBetaCandidateEvidence") { throw "Unexpected retained candidate evidence kind." }
    if ([string]$evidence.product -ne "Dragon DiskForge") { throw "Unexpected retained candidate product." }
    if ([string]$evidence.version -ne "0.5.0-beta.1") { throw "Unexpected retained candidate version '$($evidence.version)'." }
    if ([string]$evidence.architecture -ne "x64") { throw "Retained candidate architecture must be x64." }
    if ([string]$evidence.sourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw "sourceCommit must be an exact 40-character Git SHA." }
    if ([string]$evidence.workflowRunId -notmatch '^[1-9][0-9]*$') { throw "workflowRunId must be a positive integer string." }
    if ([int64]$evidence.workflowRunNumber -le 0) { throw "workflowRunNumber must be positive." }

    $expectedArtifactName = "DragonDiskForge-$($evidence.version)-win-x64-candidate-$($evidence.workflowRunId)"
    if ([string]$evidence.artifactName -ne $expectedArtifactName) { throw "artifactName does not match the version/run identity." }

    if ([int]$evidence.liveKitSchema -ne 1) { throw "Retained candidate must bind beta QA live kit schema 1." }
    $liveManifest = Assert-HexSha256 -Value ([string]$evidence.liveKitManifestSha256) -Label "liveKitManifestSha256"
    $liveVerifier = Assert-HexSha256 -Value ([string]$evidence.liveVerifierSha256) -Label "liveVerifierSha256"
    $liveHelper = Assert-HexSha256 -Value ([string]$evidence.liveSessionHelperSha256) -Label "liveSessionHelperSha256"
    $liveGuide = Assert-HexSha256 -Value ([string]$evidence.liveSessionGuideSha256) -Label "liveSessionGuideSha256"
    $packageSha = Assert-HexSha256 -Value ([string]$evidence.packageSha256) -Label "packageSha256"
    $qaManifest = Assert-HexSha256 -Value ([string]$evidence.qaKitManifestSha256) -Label "qaKitManifestSha256"

    if ([bool]$evidence.publicRelease) { throw "Live-bound retained candidate must not claim a public release." }
    if ([bool]$evidence.betaReady) { throw "Live-session binding must not claim beta readiness while interactive gates remain open." }

    return [pscustomobject]@{
        version = [string]$evidence.version
        sourceCommit = ([string]$evidence.sourceCommit).ToLowerInvariant()
        workflowRunId = [string]$evidence.workflowRunId
        workflowRunNumber = [int64]$evidence.workflowRunNumber
        artifactName = [string]$evidence.artifactName
        packageSha256 = $packageSha
        qaKitManifestSha256 = $qaManifest
        liveKitSchema = [int]$evidence.liveKitSchema
        liveKitManifestSha256 = $liveManifest
        liveVerifierSha256 = $liveVerifier
        liveSessionHelperSha256 = $liveHelper
        liveSessionGuideSha256 = $liveGuide
    }
}

function Get-RetainedBlock {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $pattern = '(?s)' + [regex]::Escape($StartMarker) + '(.*?)' + [regex]::Escape($EndMarker)
    $matches = [regex]::Matches($Text, $pattern)
    if ($matches.Count -ne 1) { throw "$Path must contain exactly one retained beta candidate block; found $($matches.Count)." }
    return $matches[0].Groups[1].Value
}

function Test-LiveDocumentation {
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
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required retained-candidate document is missing: $relativePath" }
        $block = Get-RetainedBlock -Text (Get-Content -LiteralPath $path -Raw) -Path $relativePath
        foreach ($token in $tokens) {
            if ($block.IndexOf([string]$token, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                throw "$relativePath retained-candidate block is stale; missing '$token'."
            }
        }
        if ($block -notmatch '(?i)live-session-bound|live-session companion|live continuity') {
            throw "$relativePath retained-candidate block must state the live-session continuity binding."
        }
        if ($block -notmatch '(?i)not\s+(a\s+)?public\s+release|non-public') {
            throw "$relativePath retained-candidate block must remain explicitly non-public."
        }
    }

    $candidateDoc = Join-Path $Root "docs/BETA-CANDIDATE.md"
    if (-not (Test-Path -LiteralPath $candidateDoc -PathType Leaf)) { throw "docs/BETA-CANDIDATE.md is missing." }
    $candidateText = Get-Content -LiteralPath $candidateDoc -Raw
    foreach ($token in $tokens) {
        if ($candidateText.IndexOf([string]$token, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "docs/BETA-CANDIDATE.md current checkpoint is stale; missing '$token'."
        }
    }
    if ($candidateText -notmatch '(?i)live-session-bound|live-session companion|live continuity') {
        throw "docs/BETA-CANDIDATE.md must describe the live-session continuity binding for the current checkpoint."
    }
}

function Invoke-SelfTest {
    $root = Resolve-RepositoryRoot -RequestedRoot $RepositoryRoot
    $evidenceFile = if ([System.IO.Path]::IsPathRooted($EvidencePath)) { $EvidencePath } else { Join-Path $root $EvidencePath }

    $proof = Read-LiveBoundEvidence -Path $evidenceFile
    Test-LiveDocumentation -Root $root -Evidence $proof

    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-retained-live-contract-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    try {
        $goodPath = Join-Path $workspace "good.json"
        Copy-Item -LiteralPath $evidenceFile -Destination $goodPath
        $null = Read-LiveBoundEvidence -Path $goodPath

        $badHash = Join-Path $workspace "bad-hash.json"
        $data = Get-Content -LiteralPath $goodPath -Raw | ConvertFrom-Json
        $data.liveSessionHelperSha256 = "00"
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badHash -Encoding UTF8
        $rejected = $false
        try { $null = Read-LiveBoundEvidence -Path $badHash } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: invalid live-session helper SHA-256 was accepted." }

        $badSchema = Join-Path $workspace "bad-schema.json"
        $data = Get-Content -LiteralPath $goodPath -Raw | ConvertFrom-Json
        $data.liveKitSchema = 2
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badSchema -Encoding UTF8
        $rejected = $false
        try { $null = Read-LiveBoundEvidence -Path $badSchema } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: unsupported live-kit schema was accepted." }

        $badReady = Join-Path $workspace "bad-ready.json"
        $data = Get-Content -LiteralPath $goodPath -Raw | ConvertFrom-Json
        $data.betaReady = $true
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badReady -Encoding UTF8
        $rejected = $false
        try { $null = Read-LiveBoundEvidence -Path $badReady } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: unsafe beta-ready live evidence was accepted." }

        Write-Host "Retained beta live-session binding contract self-test passed."
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
$proof = Read-LiveBoundEvidence -Path $evidenceFile
Test-LiveDocumentation -Root $root -Evidence $proof
Write-Host ("Retained candidate live-session binding verified: run #{0} ({1}) / source {2} / package {3} / live manifest {4}." -f $proof.workflowRunNumber, $proof.workflowRunId, $proof.sourceCommit, $proof.packageSha256, $proof.liveKitManifestSha256)
