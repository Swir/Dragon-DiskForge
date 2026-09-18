[CmdletBinding()]
param(
    [ValidateSet("verify", "self-test")]
    [string]$Mode = "verify",
    [string]$EvidencePath = "docs/retained-beta-candidate.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ExpectedInteractiveGates = @(
    "clean-machine interactive launch/open/mount/explore/verify/analyze",
    "normal-user UAC validation",
    "real cross-process Explorer drag-out validation"
)

function Assert-HexSha256 {
    param([Parameter(Mandatory = $true)][string]$Value, [Parameter(Mandatory = $true)][string]$Label)
    if ($Value -notmatch '^[0-9a-fA-F]{64}$') { throw "$Label must be a 64-character SHA-256 value." }
    return $Value.ToLowerInvariant()
}

function Assert-ExactCommit {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { throw "sourceCommit must be an exact 40-character Git SHA." }
    return $Value.ToLowerInvariant()
}

function Test-RetainedCandidateEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Retained candidate evidence file is missing: $Path" }
    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json

    $schemaVersion = [int]$evidence.schemaVersion
    if ($schemaVersion -ne 1 -and $schemaVersion -ne 2) {
        throw "Unsupported retained-candidate evidence schema '$schemaVersion'."
    }
    if ([string]$evidence.kind -ne "DragonDiskForgeRetainedBetaCandidateEvidence") { throw "Unexpected retained-candidate evidence kind." }
    if ([string]$evidence.product -ne "Dragon DiskForge") { throw "Unexpected product in retained-candidate evidence." }
    if ([string]$evidence.version -ne "0.5.0-beta.1") { throw "Unexpected beta candidate version '$($evidence.version)'." }
    if ([string]$evidence.architecture -ne "x64") { throw "Retained beta candidate must be x64." }

    $commit = Assert-ExactCommit -Value ([string]$evidence.sourceCommit)
    $runId = [string]$evidence.workflowRunId
    if ($runId -notmatch '^[1-9][0-9]*$') { throw "workflowRunId must be a positive integer string." }
    if ([int64]$evidence.workflowRunNumber -le 0) { throw "workflowRunNumber must be positive." }
    if ([string]$evidence.artifactId -notmatch '^[1-9][0-9]*$') { throw "artifactId must be a positive integer string." }

    $expectedArtifactName = "DragonDiskForge-$($evidence.version)-win-x64-candidate-$runId"
    if ([string]$evidence.artifactName -ne $expectedArtifactName) { throw "artifactName does not match the version/run identity." }
    if ([string]$evidence.packageFile -ne "DragonDiskForge-win-x64.zip") { throw "Unexpected nested package filename." }

    $null = Assert-HexSha256 -Value ([string]$evidence.artifactDigestSha256) -Label "artifactDigestSha256"
    $null = Assert-HexSha256 -Value ([string]$evidence.packageSha256) -Label "packageSha256"
    $null = Assert-HexSha256 -Value ([string]$evidence.candidateMetadataSha256) -Label "candidateMetadataSha256"
    $null = Assert-HexSha256 -Value ([string]$evidence.qaKitManifestSha256) -Label "qaKitManifestSha256"
    $null = Assert-HexSha256 -Value ([string]$evidence.entryPointSha256) -Label "entryPointSha256"
    $null = Assert-HexSha256 -Value ([string]$evidence.betaManualQaEntryPointSha256) -Label "betaManualQaEntryPointSha256"

    if ([int]$evidence.packageManifestSchema -ne 5) { throw "Retained package evidence must bind package manifest schema 5." }
    if ([int]$evidence.qaKitSchema -ne 2) { throw "Retained package evidence must bind beta QA kit schema 2." }

    $witnessBound = $false
    if ($schemaVersion -eq 2) {
        if ([int]$evidence.witnessKitSchema -ne 1) { throw "Retained schema v2 must bind beta QA witness kit schema 1." }
        $null = Assert-HexSha256 -Value ([string]$evidence.witnessKitManifestSha256) -Label "witnessKitManifestSha256"
        $null = Assert-HexSha256 -Value ([string]$evidence.witnessVerifierSha256) -Label "witnessVerifierSha256"
        $null = Assert-HexSha256 -Value ([string]$evidence.desktopWitnessHelperSha256) -Label "desktopWitnessHelperSha256"
        $null = Assert-HexSha256 -Value ([string]$evidence.desktopWitnessGuideSha256) -Label "desktopWitnessGuideSha256"
        $witnessBound = $true
    }

    if ([string]$evidence.runtimeDeployment.dotNet -ne "self-contained") { throw ".NET runtime deployment must be self-contained." }
    if ([string]$evidence.runtimeDeployment.windowsAppSdk -ne "self-contained") { throw "Windows App SDK runtime deployment must be self-contained." }
    if ([string]$evidence.runtimeDeployment.visualCpp -ne "app-local") { throw "Visual C++ runtime deployment must be app-local." }

    $created = [DateTimeOffset]::Parse([string]$evidence.artifactCreatedAtUtc)
    $expires = [DateTimeOffset]::Parse([string]$evidence.artifactExpiresAtUtc)
    if ($expires -le $created) { throw "Artifact expiry must be later than creation time." }

    if ([bool]$evidence.publicRelease) { throw "Retained candidate evidence must not claim a public release." }
    if ([bool]$evidence.betaReady) { throw "Retained candidate evidence must not claim beta readiness while interactive gates remain open." }

    $gates = @($evidence.remainingInteractiveGates)
    if ($gates.Count -ne $ExpectedInteractiveGates.Count) {
        throw "Retained candidate evidence must preserve exactly the known interactive beta blockers."
    }
    for ($index = 0; $index -lt $ExpectedInteractiveGates.Count; $index++) {
        $actual = [string]$gates[$index]
        $expected = [string]$ExpectedInteractiveGates[$index]
        if ([string]::IsNullOrWhiteSpace($actual)) { throw "Interactive gate entries must not be empty." }
        if (-not [string]::Equals($actual, $expected, [System.StringComparison]::Ordinal)) {
            throw "Interactive gate $index changed unexpectedly. Expected '$expected', got '$actual'."
        }
    }

    return [pscustomobject]@{
        schemaVersion = $schemaVersion
        version = [string]$evidence.version
        sourceCommit = $commit
        workflowRunId = $runId
        artifactName = [string]$evidence.artifactName
        packageSha256 = ([string]$evidence.packageSha256).ToLowerInvariant()
        witnessBound = $witnessBound
        publicRelease = [bool]$evidence.publicRelease
        betaReady = [bool]$evidence.betaReady
        remainingInteractiveGateCount = $gates.Count
    }
}

