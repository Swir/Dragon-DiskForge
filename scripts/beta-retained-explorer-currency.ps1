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

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-RetainedExplorerIdentity {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained beta candidate evidence is missing: $Path"
    }

    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -ne 2) {
        throw 'Explorer currency verification requires canonical retained evidence schema v2.'
    }
    if ([string]$evidence.kind -ne 'DragonDiskForgeRetainedBetaCandidateEvidence') {
        throw 'Unexpected retained beta candidate evidence kind.'
    }
    if ([string]$evidence.sourceCommit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'sourceCommit must be an exact 40-character Git SHA.'
    }

    $properties = @($evidence.PSObject.Properties.Name)
    $explorerFields = @(
        'explorerKitSchema',
        'explorerVerifierSha256',
        'explorerWitnessHelperSha256',
        'explorerWitnessGuideSha256'
    )
    $presentCount = 0
    foreach ($name in $explorerFields) {
        if ($properties -contains $name) { $presentCount++ }
    }

    if ($presentCount -ne 0 -and $presentCount -ne $explorerFields.Count) {
        throw 'Retained candidate contains a partial Explorer witness binding; all Explorer binding fields are required together.'
    }

    $bound = ($presentCount -eq $explorerFields.Count)
    $verifierHash = ''
    $helperHash = ''
    $guideHash = ''

    if ($bound) {
        if ([int]$evidence.explorerKitSchema -ne 1) {
            throw "Unsupported retained Explorer kit schema '$($evidence.explorerKitSchema)'; expected schema 1."
        }
        $verifierHash = Assert-HexSha256 -Value ([string]$evidence.explorerVerifierSha256) -Label 'explorerVerifierSha256'
        $helperHash = Assert-HexSha256 -Value ([string]$evidence.explorerWitnessHelperSha256) -Label 'explorerWitnessHelperSha256'
        $guideHash = Assert-HexSha256 -Value ([string]$evidence.explorerWitnessGuideSha256) -Label 'explorerWitnessGuideSha256'
    }

    return [pscustomobject]@{
        sourceCommit = ([string]$evidence.sourceCommit).ToLowerInvariant()
        workflowRunNumber = [int64]$evidence.workflowRunNumber
        workflowRunId = [string]$evidence.workflowRunId
        artifactName = [string]$evidence.artifactName
        explorerBound = $bound
        explorerVerifierSha256 = $verifierHash
        explorerWitnessHelperSha256 = $helperHash
        explorerWitnessGuideSha256 = $guideHash
    }
}

