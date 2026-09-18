[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$utf8 = New-Object System.Text.UTF8Encoding($false)

Copy-Item -LiteralPath '.automation/candidate113-evidence.json' -Destination 'docs/retained-beta-candidate.json' -Force
Copy-Item -LiteralPath '.automation/retained-beta-archive-binding-contract.ps1' -Destination 'scripts/retained-beta-archive-binding-contract.ps1' -Force
Copy-Item -LiteralPath '.automation/retained-beta-archive-binding-contract.yml' -Destination '.github/workflows/retained-beta-archive-binding-contract.yml' -Force

$start = '<!-- retained-beta-candidate:start -->'
$end = '<!-- retained-beta-candidate:end -->'
$pattern = '(?s)' + [regex]::Escape($start) + '.*?' + [regex]::Escape($end)
$candidateSentence = 'Current retained candidate: Beta Candidate run #113 (`35327348417`), artifact `DragonDiskForge-0.5.0-beta.1-win-x64-candidate-35327348417`, built from `main` source commit `48054cc5036ca3809915b3f8392599c315bf2ec7`; nested package SHA-256 `8baeddfed293167e54d827ed1c11e511a3d69c3a80af2a567dfe66e4b78200e7`. Evidence is witness-bound (schema v2) and archive-bound to the packaged desktop witness companion and portable evidence-archive companion and does not claim any human gate. It is a non-public engineering candidate, not a public release.'

$docSpecs = @(
    @{ Path = 'README.md'; Link = 'docs/retained-beta-candidate.json' },
    @{ Path = 'docs/ROADMAP.md'; Link = 'retained-beta-candidate.json' },
    @{ Path = 'docs/STATUS.md'; Link = 'retained-beta-candidate.json' },
    @{ Path = 'docs/MILESTONES.md'; Link = 'retained-beta-candidate.json' },
    @{ Path = 'docs/BETA-RELEASE.md'; Link = 'retained-beta-candidate.json' }
)

foreach ($spec in $docSpecs) {
    $text = Get-Content -LiteralPath $spec.Path -Raw
    $matches = [regex]::Matches($text, $pattern)
    if ($matches.Count -ne 1) {
        throw ($spec.Path + ' must contain exactly one retained candidate block.')
    }
    $linkText = 'Authoritative retained evidence: [`' + $spec.Link + '`](' + $spec.Link + ').'
    $replacement = $start + [Environment]::NewLine + $candidateSentence + ' ' + $linkText + [Environment]::NewLine + $end
    $updated = [regex]::Replace($text, $pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $replacement }, 1)
    [System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $spec.Path), $updated, $utf8)
}

$candidatePath = 'docs/BETA-CANDIDATE.md'
$candidateText = Get-Content -LiteralPath $candidatePath -Raw
$checkpointPattern = '(?s)### Current retained checkpoint\r?\n.*?(?=\r?\n## Candidate contract script)'
if (-not [regex]::IsMatch($candidateText, $checkpointPattern)) {
    throw 'Current retained checkpoint section was not found.'
}
$checkpoint = Get-Content -LiteralPath '.automation/candidate113-checkpoint.md' -Raw
$candidateText = [regex]::Replace($candidateText, $checkpointPattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $checkpoint.TrimEnd() }, 1)

$candidateText = $candidateText.Replace(
    '7. build a hash-bound beta QA kit,',
    '7. build the hash-bound core beta QA kit plus the desktop-witness and portable evidence-archive companions,'
)
$candidateText = $candidateText.Replace(
    '8. retain the ZIP, checksum, candidate metadata and QA kit only when a `main` commit is deliberately marked `[beta-candidate]`,',
    '8. retain the ZIP, checksum, candidate metadata and all hash-bound QA companions only when a `main` commit is deliberately marked `[beta-candidate]`,'
)

if ($candidateText -notmatch 'beta-qa-archive-kit\.json') {
    $needle = 'BETA-MANUAL-VALIDATION.md' + [Environment]::NewLine + '```'
    if (-not $candidateText.Contains($needle)) {
        throw 'Retained artifact file-list insertion point was not found.'
    }
    $extra = Get-Content -LiteralPath '.automation/candidate113-extra-list.txt' -Raw
    $candidateText = $candidateText.Replace($needle, $extra.TrimEnd())
}
[System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $candidatePath), $candidateText, $utf8)

$changelogPath = 'CHANGELOG.md'
$changelog = Get-Content -LiteralPath $changelogPath -Raw
$oldCandidatePattern = '(?m)^- Beta Candidate run #109 .*?$'
if (-not [regex]::IsMatch($changelog, $oldCandidatePattern)) {
    throw 'Current retained-candidate changelog bullet was not found.'
}
$newCandidate = '- Beta Candidate run #113 on `main` commit `48054cc5036ca3809915b3f8392599c315bf2ec7` is the current retained non-public Windows x64 candidate; GitHub artifact SHA-256 `014d8971c4434c03ed0577848f968fa1bd1f00294f93796d0e9c18bf206645fe`, nested package SHA-256 `8baeddfed293167e54d827ed1c11e511a3d69c3a80af2a567dfe66e4b78200e7`; canonical evidence remains schema v2, witness-bound and additionally archive-bound to the portable evidence-archive companion'
$changelog = [regex]::Replace($changelog, $oldCandidatePattern, $newCandidate, 1)

$anchor = '- retained-candidate evidence schema v2 binding the candidate package, beta QA kit and desktop witness companion hashes while preserving fail-closed beta readiness'
$archiveBullet = '- additive retained-candidate archive binding for the portable evidence-archive manifest, verifier, helper and guide, enforced by a dedicated fail-closed contract without changing beta readiness'
if ($changelog.Contains($anchor) -and -not $changelog.Contains('additive retained-candidate archive binding')) {
    $changelog = $changelog.Replace($anchor, $anchor + [Environment]::NewLine + $archiveBullet)
}
[System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $changelogPath), $changelog, $utf8)

Write-Host 'Candidate #113 evidence and documentation synchronized in the workspace.'
