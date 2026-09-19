[CmdletBinding()]
param(
    [ValidateSet('verify', 'apply', 'self-test')]
    [string]$Mode = 'verify',
    [string]$RepositoryRoot = '',
    [string]$EvidencePath = 'docs/retained-beta-candidate.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$StartMarker = '<!-- retained-beta-candidate:start -->'
$EndMarker = '<!-- retained-beta-candidate:end -->'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Resolve-RepositoryRoot {
    param([string]$RequestedRoot)
    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        return (Resolve-Path -LiteralPath $RequestedRoot).Path
    }
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
}

function Assert-HexSha256 {
    param([Parameter(Mandatory = $true)][string]$Value, [Parameter(Mandatory = $true)][string]$Label)
    if ($Value -notmatch '^[0-9a-fA-F]{64}$') { throw "$Label must be a 64-character SHA-256 value." }
    return $Value.ToLowerInvariant()
}

function Read-RetainedEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Retained candidate evidence is missing: $Path" }
    $e = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ([int]$e.schemaVersion -ne 2) { throw 'Retained documentation sync requires evidence schema v2.' }
    if ([string]$e.kind -ne 'DragonDiskForgeRetainedBetaCandidateEvidence') { throw 'Unexpected retained evidence kind.' }
    if ([string]$e.product -ne 'Dragon DiskForge') { throw 'Unexpected retained evidence product.' }
    if ([string]$e.version -ne '0.5.0-beta.1') { throw "Unexpected retained candidate version '$($e.version)'." }
    if ([string]$e.architecture -ne 'x64') { throw 'Retained candidate architecture must be x64.' }
    if ([string]$e.sourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'sourceCommit must be an exact Git SHA.' }
    if ([string]$e.workflowRunId -notmatch '^[1-9][0-9]*$') { throw 'workflowRunId must be a positive integer string.' }
    if ([int64]$e.workflowRunNumber -le 0) { throw 'workflowRunNumber must be positive.' }
    if ([string]$e.artifactId -notmatch '^[1-9][0-9]*$') { throw 'artifactId must be a positive integer string.' }

    foreach ($item in @(
        @{ Value = [string]$e.artifactDigestSha256; Label = 'artifactDigestSha256' },
        @{ Value = [string]$e.packageSha256; Label = 'packageSha256' },
        @{ Value = [string]$e.candidateMetadataSha256; Label = 'candidateMetadataSha256' },
        @{ Value = [string]$e.qaKitManifestSha256; Label = 'qaKitManifestSha256' },
        @{ Value = [string]$e.liveKitManifestSha256; Label = 'liveKitManifestSha256' },
        @{ Value = [string]$e.liveVerifierSha256; Label = 'liveVerifierSha256' },
        @{ Value = [string]$e.liveSessionHelperSha256; Label = 'liveSessionHelperSha256' },
        @{ Value = [string]$e.liveSessionGuideSha256; Label = 'liveSessionGuideSha256' },
        @{ Value = [string]$e.witnessKitManifestSha256; Label = 'witnessKitManifestSha256' },
        @{ Value = [string]$e.witnessVerifierSha256; Label = 'witnessVerifierSha256' },
        @{ Value = [string]$e.desktopWitnessHelperSha256; Label = 'desktopWitnessHelperSha256' },
        @{ Value = [string]$e.desktopWitnessGuideSha256; Label = 'desktopWitnessGuideSha256' },
        @{ Value = [string]$e.archiveKitManifestSha256; Label = 'archiveKitManifestSha256' },
        @{ Value = [string]$e.archiveVerifierSha256; Label = 'archiveVerifierSha256' },
        @{ Value = [string]$e.archiveHelperSha256; Label = 'archiveHelperSha256' },
        @{ Value = [string]$e.archiveGuideSha256; Label = 'archiveGuideSha256' },
        @{ Value = [string]$e.explorerVerifierSha256; Label = 'explorerVerifierSha256' },
        @{ Value = [string]$e.explorerWitnessHelperSha256; Label = 'explorerWitnessHelperSha256' },
        @{ Value = [string]$e.explorerWitnessGuideSha256; Label = 'explorerWitnessGuideSha256' },
        @{ Value = [string]$e.entryPointSha256; Label = 'entryPointSha256' },
        @{ Value = [string]$e.betaManualQaEntryPointSha256; Label = 'betaManualQaEntryPointSha256' }
    )) { $null = Assert-HexSha256 -Value $item.Value -Label $item.Label }

    if ([int]$e.packageManifestSchema -ne 5) { throw 'packageManifestSchema must be 5.' }
    if ([int]$e.qaKitSchema -ne 2) { throw 'qaKitSchema must be 2.' }
    if ([int]$e.liveKitSchema -ne 1) { throw 'liveKitSchema must be 1.' }
    if ([int]$e.witnessKitSchema -ne 1) { throw 'witnessKitSchema must be 1.' }
    if ([int]$e.archiveKitSchema -ne 1) { throw 'archiveKitSchema must be 1.' }
    if ([int]$e.explorerKitSchema -ne 1) { throw 'explorerKitSchema must be 1.' }
    if ([bool]$e.publicRelease) { throw 'Retained candidate must remain non-public.' }
    if ([bool]$e.betaReady) { throw 'Retained candidate must remain not beta-ready while interactive gates are open.' }
    return $e
}

