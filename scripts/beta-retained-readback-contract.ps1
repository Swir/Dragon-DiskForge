[CmdletBinding()]
param(
    [ValidateSet('verify', 'self-test')]
    [string]$Mode = 'verify',
    [string]$RepositoryRoot = '',
    [string]$EvidencePath = 'docs/retained-beta-candidate.json'
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
}

function Assert-Property {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not ($Object.PSObject.Properties.Name -contains $Name)) {
        throw $Message
    }
}

function Read-CanonicalEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained beta candidate evidence is missing: $Path"
    }

    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -ne 2) {
        throw 'Retained beta read-back contract requires canonical evidence schema v2.'
    }
    if ([string]$evidence.kind -ne 'DragonDiskForgeRetainedBetaCandidateEvidence') {
        throw 'Unexpected retained beta candidate evidence kind.'
    }

    Assert-HexSha256 -Value ([string]$evidence.artifactDigestSha256) -Label 'artifactDigestSha256'
    Assert-HexSha256 -Value ([string]$evidence.witnessKitManifestSha256) -Label 'witnessKitManifestSha256'
    if ([int]$evidence.liveKitSchema -ne 1) { throw 'Retained beta read-back requires live kit schema 1.' }
    Assert-HexSha256 -Value ([string]$evidence.liveKitManifestSha256) -Label 'liveKitManifestSha256'

    $packageManifestSchema = [int]$evidence.packageManifestSchema
    if ($packageManifestSchema -ne 5 -and $packageManifestSchema -ne 6) {
        throw 'Retained beta read-back requires package manifest schema 5 or 6.'
    }
    if ($packageManifestSchema -eq 6) {
        Assert-Property -Object $evidence -Name 'betaUacWitnessEntryPointSha256' -Message 'Package manifest schema 6 retained evidence must bind betaUacWitnessEntryPointSha256.'
        Assert-Property -Object $evidence -Name 'betaUacPairVerifierEntryPointSha256' -Message 'Package manifest schema 6 retained evidence must bind betaUacPairVerifierEntryPointSha256.'
        Assert-HexSha256 -Value ([string]$evidence.betaUacWitnessEntryPointSha256) -Label 'betaUacWitnessEntryPointSha256'
        Assert-HexSha256 -Value ([string]$evidence.betaUacPairVerifierEntryPointSha256) -Label 'betaUacPairVerifierEntryPointSha256'
    }

    if ([bool]$evidence.publicRelease) { throw 'Retained evidence must not claim a public release.' }
    if ([bool]$evidence.betaReady) { throw 'Retained evidence must not claim beta readiness while manual gates remain open.' }

    return $evidence
}

