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

function Read-CanonicalEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained beta candidate evidence is missing: $Path"
    }

    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$evidence.schemaVersion -ne 2) {
        throw "Retained beta read-back contract requires canonical evidence schema v2."
    }
    if ([string]$evidence.kind -ne 'DragonDiskForgeRetainedBetaCandidateEvidence') {
        throw 'Unexpected retained beta candidate evidence kind.'
    }

    Assert-HexSha256 -Value ([string]$evidence.artifactDigestSha256) -Label 'artifactDigestSha256'
    Assert-HexSha256 -Value ([string]$evidence.witnessKitManifestSha256) -Label 'witnessKitManifestSha256'
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
    $pattern = '(?s)Independent retained-artifact read-back confirms GitHub artifact SHA-256 `(?<artifact>[0-9a-fA-F]{64})`.*?Schema-v2 retained evidence also binds witness-kit manifest SHA-256 `(?<witness>[0-9a-fA-F]{64})`'
    $matches = [regex]::Matches($text, $pattern)
    if ($matches.Count -ne 1) {
        throw "docs/BETA-RELEASE.md must contain exactly one canonical retained-artifact read-back paragraph; found $($matches.Count)."
    }

    $artifact = $matches[0].Groups['artifact'].Value.ToLowerInvariant()
    $witness = $matches[0].Groups['witness'].Value.ToLowerInvariant()
    $expectedArtifact = ([string]$Evidence.artifactDigestSha256).ToLowerInvariant()
    $expectedWitness = ([string]$Evidence.witnessKitManifestSha256).ToLowerInvariant()

    if ($artifact -ne $expectedArtifact) {
        throw "docs/BETA-RELEASE.md retained-artifact digest is stale: expected $expectedArtifact, found $artifact."
    }
    if ($witness -ne $expectedWitness) {
        throw "docs/BETA-RELEASE.md witness-kit manifest digest is stale: expected $expectedWitness, found $witness."
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

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-beta-readback-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $root 'docs') -Force | Out-Null
    try {
        $evidence = [ordered]@{
            schemaVersion = 2
            kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
            artifactDigestSha256 = ('1' * 64)
            witnessKitManifestSha256 = ('2' * 64)
            publicRelease = $false
            betaReady = $false
        }
        $evidencePath = Join-Path $root 'docs/retained-beta-candidate.json'
        $releasePath = Join-Path $root 'docs/BETA-RELEASE.md'
        Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
        $parsed = Read-CanonicalEvidence -Path $evidencePath

        $good = "Independent retained-artifact read-back confirms GitHub artifact SHA-256 ``$($parsed.artifactDigestSha256)``, package manifest schema 5. Schema-v2 retained evidence also binds witness-kit manifest SHA-256 ``$($parsed.witnessKitManifestSha256)``, verifier evidence."
        Write-Utf8NoBom -Path $releasePath -Text ($good + [Environment]::NewLine)
        Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed

        $badArtifact = $good.Replace([string]$parsed.artifactDigestSha256, ('9' * 64))
        Write-Utf8NoBom -Path $releasePath -Text ($badArtifact + [Environment]::NewLine)
        $rejected = $false
        try { Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: stale artifact digest was accepted.' }

        $badWitness = $good.Replace([string]$parsed.witnessKitManifestSha256, ('8' * 64))
        Write-Utf8NoBom -Path $releasePath -Text ($badWitness + [Environment]::NewLine)
        $rejected = $false
        try { Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: stale witness-kit digest was accepted.' }

        Write-Utf8NoBom -Path $releasePath -Text "No retained read-back paragraph.`n"
        $rejected = $false
        try { Test-BetaReleaseReadback -BetaReleasePath $releasePath -Evidence $parsed } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: missing retained read-back paragraph was accepted.' }

        Write-Host 'Dragon DiskForge retained beta read-back contract self-test passed.'
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
Write-Host ("Retained beta read-back matches canonical evidence: artifact {0}; witness manifest {1}." -f $evidence.artifactDigestSha256, $evidence.witnessKitManifestSha256)