function Write-Utf8NoBom {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Text)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Text, $Utf8NoBom)
}

function New-RetainedBlock {
    param([Parameter(Mandatory = $true)]$Evidence, [Parameter(Mandatory = $true)][string]$Link, [switch]$Bullet)
    $prefix = if ($Bullet) { '- ' } else { '' }
    $line = "${prefix}Current retained candidate: Beta Candidate run #$($Evidence.workflowRunNumber) (``$($Evidence.workflowRunId)``), artifact ``$($Evidence.artifactName)``, built from ``main`` source commit ``$($Evidence.sourceCommit)``; nested package SHA-256 ``$($Evidence.packageSha256)``. Evidence is witness-bound (schema v2), archive-bound, live-session-bound and Explorer-witness-bound to the packaged desktop witness, portable evidence-archive, package-specific live continuity and package-specific real-Explorer witness companions; no human gate is claimed. It is a non-public engineering candidate, not a public release. Authoritative retained evidence: [``$Link``]($Link)."
    return "$StartMarker`n$line`n$EndMarker"
}

function Replace-SingleMarkedBlock {
    param([Parameter(Mandatory = $true)][string]$Text, [Parameter(Mandatory = $true)][string]$Replacement, [Parameter(Mandatory = $true)][string]$Label)
    $pattern = '(?s)' + [regex]::Escape($StartMarker) + '.*?' + [regex]::Escape($EndMarker)
    $matches = [regex]::Matches($Text, $pattern)
    if ($matches.Count -ne 1) { throw "$Label must contain exactly one retained-candidate block; found $($matches.Count)." }
    return [regex]::Replace($Text, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $Replacement }, 1)
}