function Invoke-SelfTest {
    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-retained-candidate-contract-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    try {
        $source = (Resolve-Path -LiteralPath $EvidencePath).Path
        $good = Join-Path $workspace "good.json"
        Copy-Item -LiteralPath $source -Destination $good
        $proof = Test-RetainedCandidateEvidence -Path $good
        if ($proof.betaReady -or $proof.publicRelease -or $proof.remainingInteractiveGateCount -ne $ExpectedInteractiveGates.Count) { throw "Valid fixture produced an unsafe readiness result." }

        $goodV2 = Join-Path $workspace "good-v2.json"
        $data = Get-Content -LiteralPath $good -Raw | ConvertFrom-Json
        $data.schemaVersion = 2
        $data | Add-Member -NotePropertyName witnessKitSchema -NotePropertyValue 1 -Force
        $data | Add-Member -NotePropertyName witnessKitManifestSha256 -NotePropertyValue ("1" * 64) -Force
        $data | Add-Member -NotePropertyName witnessVerifierSha256 -NotePropertyValue ("2" * 64) -Force
        $data | Add-Member -NotePropertyName desktopWitnessHelperSha256 -NotePropertyValue ("3" * 64) -Force
        $data | Add-Member -NotePropertyName desktopWitnessGuideSha256 -NotePropertyValue ("4" * 64) -Force
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $goodV2 -Encoding UTF8
        $proofV2 = Test-RetainedCandidateEvidence -Path $goodV2
        if (-not $proofV2.witnessBound -or $proofV2.schemaVersion -ne 2) { throw "Valid schema-v2 fixture did not produce witness-bound evidence." }

        $badWitnessHash = Join-Path $workspace "bad-witness-hash.json"
        $data = Get-Content -LiteralPath $goodV2 -Raw | ConvertFrom-Json
        $data.desktopWitnessHelperSha256 = "00"
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badWitnessHash -Encoding UTF8
        try { $null = Test-RetainedCandidateEvidence -Path $badWitnessHash; throw "Invalid witness SHA-256 fixture was accepted." } catch { if ($_.Exception.Message -eq "Invalid witness SHA-256 fixture was accepted.") { throw } }

        $badWitnessSchema = Join-Path $workspace "bad-witness-schema.json"
        $data = Get-Content -LiteralPath $goodV2 -Raw | ConvertFrom-Json
        $data.witnessKitSchema = 2
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badWitnessSchema -Encoding UTF8
        try { $null = Test-RetainedCandidateEvidence -Path $badWitnessSchema; throw "Unsupported witness schema fixture was accepted." } catch { if ($_.Exception.Message -eq "Unsupported witness schema fixture was accepted.") { throw } }

        $badHash = Join-Path $workspace "bad-hash.json"
        $data = Get-Content -LiteralPath $good -Raw | ConvertFrom-Json
        $data.packageSha256 = "00"
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badHash -Encoding UTF8
        try { $null = Test-RetainedCandidateEvidence -Path $badHash; throw "Invalid SHA-256 fixture was accepted." } catch { if ($_.Exception.Message -eq "Invalid SHA-256 fixture was accepted.") { throw } }

        $badReady = Join-Path $workspace "bad-ready.json"
        $data = Get-Content -LiteralPath $good -Raw | ConvertFrom-Json
        $data.betaReady = $true
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badReady -Encoding UTF8
        try { $null = Test-RetainedCandidateEvidence -Path $badReady; throw "Unsafe beta-ready fixture was accepted." } catch { if ($_.Exception.Message -eq "Unsafe beta-ready fixture was accepted.") { throw } }

        $badArtifact = Join-Path $workspace "bad-artifact.json"
        $data = Get-Content -LiteralPath $good -Raw | ConvertFrom-Json
        $data.artifactName = "wrong-name"
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badArtifact -Encoding UTF8
        try { $null = Test-RetainedCandidateEvidence -Path $badArtifact; throw "Mismatched artifact-name fixture was accepted." } catch { if ($_.Exception.Message -eq "Mismatched artifact-name fixture was accepted.") { throw } }

        $badGate = Join-Path $workspace "bad-gate.json"
        $data = Get-Content -LiteralPath $good -Raw | ConvertFrom-Json
        $data.remainingInteractiveGates[0] = "generic manual QA"
        $data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $badGate -Encoding UTF8
        try { $null = Test-RetainedCandidateEvidence -Path $badGate; throw "Mutated interactive-gate fixture was accepted." } catch { if ($_.Exception.Message -eq "Mutated interactive-gate fixture was accepted.") { throw } }

        Write-Host "Retained beta candidate evidence contract self-test passed, including witness-bound schema v2."
    }
    finally {
        Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Mode -eq "self-test") {
    Invoke-SelfTest
    exit 0
}

$proof = Test-RetainedCandidateEvidence -Path $EvidencePath
Write-Host ("Retained candidate evidence verified: schema {0} / {1} / run {2} / package SHA-256 {3} / witness-bound {4} / interactive blockers {5}." -f $proof.schemaVersion, $proof.sourceCommit, $proof.workflowRunId, $proof.packageSha256, $proof.witnessBound, $proof.remainingInteractiveGateCount)