function Test-BetaReleaseReadback {
    param(
        [Parameter(Mandatory = $true)][string]$BetaReleasePath,
        [Parameter(Mandatory = $true)]$Evidence
    )

    if (-not (Test-Path -LiteralPath $BetaReleasePath -PathType Leaf)) {
        throw "Beta release document is missing: $BetaReleasePath"
    }

    $text = Get-Content -LiteralPath $BetaReleasePath -Raw
    $pattern = '(?s)Independent retained-artifact read-back confirms GitHub artifact SHA-256 `(?<artifact>[0-9a-fA-F]{64})`.*?Schema-v2 retained evidence also binds witness-kit manifest SHA-256 `(?<witness>[0-9a-fA-F]{64})`.*?Live-session binding additionally records live-kit manifest SHA-256 `(?<live>[0-9a-fA-F]{64})`.*?(?=\r?\n\r?\n### Regression and manual QA|\z)'
    $matches = [regex]::Matches($text, $pattern)
    if ($matches.Count -ne 1) {
        throw "docs/BETA-RELEASE.md must contain exactly one canonical retained-artifact read-back paragraph with witness and live-session bindings; found $($matches.Count)."
    }

    $paragraph = $matches[0].Value
    $artifact = $matches[0].Groups['artifact'].Value.ToLowerInvariant()
    $witness = $matches[0].Groups['witness'].Value.ToLowerInvariant()
    $live = $matches[0].Groups['live'].Value.ToLowerInvariant()
    $expectedArtifact = ([string]$Evidence.artifactDigestSha256).ToLowerInvariant()
    $expectedWitness = ([string]$Evidence.witnessKitManifestSha256).ToLowerInvariant()
    $expectedLive = ([string]$Evidence.liveKitManifestSha256).ToLowerInvariant()

    if ($artifact -ne $expectedArtifact) {
        throw "docs/BETA-RELEASE.md retained-artifact digest is stale: expected $expectedArtifact, found $artifact."
    }
    if ($witness -ne $expectedWitness) {
        throw "docs/BETA-RELEASE.md witness-kit manifest digest is stale: expected $expectedWitness, found $witness."
    }
    if ($live -ne $expectedLive) {
        throw "docs/BETA-RELEASE.md live-kit manifest digest is stale: expected $expectedLive, found $live."
    }

    if ([int]$Evidence.packageManifestSchema -eq 6) {
        $expectedUac = ([string]$Evidence.betaUacWitnessEntryPointSha256).ToLowerInvariant()
        $expectedPair = ([string]$Evidence.betaUacPairVerifierEntryPointSha256).ToLowerInvariant()
        if ($paragraph.IndexOf($expectedUac, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "docs/BETA-RELEASE.md retained read-back is missing or stale for the packaged UAC witness SHA-256 $expectedUac."
        }
        if ($paragraph.IndexOf($expectedPair, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "docs/BETA-RELEASE.md retained read-back is missing or stale for the packaged UAC pair verifier SHA-256 $expectedPair."
        }
        if ($paragraph -notmatch '(?i)packaged\s+(normal-user\s+)?UAC\s+witness') {
            throw 'docs/BETA-RELEASE.md retained read-back must explicitly identify the packaged UAC witness binding for package schema 6.'
        }
        if ($paragraph -notmatch '(?i)UAC\s+(before/after\s+)?pair\s+verifier|UAC\s+pair-verifier') {
            throw 'docs/BETA-RELEASE.md retained read-back must explicitly identify the packaged UAC pair verifier binding for package schema 6.'
        }
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
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw $FailureMessage }
}

function Copy-JsonObject {
    param([Parameter(Mandatory = $true)]$Value)
    return ($Value | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-beta-readback-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $root 'docs') -Force | Out-Null
    try {
        $evidence = [ordered]@{
            schemaVersion = 2
            kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
            artifactDigestSha256 = ('1' * 64)
            witnessKitManifestSha256 = ('2' * 64)
            liveKitSchema = 1
            liveKitManifestSha256 = ('3' * 64)
            packageManifestSchema = 5
            publicRelease = $false
            betaReady = $false
        }
        $evidencePath = Join-Path $root 'docs/retained-beta-candidate.json'
        $releasePath = Join-Path $root 'docs/BETA-RELEASE.md'
        Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        $parsed = Read-CanonicalEvidence -Path $evidencePath

        $good = "Independent retained-artifact read-back confirms GitHub artifact SHA-256 ``$($parsed.artifactDigestSha256)``, package manifest schema 5. Schema-v2 retained evidence also binds witness-kit manifest SHA-256 ``$($parsed.witnessKitManifestSha256)``, verifier evidence. Live-session binding additionally records live-kit manifest SHA-256 ``$($parsed.liveKitManifestSha256)``, verifier evidence."
        Write-Utf8NoBom -Path $releasePath -Text ($good + [Environment]::NewLine)
        Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed

        foreach ($case in @(
            @{ Text = $good.Replace([string]$parsed.artifactDigestSha256, ('9' * 64)); Message = 'Self-test failed: stale artifact digest was accepted.' },
            @{ Text = $good.Replace([string]$parsed.witnessKitManifestSha256, ('8' * 64)); Message = 'Self-test failed: stale witness-kit digest was accepted.' },
            @{ Text = $good.Replace([string]$parsed.liveKitManifestSha256, ('7' * 64)); Message = 'Self-test failed: stale live-kit digest was accepted.' }
        )) {
            Write-Utf8NoBom -Path $releasePath -Text ($case.Text + [Environment]::NewLine)
            Assert-Rejected -Action { Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed } -FailureMessage $case.Message
        }

        $schema6 = [ordered]@{
            schemaVersion = 2
            kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
            artifactDigestSha256 = ('1' * 64)
            witnessKitManifestSha256 = ('2' * 64)
            liveKitSchema = 1
            liveKitManifestSha256 = ('3' * 64)
            packageManifestSchema = 6
            betaUacWitnessEntryPointSha256 = ('4' * 64)
            betaUacPairVerifierEntryPointSha256 = ('5' * 64)
            publicRelease = $false
            betaReady = $false
        }
        Write-Utf8NoBom -Path $evidencePath -Text (($schema6 | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        $parsed6 = Read-CanonicalEvidence -Path $evidencePath
        $good6 = "Independent retained-artifact read-back confirms GitHub artifact SHA-256 ``$($parsed6.artifactDigestSha256)``, package manifest schema 6 and packaged normal-user UAC witness SHA-256 ``$($parsed6.betaUacWitnessEntryPointSha256)`` plus packaged UAC before/after pair verifier SHA-256 ``$($parsed6.betaUacPairVerifierEntryPointSha256)``. Schema-v2 retained evidence also binds witness-kit manifest SHA-256 ``$($parsed6.witnessKitManifestSha256)``, verifier evidence. Live-session binding additionally records live-kit manifest SHA-256 ``$($parsed6.liveKitManifestSha256)``, verifier evidence. The retained package binds the exact packaged normal-user UAC witness and UAC pair verifier."
        Write-Utf8NoBom -Path $releasePath -Text ($good6 + [Environment]::NewLine)
        Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed6

        $badUac = $good6.Replace([string]$parsed6.betaUacWitnessEntryPointSha256, ('6' * 64))
        Write-Utf8NoBom -Path $releasePath -Text ($badUac + [Environment]::NewLine)
        Assert-Rejected -Action { Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed6 } -FailureMessage 'Self-test failed: stale packaged UAC witness digest was accepted.'

        $badPair = $good6.Replace([string]$parsed6.betaUacPairVerifierEntryPointSha256, ('7' * 64))
        Write-Utf8NoBom -Path $releasePath -Text ($badPair + [Environment]::NewLine)
        Assert-Rejected -Action { Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed6 } -FailureMessage 'Self-test failed: stale packaged UAC pair verifier digest was accepted.'

        $missingUacEvidence = Copy-JsonObject -Value $schema6
        $missingUacEvidence.PSObject.Properties.Remove('betaUacWitnessEntryPointSha256')
        Write-Utf8NoBom -Path $evidencePath -Text (($missingUacEvidence | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        Assert-Rejected -Action { $null = Read-CanonicalEvidence -Path $evidencePath } -FailureMessage 'Self-test failed: schema-6 evidence without packaged UAC witness SHA-256 was accepted.'

        $missingPairEvidence = Copy-JsonObject -Value $schema6
        $missingPairEvidence.PSObject.Properties.Remove('betaUacPairVerifierEntryPointSha256')
        Write-Utf8NoBom -Path $evidencePath -Text (($missingPairEvidence | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        Assert-Rejected -Action { $null = Read-CanonicalEvidence -Path $evidencePath } -FailureMessage 'Self-test failed: schema-6 evidence without packaged UAC pair verifier SHA-256 was accepted.'

        $badPairEvidence = Copy-JsonObject -Value $schema6
        $badPairEvidence.betaUacPairVerifierEntryPointSha256 = '00'
        Write-Utf8NoBom -Path $evidencePath -Text (($badPairEvidence | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        Assert-Rejected -Action { $null = Read-CanonicalEvidence -Path $evidencePath } -FailureMessage 'Self-test failed: malformed packaged UAC pair verifier SHA-256 was accepted.'

        Write-Utf8NoBom -Path $releasePath -Text "No retained read-back paragraph.`n"
        Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        $parsed = Read-CanonicalEvidence -Path $evidencePath
        Assert-Rejected -Action { Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed } -FailureMessage 'Self-test failed: missing retained read-back paragraph was accepted.'

        Write-Host 'Dragon DiskForge retained beta read-back contract self-test passed for package manifest schemas 5 and 6, including the schema-6 UAC pair verifier binding.'
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
$evidence = Read-CanonicalEvidence -Path $evidenceFile
Test-BetaReleaseReadback -BetaReleasePath (Join-Path $root 'docs/BETA-RELEASE.md') -Evidence $evidence
if ([int]$evidence.packageManifestSchema -eq 6) {
    Write-Host ("Retained beta read-back matches canonical evidence: artifact {0}; witness manifest {1}; live manifest {2}; package manifest schema 6; UAC witness {3}; UAC pair verifier {4}." -f $evidence.artifactDigestSha256, $evidence.witnessKitManifestSha256, $evidence.liveKitManifestSha256, $evidence.betaUacWitnessEntryPointSha256, $evidence.betaUacPairVerifierEntryPointSha256)
}
else {
    Write-Host ("Retained beta read-back matches canonical evidence: artifact {0}; witness manifest {1}; live manifest {2}; package manifest schema {3}." -f $evidence.artifactDigestSha256, $evidence.witnessKitManifestSha256, $evidence.liveKitManifestSha256, $evidence.packageManifestSchema)
}
