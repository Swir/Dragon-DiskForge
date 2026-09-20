[CmdletBinding()]
param(
    [ValidateSet('verify-retained', 'verify-candidate', 'self-test')]
    [string]$Mode = 'verify-retained',
    [string]$EvidencePath = 'docs/retained-beta-candidate.json',
    [string]$CandidateMetadataPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

function Assert-Schema6UacBindings {
    param(
        [Parameter(Mandatory = $true)]$Document,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $schema = [int]$Document.packageManifestSchema
    if ($schema -lt 5 -or $schema -gt 6) {
        throw "$Label packageManifestSchema '$schema' is unsupported."
    }

    if ($schema -eq 5) {
        return [pscustomobject]@{
            packageManifestSchema = 5
            uacWitnessBound = $false
            uacPairVerifierBound = $false
        }
    }

    foreach ($property in @('betaUacWitnessEntryPointSha256', 'betaUacPairVerifierEntryPointSha256')) {
        if (-not ($Document.PSObject.Properties.Name -contains $property)) {
            throw "$Label package manifest schema 6 must bind $property."
        }
    }

    $uacWitness = Assert-HexSha256 -Value ([string]$Document.betaUacWitnessEntryPointSha256) -Label "$Label betaUacWitnessEntryPointSha256"
    $uacPair = Assert-HexSha256 -Value ([string]$Document.betaUacPairVerifierEntryPointSha256) -Label "$Label betaUacPairVerifierEntryPointSha256"

    return [pscustomobject]@{
        packageManifestSchema = 6
        uacWitnessBound = $true
        uacPairVerifierBound = $true
        betaUacWitnessEntryPointSha256 = $uacWitness
        betaUacPairVerifierEntryPointSha256 = $uacPair
    }
}

function Test-CandidateMetadata {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Candidate metadata is missing: $Path"
    }
    $candidate = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$candidate.schemaVersion -ne 1) { throw 'Candidate metadata schema must be 1.' }
    if ([string]$candidate.kind -ne 'DragonDiskForgeBetaCandidate') { throw 'Unexpected candidate metadata kind.' }
    if ([string]$candidate.product -ne 'Dragon DiskForge') { throw 'Unexpected candidate metadata product.' }
    if ([string]$candidate.architecture -ne 'x64') { throw 'Candidate metadata architecture must be x64.' }
    if ([bool]$candidate.publicRelease) { throw 'Candidate metadata must remain non-public.' }
    return Assert-Schema6UacBindings -Document $candidate -Label 'Candidate metadata'
}

function Test-RetainedEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained candidate evidence is missing: $Path"
    }
    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -ne 2) { throw 'Retained candidate evidence schema must be 2.' }
    if ([string]$evidence.kind -ne 'DragonDiskForgeRetainedBetaCandidateEvidence') { throw 'Unexpected retained candidate evidence kind.' }
    if ([string]$evidence.product -ne 'Dragon DiskForge') { throw 'Unexpected retained candidate evidence product.' }
    if ([string]$evidence.architecture -ne 'x64') { throw 'Retained candidate architecture must be x64.' }
    if ([bool]$evidence.publicRelease) { throw 'Retained candidate must remain non-public.' }
    if ([bool]$evidence.betaReady) { throw 'Retained candidate must remain not beta-ready while interactive gates are open.' }
    return Assert-Schema6UacBindings -Document $evidence -Label 'Retained candidate evidence'
}

function Write-TestJson {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 6) + [Environment]::NewLine), $encoding)
}