function Add-ExplorerArtifactList {
    param([Parameter(Mandatory = $true)][string]$Text)
    if ($Text.Contains('beta-qa-explorer-kit.json')) { return $Text }
    $needle = 'BETA-QA-EVIDENCE-ARCHIVE.md.sha256' + "`n" + '```'
    $insert = @'
BETA-QA-EVIDENCE-ARCHIVE.md.sha256
beta-qa-explorer-kit.json
beta-qa-explorer-kit.json.sha256
beta-qa-explorer-kit.ps1
beta-qa-explorer-kit.ps1.sha256
beta-qa-explorer-witness.ps1
beta-qa-explorer-witness.ps1.sha256
BETA-QA-EXPLORER-WITNESS.md
BETA-QA-EXPLORER-WITNESS.md.sha256
```
'@
    if (-not $Text.Contains($needle)) { throw 'BETA-CANDIDATE artifact list anchor is missing.' }
    return $Text.Replace($needle, $insert.TrimEnd())
}

function New-CandidateCheckpoint {
    param([Parameter(Mandatory = $true)]$E)
    return @"
### Current retained checkpoint

The authoritative retained candidate is **Beta Candidate run #$($E.workflowRunNumber) (``$($E.workflowRunId)``)**, artifact ``$($E.artifactName)``, built from ``main`` commit ``$($E.sourceCommit)``. The nested package SHA-256 is ``$($E.packageSha256)``; the GitHub artifact SHA-256 is ``$($E.artifactDigestSha256)``.

Independent artifact read-back verified all 23 supplied SHA-256 sidecars, package manifest schema 5, x64 architecture, ``.NET=self-contained``, ``WindowsAppSDK=self-contained``, ``VisualCpp=app-local``, exactly one desktop executable, no PDB payloads, the desktop entry-point hash ``$($E.entryPointSha256)`` and the packaged manual-QA-tool hash ``$($E.betaManualQaEntryPointSha256)``.

Canonical retained evidence remains schema v2. It is witness-bound to the packaged desktop witness companion, archive-bound to the portable evidence-archive companion, live-session-bound to the package-specific live continuity manifest/verifier/helper/guide, and Explorer-witness-bound to the package-specific verifier/helper/guide used to capture real File Explorer process/window/path evidence. Candidate #$($E.workflowRunNumber) is the first authoritative retained candidate after the package-bound Explorer witness companion and the fail-closed retained Explorer-currency gate landed. None of these bindings claims a human gate.

The authoritative machine-readable record is [``retained-beta-candidate.json``](retained-beta-candidate.json). It deliberately keeps ``publicRelease=false`` and ``betaReady=false``.

Earlier candidate checkpoints remain historical evidence only. Candidates that predate the same-session binding, witness binding, portable archive kit, explicit retention policy, retained-candidate currency guard, live-session continuity kit, process-start binding or package-bound Explorer witness companion must not be mixed with the current manual-QA evidence path.

This reselection does not complete or waive any manual release gate. Clean-desktop visible WinUI launch/basic regression, normal-user UAC behavior and real cross-process Explorer/Desktop drag-out still require human observations against the exact retained package, and the separate 0.7 physical-writer gate still requires dedicated disposable media. Until that evidence exists, the candidate remains non-public and ``0.5.0-beta.1`` must not be published as a GitHub Release.
"@
}

function New-ReleaseReadback {
    param([Parameter(Mandatory = $true)]$E)
    return "Independent retained-artifact read-back confirms GitHub artifact SHA-256 ``$($E.artifactDigestSha256)``, package manifest schema 5, x64, ``.NET=self-contained``, ``WindowsAppSDK=self-contained``, ``VisualCpp=app-local``, exactly one desktop entry point, no PDB payloads and SHA-256-bound desktop/manual-QA entry points. The nested package SHA-256 is ``$($E.packageSha256)``; the desktop entry point is bound as ``$($E.entryPointSha256)`` and the packaged manual-QA tool as ``$($E.betaManualQaEntryPointSha256)``. Schema-v2 retained evidence also binds witness-kit manifest SHA-256 ``$($E.witnessKitManifestSha256)``, verifier ``$($E.witnessVerifierSha256)``, desktop witness helper ``$($E.desktopWitnessHelperSha256)`` and guide ``$($E.desktopWitnessGuideSha256)``. Archive binding additionally records archive-kit manifest SHA-256 ``$($E.archiveKitManifestSha256)``, verifier ``$($E.archiveVerifierSha256)``, helper ``$($E.archiveHelperSha256)`` and guide ``$($E.archiveGuideSha256)``. Live-session binding additionally records live-kit manifest SHA-256 ``$($E.liveKitManifestSha256)``, verifier ``$($E.liveVerifierSha256)``, live-session helper ``$($E.liveSessionHelperSha256)`` and guide ``$($E.liveSessionGuideSha256)``. Explorer-witness binding additionally records verifier ``$($E.explorerVerifierSha256)``, real-Explorer witness helper ``$($E.explorerWitnessHelperSha256)`` and guide ``$($E.explorerWitnessGuideSha256)``. Candidate #$($E.workflowRunNumber) carries the package-bound Explorer witness companion and remains fail-closed against stale retained Explorer tooling. The package-only clean-machine runtime matrix also exercises packaged CLI/shell/state/diagnostic paths after developer-toolchain paths are removed. These facts close the developer-SDK dependency gate for the verified packaged runtime paths, but **do not** substitute for the remaining human-confirmed WinUI launch, UAC or real Explorer drag-out gates."
}

function Add-HistoryLine {
    param([Parameter(Mandatory = $true)][string]$Text)
    if ($Text.Contains('Beta Candidate #158')) { return $Text }
    $pattern = '(?m)^- PR #102 .*Beta Candidate #146.*$'
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) { return $Text }
    $line = '- PR #106 + PR #107 / exact PR #107 head `8719ee64cac75bebbad0b628dcb4edfd6d990832` + fully green `main` `b50a28d98bccb14511bf33812df1b80f68756d69` + Beta Candidate #158 — package-bound real-Explorer witness companion, fail-closed retained Explorer-currency guard and fresh Explorer-bound Windows x64 candidate'
    return $Text.Insert($match.Index + $match.Length, "`n$line")
}

