[CmdletBinding()]
param(
    [ValidateSet('verify', 'self-test')]
    [string]$Mode = 'verify',

    [string]$RepositoryRoot = '',

    [string]$EvidencePath = 'docs/retained-beta-candidate.json',

    [switch]$AllowStale
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryRoot {
    param([string]$RequestedRoot)

    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        return (Resolve-Path -LiteralPath $RequestedRoot).Path
    }

    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
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

function Read-RetainedCandidateIdentity {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained beta candidate evidence is missing: $Path"
    }

    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -ne 2) {
        throw 'Retained beta candidate currency verification requires canonical evidence schema v2.'
    }
    if ([string]$evidence.kind -ne 'DragonDiskForgeRetainedBetaCandidateEvidence') {
        throw 'Unexpected retained beta candidate evidence kind.'
    }
    if ([string]$evidence.sourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'sourceCommit must be an exact 40-character Git SHA.'
    }

    $qaHash = Assert-HexSha256 -Value ([string]$evidence.betaManualQaEntryPointSha256) -Label 'betaManualQaEntryPointSha256'

    return [pscustomobject]@{
        sourceCommit = ([string]$evidence.sourceCommit).ToLowerInvariant()
        workflowRunNumber = [int64]$evidence.workflowRunNumber
        workflowRunId = [string]$evidence.workflowRunId
        artifactName = [string]$evidence.artifactName
        betaManualQaEntryPointSha256 = $qaHash
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-RetainedCandidateCurrency {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$CandidateEvidencePath,
        [bool]$PermitStale = $false
    )

    $identity = Read-RetainedCandidateIdentity -Path $CandidateEvidencePath
    $currentQaPath = Join-Path $Root 'scripts/beta-manual-qa.ps1'
    $currentQaHash = Get-Sha256 -Path $currentQaPath
    $isCurrent = [string]::Equals(
        $identity.betaManualQaEntryPointSha256,
        $currentQaHash,
        [System.StringComparison]::OrdinalIgnoreCase)

    if (-not $isCurrent -and -not $PermitStale) {
        throw ("Retained beta candidate run #{0} ({1}) is stale for final manual QA: packaged beta-manual-qa SHA-256 {2} does not match current repository tool SHA-256 {3}. Retain a fresh candidate from the current verified main before recording final human-gate evidence." -f $identity.workflowRunNumber, $identity.workflowRunId, $identity.betaManualQaEntryPointSha256, $currentQaHash)
    }

    return [pscustomobject]@{
        current = $isCurrent
        sourceCommit = $identity.sourceCommit
        workflowRunNumber = $identity.workflowRunNumber
        workflowRunId = $identity.workflowRunId
        artifactName = $identity.artifactName
        retainedManualQaSha256 = $identity.betaManualQaEntryPointSha256
        currentManualQaSha256 = $currentQaHash
    }
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function New-TestEvidence {
    param([Parameter(Mandatory = $true)][string]$QaHash)

    return [ordered]@{
        schemaVersion = 2
        kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
        sourceCommit = ('a' * 40)
        workflowRunNumber = 123
        workflowRunId = '456789'
        artifactName = 'DragonDiskForge-0.5.0-beta.1-win-x64-candidate-456789'
        betaManualQaEntryPointSha256 = $QaHash
    }
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-beta-retained-currency-' + [guid]::NewGuid().ToString('N'))
    $scripts = Join-Path $root 'scripts'
    $docs = Join-Path $root 'docs'
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    New-Item -ItemType Directory -Path $docs -Force | Out-Null

    try {
        $qaPath = Join-Path $scripts 'beta-manual-qa.ps1'
        Write-Utf8NoBom -Path $qaPath -Text "Write-Host 'manual QA fixture'`n"
        $currentHash = Get-Sha256 -Path $qaPath
        $evidencePath = Join-Path $docs 'retained-beta-candidate.json'

        $matching = New-TestEvidence -QaHash $currentHash
        Write-Utf8NoBom -Path $evidencePath -Text (($matching | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        $proof = Test-RetainedCandidateCurrency -Root $root -CandidateEvidencePath $evidencePath
        if (-not $proof.current) { throw 'Self-test failed: matching retained QA tool was reported stale.' }

        $stale = New-TestEvidence -QaHash ('1' * 64)
        Write-Utf8NoBom -Path $evidencePath -Text (($stale | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        $rejected = $false
        try { $null = Test-RetainedCandidateCurrency -Root $root -CandidateEvidencePath $evidencePath } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: stale retained QA tool was accepted in strict mode.' }

        $inspection = Test-RetainedCandidateCurrency -Root $root -CandidateEvidencePath $evidencePath -PermitStale $true
        if ($inspection.current) { throw 'Self-test failed: stale retained QA tool was reported current in inspection mode.' }

        $invalid = New-TestEvidence -QaHash '00'
        Write-Utf8NoBom -Path $evidencePath -Text (($invalid | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        $rejected = $false
        try { $null = Test-RetainedCandidateCurrency -Root $root -CandidateEvidencePath $evidencePath -PermitStale $true } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: malformed retained QA hash was accepted.' }

        Write-Host 'Dragon DiskForge retained beta candidate currency self-test passed.'
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
$proof = Test-RetainedCandidateCurrency -Root $root -CandidateEvidencePath $evidenceFile -PermitStale $AllowStale.IsPresent

if ($proof.current) {
    Write-Host ("Retained candidate run #{0} ({1}) is current for the repository manual-QA tool: SHA-256 {2}." -f $proof.workflowRunNumber, $proof.workflowRunId, $proof.currentManualQaSha256)
}
else {
    Write-Warning ("Retained candidate run #{0} ({1}) predates the current manual-QA tool. Retained SHA-256: {2}; current SHA-256: {3}. It remains historical package evidence but must not be used for final human-gate recording." -f $proof.workflowRunNumber, $proof.workflowRunId, $proof.retainedManualQaSha256, $proof.currentManualQaSha256)
}
