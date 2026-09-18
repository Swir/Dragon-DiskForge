[CmdletBinding()]
param(
    [ValidateSet("verify", "self-test")]
    [string]$Mode = "verify",
    [string]$RepositoryRoot = "",
    [string]$EvidencePath = "docs/retained-beta-candidate.json"
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

function Read-RetainedCandidateEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained beta candidate evidence is missing: $Path"
    }

    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -ne 1) { throw "Unsupported retained beta candidate evidence schema '$($evidence.schemaVersion)'." }
    if ([string]$evidence.kind -ne "DragonDiskForgeRetainedBetaCandidateEvidence") { throw "Unexpected retained beta candidate evidence kind." }
    if ([string]$evidence.product -ne "Dragon DiskForge") { throw "Unexpected retained beta candidate product." }
    if ([string]$evidence.version -ne "0.5.0-beta.1") { throw "Unexpected retained beta candidate version '$($evidence.version)'." }
    if ([string]$evidence.architecture -ne "x64") { throw "Retained beta candidate architecture must be x64." }
    if ([string]$evidence.sourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw "Retained candidate sourceCommit must be an exact Git SHA." }
    if ([string]$evidence.workflowRunId -notmatch '^[1-9][0-9]*$') { throw "Retained candidate workflowRunId must be a positive integer string." }
    if ([int64]$evidence.workflowRunNumber -le 0) { throw "Retained candidate workflowRunNumber must be positive." }
    if ([string]$evidence.packageSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Retained candidate packageSha256 must be SHA-256." }
    if ([string]$evidence.artifactName -ne "DragonDiskForge-$($evidence.version)-win-x64-candidate-$($evidence.workflowRunId)") {
        throw "Retained candidate artifactName does not match version/run identity."
    }
    if ([bool]$evidence.publicRelease) { throw "Retained candidate evidence must not claim a public release." }
    if ([bool]$evidence.betaReady) { throw "Retained candidate evidence must not claim beta readiness while manual gates remain open." }

    return $evidence
}

function Get-CanonicalTokens {
    param([Parameter(Mandatory = $true)]$Evidence)

    return @(
        [string]$Evidence.version,
        ("Beta Candidate run #{0}" -f [int64]$Evidence.workflowRunNumber),
        [string]$Evidence.workflowRunId,
        [string]$Evidence.artifactName,
        ([string]$Evidence.sourceCommit).ToLowerInvariant(),
        ([string]$Evidence.packageSha256).ToLowerInvariant()
    )
}

function Get-SingleCandidateBlock {
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

function Test-DocumentationSet {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)]$Evidence
    )

    $specs = @(
        @{ Path = 'README.md'; EvidenceLink = 'docs/retained-beta-candidate.json' },
        @{ Path = 'docs/ROADMAP.md'; EvidenceLink = 'retained-beta-candidate.json' },
        @{ Path = 'docs/STATUS.md'; EvidenceLink = 'retained-beta-candidate.json' },
        @{ Path = 'docs/MILESTONES.md'; EvidenceLink = 'retained-beta-candidate.json' },
        @{ Path = 'docs/BETA-RELEASE.md'; EvidenceLink = 'retained-beta-candidate.json' },
        @{ Path = 'CHANGELOG.md'; EvidenceLink = 'docs/retained-beta-candidate.json' }
    )

    $tokens = Get-CanonicalTokens -Evidence $Evidence
    foreach ($spec in $specs) {
        $path = Join-Path $Root $spec.Path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required beta status document is missing: $($spec.Path)" }

        $text = Get-Content -LiteralPath $path -Raw
        $block = Get-SingleCandidateBlock -Text $text -Path $spec.Path
        foreach ($token in $tokens) {
            if ($block.IndexOf($token, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                throw "$($spec.Path) retained-candidate block is stale or incomplete; missing '$token'."
            }
        }

        if ($block.IndexOf([string]$spec.EvidenceLink, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "$($spec.Path) retained-candidate block does not link to $($spec.EvidenceLink)."
        }
        if ($block -notmatch '(?i)not\s+(a\s+)?public\s+release|non-public') {
            throw "$($spec.Path) retained-candidate block must state that the artifact is not a public release."
        }
    }
}

function Write-Utf8NoBom {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Text)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function New-SelfTestBlock {
    param([Parameter(Mandatory = $true)]$Evidence, [Parameter(Mandatory = $true)][string]$EvidenceLink)
    return @"
$StartMarker
Current retained candidate: Beta Candidate run #$($Evidence.workflowRunNumber) ($($Evidence.workflowRunId)), artifact $($Evidence.artifactName), source commit $($Evidence.sourceCommit), package SHA-256 $($Evidence.packageSha256). This is a non-public engineering candidate, not a public release. Authoritative evidence: [$EvidenceLink]($EvidenceLink).
$EndMarker
"@
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-docs-consistency-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path (Join-Path $root 'docs') -Force | Out-Null
    try {
        $evidence = [ordered]@{
            schemaVersion = 1
            kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
            product = 'Dragon DiskForge'
            version = '0.5.0-beta.1'
            architecture = 'x64'
            sourceCommit = ('a' * 40)
            workflowRunId = '123456'
            workflowRunNumber = 79
            artifactName = 'DragonDiskForge-0.5.0-beta.1-win-x64-candidate-123456'
            packageSha256 = ('b' * 64)
            publicRelease = $false
            betaReady = $false
        }
        $evidencePath = Join-Path $root 'docs/retained-beta-candidate.json'
        Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        $parsed = Read-RetainedCandidateEvidence -Path $evidencePath

        $specs = @(
            @{ Path = 'README.md'; Link = 'docs/retained-beta-candidate.json' },
            @{ Path = 'docs/ROADMAP.md'; Link = 'retained-beta-candidate.json' },
            @{ Path = 'docs/STATUS.md'; Link = 'retained-beta-candidate.json' },
            @{ Path = 'docs/MILESTONES.md'; Link = 'retained-beta-candidate.json' },
            @{ Path = 'docs/BETA-RELEASE.md'; Link = 'retained-beta-candidate.json' },
            @{ Path = 'CHANGELOG.md'; Link = 'docs/retained-beta-candidate.json' }
        )
        foreach ($spec in $specs) {
            Write-Utf8NoBom -Path (Join-Path $root $spec.Path) -Text (New-SelfTestBlock -Evidence $parsed -EvidenceLink $spec.Link)
        }

        Test-DocumentationSet -Root $root -Evidence $parsed

        $badPath = Join-Path $root 'docs/STATUS.md'
        $badText = (Get-Content -LiteralPath $badPath -Raw).Replace(('b' * 64), ('c' * 64))
        Write-Utf8NoBom -Path $badPath -Text $badText
        $rejected = $false
        try { Test-DocumentationSet -Root $root -Evidence $parsed } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: stale package SHA-256 in documentation was accepted." }

        Write-Host "Dragon DiskForge beta documentation consistency self-test passed."
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Mode -eq 'self-test') {
    Invoke-SelfTest
    exit 0
}

$root = Resolve-RepositoryRoot -RequestedRoot $RepositoryRoot
$evidenceFile = if ([System.IO.Path]::IsPathRooted($EvidencePath)) { $EvidencePath } else { Join-Path $root $EvidencePath }
$evidence = Read-RetainedCandidateEvidence -Path $evidenceFile
Test-DocumentationSet -Root $root -Evidence $evidence
Write-Host ("Beta documentation is synchronized to retained candidate run #{0} ({1}), source {2}, package SHA-256 {3}." -f $evidence.workflowRunNumber, $evidence.workflowRunId, $evidence.sourceCommit, $evidence.packageSha256)