function Sync-RepositoryDocs {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)]$E, [switch]$Apply)

    $specs = @(
        @{ Path = 'README.md'; Link = 'docs/retained-beta-candidate.json'; Bullet = $false },
        @{ Path = 'docs/ROADMAP.md'; Link = 'retained-beta-candidate.json'; Bullet = $false },
        @{ Path = 'docs/STATUS.md'; Link = 'retained-beta-candidate.json'; Bullet = $false },
        @{ Path = 'docs/MILESTONES.md'; Link = 'retained-beta-candidate.json'; Bullet = $false },
        @{ Path = 'docs/BETA-RELEASE.md'; Link = 'retained-beta-candidate.json'; Bullet = $false },
        @{ Path = 'CHANGELOG.md'; Link = 'docs/retained-beta-candidate.json'; Bullet = $true }
    )

    foreach ($spec in $specs) {
        $path = Join-Path $Root $spec.Path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing maintained document: $($spec.Path)" }
        $text = Get-Content -LiteralPath $path -Raw
        $expectedBlock = New-RetainedBlock -Evidence $E -Link $spec.Link -Bullet:$spec.Bullet
        $updated = Replace-SingleMarkedBlock -Text $text -Replacement $expectedBlock -Label $spec.Path
        if ($spec.Path -in @('docs/ROADMAP.md', 'docs/STATUS.md', 'docs/MILESTONES.md', 'CHANGELOG.md')) { $updated = Add-HistoryLine -Text $updated }
        if ($Apply) { Write-Utf8NoBom -Path $path -Text $updated }
        elseif (-not [string]::Equals($text, $updated, [System.StringComparison]::Ordinal)) { throw "$($spec.Path) retained-candidate documentation is stale." }
    }

    $candidatePath = Join-Path $Root 'docs/BETA-CANDIDATE.md'
    $candidate = Get-Content -LiteralPath $candidatePath -Raw
    $candidateExpected = $candidate.Replace('build the hash-bound core beta QA kit plus the live-session, desktop-witness and portable evidence-archive companions,', 'build the hash-bound core beta QA kit plus the live-session, desktop-witness, portable evidence-archive and real-Explorer witness companions,')
    $candidateExpected = Add-ExplorerArtifactList -Text $candidateExpected
    $checkpointPattern = '(?s)### Current retained checkpoint\r?\n.*?(?=\r?\n## Candidate contract script)'
    $matches = [regex]::Matches($candidateExpected, $checkpointPattern)
    if ($matches.Count -ne 1) { throw "docs/BETA-CANDIDATE.md must contain one current retained checkpoint section; found $($matches.Count)." }
    $checkpoint = (New-CandidateCheckpoint -E $E).TrimEnd("`r", "`n")
    $candidateExpected = [regex]::Replace($candidateExpected, $checkpointPattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $checkpoint }, 1)
    if ($Apply) { Write-Utf8NoBom -Path $candidatePath -Text $candidateExpected }
    elseif (-not [string]::Equals($candidate, $candidateExpected, [System.StringComparison]::Ordinal)) { throw 'docs/BETA-CANDIDATE.md current checkpoint is stale.' }

    $releasePath = Join-Path $Root 'docs/BETA-RELEASE.md'
    $release = Get-Content -LiteralPath $releasePath -Raw
    $releaseExpected = $release
    $explorerChecklist = '- [x] retained evidence additionally binds the package-specific real-Explorer witness verifier, helper and guide by SHA-256'
    if (-not $releaseExpected.Contains($explorerChecklist)) {
        $anchor = '- [x] retained evidence additionally binds the package-specific live-session continuity manifest, verifier, helper and guide by SHA-256'
        if (-not $releaseExpected.Contains($anchor)) { throw 'docs/BETA-RELEASE.md live-session checklist anchor is missing.' }
        $releaseExpected = $releaseExpected.Replace($anchor, "$anchor`n$explorerChecklist")
    }
    $readbackPattern = '(?s)Independent retained-artifact read-back confirms GitHub artifact SHA-256 .*?(?=\r?\n\r?\n### Regression and manual QA)'
    $rbMatches = [regex]::Matches($releaseExpected, $readbackPattern)
    if ($rbMatches.Count -ne 1) { throw "docs/BETA-RELEASE.md must contain one retained read-back paragraph; found $($rbMatches.Count)." }
    $readback = New-ReleaseReadback -E $E
    $releaseExpected = [regex]::Replace($releaseExpected, $readbackPattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $readback }, 1)
    if ($Apply) { Write-Utf8NoBom -Path $releasePath -Text $releaseExpected }
    elseif (-not [string]::Equals($release, $releaseExpected, [System.StringComparison]::Ordinal)) { throw 'docs/BETA-RELEASE.md retained read-back is stale.' }

    $workflowPath = Join-Path $Root '.github/workflows/beta-retained-explorer-currency.yml'
    $workflow = Get-Content -LiteralPath $workflowPath -Raw
    $strictWorkflow = $workflow.Replace('Inspect retained Explorer companion currency', 'Verify retained Explorer companion currency').Replace('./scripts/beta-retained-explorer-currency.ps1 -Mode verify -AllowStale', './scripts/beta-retained-explorer-currency.ps1 -Mode verify')
    if ($strictWorkflow -match '(?m)-AllowStale\s*$') { throw 'Retained Explorer currency workflow is still inspection-only.' }
    if ($Apply) { Write-Utf8NoBom -Path $workflowPath -Text $strictWorkflow }
    elseif (-not [string]::Equals($workflow, $strictWorkflow, [System.StringComparison]::Ordinal)) { throw 'Retained Explorer currency workflow is not strict yet.' }
}