function Assert-Rejected {
    param([Parameter(Mandatory = $true)][scriptblock]$Action, [Parameter(Mandatory = $true)][string]$Label)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Self-test failed: $Label was accepted." }
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-uac-provenance-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $candidatePath = Join-Path $root 'beta-candidate.json'
        $candidate = [ordered]@{
            schemaVersion = 1
            kind = 'DragonDiskForgeBetaCandidate'
            product = 'Dragon DiskForge'
            architecture = 'x64'
            packageManifestSchema = 6
            betaUacWitnessEntryPointSha256 = ('1' * 64)
            betaUacPairVerifierEntryPointSha256 = ('2' * 64)
            publicRelease = $false
        }
        Write-TestJson -Path $candidatePath -Value $candidate
        $candidateProof = Test-CandidateMetadata -Path $candidatePath
        if (-not $candidateProof.uacWitnessBound -or -not $candidateProof.uacPairVerifierBound) {
            throw 'Self-test failed: valid schema-6 candidate metadata was not fully UAC-bound.'
        }

        $missingPair = [ordered]@{}
        foreach ($property in $candidate.Keys) { if ($property -ne 'betaUacPairVerifierEntryPointSha256') { $missingPair[$property] = $candidate[$property] } }
        Write-TestJson -Path $candidatePath -Value $missingPair
        Assert-Rejected -Label 'schema-6 candidate metadata without pair-verifier hash' -Action { Test-CandidateMetadata -Path $candidatePath }

        $candidate.betaUacPairVerifierEntryPointSha256 = '00'
        Write-TestJson -Path $candidatePath -Value $candidate
        Assert-Rejected -Label 'schema-6 candidate metadata with malformed pair-verifier hash' -Action { Test-CandidateMetadata -Path $candidatePath }
        $candidate.betaUacPairVerifierEntryPointSha256 = ('2' * 64)

        $retainedPath = Join-Path $root 'retained.json'
        $retained = [ordered]@{
            schemaVersion = 2
            kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
            product = 'Dragon DiskForge'
            architecture = 'x64'
            packageManifestSchema = 6
            betaUacWitnessEntryPointSha256 = ('3' * 64)
            betaUacPairVerifierEntryPointSha256 = ('4' * 64)
            publicRelease = $false
            betaReady = $false
        }
        Write-TestJson -Path $retainedPath -Value $retained
        $retainedProof = Test-RetainedEvidence -Path $retainedPath
        if (-not $retainedProof.uacWitnessBound -or -not $retainedProof.uacPairVerifierBound) {
            throw 'Self-test failed: valid schema-6 retained evidence was not fully UAC-bound.'
        }

        $retained.PSObject | Out-Null
        $missingWitness = [ordered]@{}
        foreach ($property in $retained.Keys) { if ($property -ne 'betaUacWitnessEntryPointSha256') { $missingWitness[$property] = $retained[$property] } }
        Write-TestJson -Path $retainedPath -Value $missingWitness
        Assert-Rejected -Label 'schema-6 retained evidence without UAC witness hash' -Action { Test-RetainedEvidence -Path $retainedPath }

        $retained.betaReady = $true
        Write-TestJson -Path $retainedPath -Value $retained
        Assert-Rejected -Label 'retained evidence that claims beta readiness' -Action { Test-RetainedEvidence -Path $retainedPath }

        $legacy = [ordered]@{
            schemaVersion = 2
            kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
            product = 'Dragon DiskForge'
            architecture = 'x64'
            packageManifestSchema = 5
            publicRelease = $false
            betaReady = $false
        }
        Write-TestJson -Path $retainedPath -Value $legacy
        $legacyProof = Test-RetainedEvidence -Path $retainedPath
        if ($legacyProof.uacWitnessBound -or $legacyProof.uacPairVerifierBound) {
            throw 'Self-test failed: legacy schema-5 evidence incorrectly claimed UAC provenance.'
        }

        Write-Host 'Dragon DiskForge UAC provenance contract self-test passed: schema 6 requires witness + pair-verifier SHA-256; schema 5 remains legacy/non-claiming.'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

switch ($Mode) {
    'self-test' {
        Invoke-SelfTest
        exit 0
    }
    'verify-candidate' {
        if ([string]::IsNullOrWhiteSpace($CandidateMetadataPath)) { throw '-CandidateMetadataPath is required for verify-candidate.' }
        $proof = Test-CandidateMetadata -Path $CandidateMetadataPath
        Write-Host ("Candidate UAC provenance verified: package schema {0}; witness-bound {1}; pair-verifier-bound {2}." -f $proof.packageManifestSchema, $proof.uacWitnessBound, $proof.uacPairVerifierBound)
        exit 0
    }
    'verify-retained' {
        $proof = Test-RetainedEvidence -Path $EvidencePath
        Write-Host ("Retained UAC provenance verified: package schema {0}; witness-bound {1}; pair-verifier-bound {2}." -f $proof.packageManifestSchema, $proof.uacWitnessBound, $proof.uacPairVerifierBound)
        exit 0
    }
}