function Test-RetainedExplorerCurrency {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$CandidateEvidencePath,
        [bool]$PermitStale = $false
    )

    $identity = Read-RetainedExplorerIdentity -Path $CandidateEvidencePath
    $verifierPath = Join-Path $Root 'scripts/beta-qa-explorer-kit.ps1'
    $helperPath = Join-Path $Root 'scripts/beta-qa-explorer-witness.ps1'
    $guidePath = Join-Path $Root 'docs/BETA-QA-EXPLORER-WITNESS.md'

    $currentVerifierHash = Get-Sha256 -Path $verifierPath
    $currentHelperHash = Get-Sha256 -Path $helperPath
    $currentGuideHash = Get-Sha256 -Path $guidePath

    $verifierCurrent = $identity.explorerBound -and [string]::Equals($identity.explorerVerifierSha256, $currentVerifierHash, [System.StringComparison]::OrdinalIgnoreCase)
    $helperCurrent = $identity.explorerBound -and [string]::Equals($identity.explorerWitnessHelperSha256, $currentHelperHash, [System.StringComparison]::OrdinalIgnoreCase)
    $guideCurrent = $identity.explorerBound -and [string]::Equals($identity.explorerWitnessGuideSha256, $currentGuideHash, [System.StringComparison]::OrdinalIgnoreCase)
    $isCurrent = $identity.explorerBound -and $verifierCurrent -and $helperCurrent -and $guideCurrent

    if (-not $isCurrent -and -not $PermitStale) {
        if (-not $identity.explorerBound) {
            throw ("Retained beta candidate run #{0} ({1}) predates the package-bound Explorer witness companion. Retain a fresh candidate from the current verified main and synchronize its Explorer binding before final cross-process drag-out QA." -f $identity.workflowRunNumber, $identity.workflowRunId)
        }

        throw ("Retained beta candidate run #{0} ({1}) is stale for final Explorer drag-out QA. Verifier current={2}, helper current={3}, guide current={4}. Retain a fresh candidate from the current verified main before recording the final human gate." -f $identity.workflowRunNumber, $identity.workflowRunId, $verifierCurrent, $helperCurrent, $guideCurrent)
    }

    return [pscustomobject]@{
        current = $isCurrent
        explorerBound = $identity.explorerBound
        sourceCommit = $identity.sourceCommit
        workflowRunNumber = $identity.workflowRunNumber
        workflowRunId = $identity.workflowRunId
        artifactName = $identity.artifactName
        retainedVerifierSha256 = $identity.explorerVerifierSha256
        currentVerifierSha256 = $currentVerifierHash
        retainedHelperSha256 = $identity.explorerWitnessHelperSha256
        currentHelperSha256 = $currentHelperHash
        retainedGuideSha256 = $identity.explorerWitnessGuideSha256
        currentGuideSha256 = $currentGuideHash
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
    param(
        [string]$VerifierHash = '',
        [string]$HelperHash = '',
        [string]$GuideHash = '',
        [switch]$Unbound
    )

    $evidence = [ordered]@{
        schemaVersion = 2
        kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
        sourceCommit = ('a' * 40)
        workflowRunNumber = 123
        workflowRunId = '456789'
        artifactName = 'DragonDiskForge-0.5.0-beta.1-win-x64-candidate-456789'
    }

    if (-not $Unbound.IsPresent) {
        $evidence.explorerKitSchema = 1
        $evidence.explorerVerifierSha256 = $VerifierHash
        $evidence.explorerWitnessHelperSha256 = $HelperHash
        $evidence.explorerWitnessGuideSha256 = $GuideHash
    }

    return $evidence
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-retained-explorer-currency-' + [guid]::NewGuid().ToString('N'))
    $scripts = Join-Path $root 'scripts'
    $docs = Join-Path $root 'docs'
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    New-Item -ItemType Directory -Path $docs -Force | Out-Null

    try {
        $verifierPath = Join-Path $scripts 'beta-qa-explorer-kit.ps1'
        $helperPath = Join-Path $scripts 'beta-qa-explorer-witness.ps1'
        $guidePath = Join-Path $docs 'BETA-QA-EXPLORER-WITNESS.md'
        Write-Utf8NoBom -Path $verifierPath -Text "Write-Host 'explorer verifier fixture'`n"
        Write-Utf8NoBom -Path $helperPath -Text "Write-Host 'explorer helper fixture'`n"
        Write-Utf8NoBom -Path $guidePath -Text "# Explorer witness fixture`n"

        $verifierHash = Get-Sha256 -Path $verifierPath
        $helperHash = Get-Sha256 -Path $helperPath
        $guideHash = Get-Sha256 -Path $guidePath
        $evidencePath = Join-Path $docs 'retained-beta-candidate.json'

        $matching = New-TestEvidence -VerifierHash $verifierHash -HelperHash $helperHash -GuideHash $guideHash
        Write-Utf8NoBom -Path $evidencePath -Text (($matching | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        $proof = Test-RetainedExplorerCurrency -Root $root -CandidateEvidencePath $evidencePath
        if (-not $proof.current -or -not $proof.explorerBound) { throw 'Self-test failed: matching Explorer binding was reported stale.' }

        $staleHelper = New-TestEvidence -VerifierHash $verifierHash -HelperHash ('1' * 64) -GuideHash $guideHash
        Write-Utf8NoBom -Path $evidencePath -Text (($staleHelper | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        $rejected = $false
        try { $null = Test-RetainedExplorerCurrency -Root $root -CandidateEvidencePath $evidencePath } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: stale Explorer helper was accepted in strict mode.' }
        $inspection = Test-RetainedExplorerCurrency -Root $root -CandidateEvidencePath $evidencePath -PermitStale $true
        if ($inspection.current) { throw 'Self-test failed: stale Explorer helper was reported current in inspection mode.' }

        $unbound = New-TestEvidence -Unbound
        Write-Utf8NoBom -Path $evidencePath -Text (($unbound | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        $rejected = $false
        try { $null = Test-RetainedExplorerCurrency -Root $root -CandidateEvidencePath $evidencePath } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: Explorer-unbound retained evidence was accepted in strict mode.' }
        $inspection = Test-RetainedExplorerCurrency -Root $root -CandidateEvidencePath $evidencePath -PermitStale $true
        if ($inspection.current -or $inspection.explorerBound) { throw 'Self-test failed: Explorer-unbound evidence was misreported as current or bound.' }

        $partial = New-TestEvidence -Unbound
        $partial.explorerKitSchema = 1
        Write-Utf8NoBom -Path $evidencePath -Text (($partial | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        $rejected = $false
        try { $null = Test-RetainedExplorerCurrency -Root $root -CandidateEvidencePath $evidencePath -PermitStale $true } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: partial Explorer binding was accepted.' }

        Write-Host 'Dragon DiskForge retained Explorer companion currency self-test passed.'
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
$proof = Test-RetainedExplorerCurrency -Root $root -CandidateEvidencePath $evidenceFile -PermitStale $AllowStale.IsPresent

if ($proof.current) {
    Write-Host ("Retained candidate run #{0} ({1}) is current for the package-bound Explorer witness companion." -f $proof.workflowRunNumber, $proof.workflowRunId)
}
else {
    Write-Warning ("Retained candidate run #{0} ({1}) is not current for the package-bound Explorer witness companion. Explorer bound: {2}. It remains historical package evidence but must not be used for final cross-process Explorer drag-out QA." -f $proof.workflowRunNumber, $proof.workflowRunId, $proof.explorerBound)
}