function Invoke-SelfTest {
    $sourceRoot = Resolve-RepositoryRoot -RequestedRoot $RepositoryRoot
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-retained-doc-sync-' + [guid]::NewGuid().ToString('N'))
    $files = @(
        'README.md', 'CHANGELOG.md', 'docs/ROADMAP.md', 'docs/STATUS.md', 'docs/MILESTONES.md',
        'docs/BETA-RELEASE.md', 'docs/BETA-CANDIDATE.md', 'docs/retained-beta-candidate.json',
        '.github/workflows/beta-retained-explorer-currency.yml'
    )
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        foreach ($relative in $files) {
            $source = Join-Path $sourceRoot $relative
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Self-test source is missing: $relative" }
            $destination = Join-Path $tempRoot $relative
            $destinationDir = Split-Path -Parent $destination
            if (-not (Test-Path -LiteralPath $destinationDir)) { New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null }
            Copy-Item -LiteralPath $source -Destination $destination
        }
        $e = Read-RetainedEvidence -Path (Join-Path $tempRoot 'docs/retained-beta-candidate.json')
        Sync-RepositoryDocs -Root $tempRoot -E $e -Apply
        Sync-RepositoryDocs -Root $tempRoot -E $e
        Write-Host 'Retained candidate documentation sync self-test passed.'
    }
    finally { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($Mode -eq 'self-test') { Invoke-SelfTest; exit 0 }
$root = Resolve-RepositoryRoot -RequestedRoot $RepositoryRoot
$evidenceFile = if ([System.IO.Path]::IsPathRooted($EvidencePath)) { $EvidencePath } else { Join-Path $root $EvidencePath }
$evidence = Read-RetainedEvidence -Path $evidenceFile
if ($Mode -eq 'apply') {
    Sync-RepositoryDocs -Root $root -E $evidence -Apply
    Write-Host "Retained candidate documentation synchronized to run #$($evidence.workflowRunNumber)."
    exit 0
}
Sync-RepositoryDocs -Root $root -E $evidence
Write-Host "Retained candidate documentation is synchronized to run #$($evidence.workflowRunNumber) ($($evidence.workflowRunId))."
