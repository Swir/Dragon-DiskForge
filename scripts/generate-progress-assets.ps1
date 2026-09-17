[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

# Source of truth:
# - docs/ROADMAP.md overall percentage is the existing project-weighted metric.
# - milestone 0.9 uses verified_completed / total_in_scope.
# Release readiness is intentionally not folded into either progress percentage.

function Get-ProgressGeometry {
    param(
        [double]$Numerator,
        [double]$Denominator,
        [double]$TrackWidth
    )

    if ($TrackWidth -lt 0 -or [double]::IsNaN($TrackWidth) -or [double]::IsInfinity($TrackWidth)) {
        throw 'Track width must be a finite non-negative number.'
    }

    if ($Denominator -le 0) {
        return [pscustomobject]@{
            IsKnown = $false
            Fraction = $null
            Percentage = $null
            FillWidth = 0.0
        }
    }

    if ($Numerator -lt 0 -or $Numerator -gt $Denominator) {
        throw "Progress numerator $Numerator is outside the valid range 0..$Denominator."
    }

    $fraction = $Numerator / $Denominator
    $percentage = $fraction * 100.0
    $fillWidth = $TrackWidth * $fraction

    if ([double]::IsNaN($fillWidth) -or [double]::IsInfinity($fillWidth) -or $fillWidth -lt 0 -or $fillWidth -gt $TrackWidth) {
        throw 'Computed progress geometry is invalid or out of bounds.'
    }

    return [pscustomobject]@{
        IsKnown = $true
        Fraction = $fraction
        Percentage = $percentage
        FillWidth = $fillWidth
    }
}

function ConvertTo-XmlText {
    param([string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Assert-GeneratorSelfTests {
    $zero = Get-ProgressGeometry -Numerator 0 -Denominator 7 -TrackWidth 700
    if (-not $zero.IsKnown -or $zero.Percentage -ne 0 -or $zero.FillWidth -ne 0) {
        throw 'Zero-progress geometry self-test failed.'
    }

    $complete = Get-ProgressGeometry -Numerator 7 -Denominator 7 -TrackWidth 700
    if (-not $complete.IsKnown -or [math]::Abs($complete.Percentage - 100.0) -gt 0.000001 -or [math]::Abs($complete.FillWidth - 700.0) -gt 0.000001) {
        throw 'Complete-progress geometry self-test failed.'
    }

    $partial = Get-ProgressGeometry -Numerator 5 -Denominator 7 -TrackWidth 700
    if (-not $partial.IsKnown -or [math]::Abs($partial.FillWidth - 500.0) -gt 0.000001) {
        throw 'Partial-progress geometry self-test failed.'
    }

    $unknown = Get-ProgressGeometry -Numerator 0 -Denominator 0 -TrackWidth 700
    if ($unknown.IsKnown -or $unknown.FillWidth -ne 0) {
        throw 'Unknown-denominator geometry self-test failed.'
    }

    $longLabel = 'Long label ' + (-join ('X' * 180)) + ' & < >'
    $escaped = ConvertTo-XmlText $longLabel
    if ($escaped -notmatch '&amp;' -or $escaped -notmatch '&lt;' -or $escaped -notmatch '&gt;') {
        throw 'Long-label XML escaping self-test failed.'
    }
}

function Normalize-Text {
    param([string]$Value)
    return $Value.Replace("`r`n", "`n").TrimEnd([char[]]"`r`n") + "`n"
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )

    $normalized = Normalize-Text $Content
    [System.IO.File]::WriteAllText($Path, $normalized, [System.Text.UTF8Encoding]::new($false))
}

function Assert-XmlDocument {
    param(
        [string]$Name,
        [string]$Content
    )

    $xml = [System.Xml.XmlDocument]::new()
    $xml.PreserveWhitespace = $true
    try {
        $xml.LoadXml($Content)
    }
    catch {
        throw "$Name is not valid XML: $($_.Exception.Message)"
    }

    if ($Content -match '="(?:NaN|Infinity|-Infinity|XXX)"') {
        throw "$Name contains a non-finite or placeholder numeric attribute."
    }
}

Assert-GeneratorSelfTests

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$roadmapPath = Join-Path $repoRoot 'docs/ROADMAP.md'
$readmePath = Join-Path $repoRoot 'README.md'
$assetDir = Join-Path $repoRoot 'assets/readme'

$roadmap = [System.IO.File]::ReadAllText($roadmapPath)
$readme = [System.IO.File]::ReadAllText($readmePath)

$overallMatch = [regex]::Match($roadmap, '(?m)^## Overall progress — (?<percentage>\d+(?:\.\d+)?)% toward 1\.0\s*$')
if (-not $overallMatch.Success) {
    throw 'Could not read the authoritative overall project percentage from docs/ROADMAP.md.'
}

$overallPercentage = [double]::Parse($overallMatch.Groups['percentage'].Value, $culture)
if ($overallPercentage -lt 0 -or $overallPercentage -gt 100 -or [double]::IsNaN($overallPercentage) -or [double]::IsInfinity($overallPercentage)) {
    throw 'Authoritative overall project percentage is outside 0..100 or is not finite.'
}

$stageMatches = [regex]::Matches($roadmap, '(?m)^- \*\*(?<id>(?:0\.[1-9]|1\.0)) .+?\*\*\s*$')
if ($stageMatches.Count -eq 0) {
    throw 'Could not read roadmap-stage counters from docs/ROADMAP.md.'
}
$completedStages = @($stageMatches | Where-Object { $_.Value -match '— COMPLETE' }).Count
$totalStages = $stageMatches.Count

$milestoneSection = [regex]::Match($roadmap, '(?ms)^## 0\.9 Quality, Security \+ Beta Hardening.*?(?=^## 1\.0 Production Release)')
if (-not $milestoneSection.Success) {
    throw 'Could not locate the authoritative 0.9 milestone section in docs/ROADMAP.md.'
}

$milestoneMatch = [regex]::Match($milestoneSection.Value, '(?m)^Current scope: \*\*(?<completed>\d+)/(?<total>\d+) \(~\d+%\)\*\*\.')
if (-not $milestoneMatch.Success) {
    throw 'Could not read the 0.9 verified-deliverable counter from docs/ROADMAP.md.'
}

$milestoneCompleted = [double]::Parse($milestoneMatch.Groups['completed'].Value, $culture)
$milestoneTotal = [double]::Parse($milestoneMatch.Groups['total'].Value, $culture)
$milestoneGeometry = Get-ProgressGeometry -Numerator $milestoneCompleted -Denominator $milestoneTotal -TrackWidth 700
if (-not $milestoneGeometry.IsKnown) {
    throw 'Milestone 0.9 denominator is unknown; live progress must be rendered as N/A instead of a percentage.'
}

$overallDisplay = $overallPercentage.ToString('0.0', $culture)
$overallFill = (1100.0 * ($overallPercentage / 100.0)).ToString('0.000', $culture)
$milestoneDisplay = $milestoneGeometry.Percentage.ToString('0.0', $culture)
$milestoneFill = $milestoneGeometry.FillWidth.ToString('0.000', $culture)
$milestoneCompletedDisplay = ([int]$milestoneCompleted).ToString($culture)
$milestoneTotalDisplay = ([int]$milestoneTotal).ToString($culture)
$milestoneStatus = if ($milestoneGeometry.Percentage -ge 100.0) { 'COMPLETE' } else { 'IN PROGRESS' }
$projectStatus = if ($overallPercentage -ge 100.0) { 'COMPLETE' } else { 'IN PROGRESS' }

$overallFillElement = if ($overallPercentage -gt 0) {
    "  <rect x=\"50\" y=\"126\" width=\"$overallFill\" height=\"14\" rx=\"7\" fill=\"url(#progressGradient)\" clip-path=\"url(#trackClip)\" filter=\"url(#softGlow)\"/>"
} else {
    ''
}

$milestoneFillElement = if ($milestoneGeometry.Percentage -gt 0) {
    "  <rect x=\"170\" y=\"50\" width=\"$milestoneFill\" height=\"12\" rx=\"6\" fill=\"url(#miniGradient)\" clip-path=\"url(#miniTrackClip)\" filter=\"url(#miniGlow)\"/>"
} else {
    ''
}

$card = @"
<!-- SWIR-PROGRESS-SVG-PRO:v1 -->
<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="180" viewBox="0 0 1200 180" role="img" aria-labelledby="progressTitle progressDesc">
  <title id="progressTitle">Dragon DiskForge project progress</title>
  <desc id="progressDesc">Weighted project progress is $overallDisplay percent toward 1.0. $completedStages of $totalStages roadmap stages are complete. Release readiness is tracked separately.</desc>
  <!-- source: docs/ROADMAP.md; metric: documented weighted overall percentage; counter: completed roadmap stages / total roadmap stages -->
  <defs>
    <linearGradient id="progressGradient" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0%" stop-color="#0088FF"/>
      <stop offset="100%" stop-color="#62E5FF"/>
    </linearGradient>
    <pattern id="grid" width="24" height="24" patternUnits="userSpaceOnUse">
      <path d="M 24 0 L 0 0 0 24" fill="none" stroke="#12314A" stroke-width="0.7" opacity="0.22"/>
    </pattern>
    <filter id="softGlow" x="-10%" y="-100%" width="120%" height="300%">
      <feGaussianBlur stdDeviation="3" result="blur"/>
      <feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
    </filter>
    <clipPath id="trackClip"><rect x="50" y="126" width="1100" height="14" rx="7"/></clipPath>
  </defs>
  <rect x="0" y="0" width="1200" height="180" rx="22" fill="#02050A"/>
  <rect x="1" y="1" width="1198" height="178" rx="21" fill="url(#grid)" stroke="#12314A"/>
  <text x="50" y="42" fill="#F4FAFF" font-family="Segoe UI, Arial, sans-serif" font-size="25" font-weight="700">Dragon DiskForge</text>
  <text x="50" y="68" fill="#8DA8B8" font-family="Segoe UI, Arial, sans-serif" font-size="15">Measured scope: weighted project completion toward 1.0</text>
  <text x="1150" y="52" text-anchor="end" fill="#62E5FF" font-family="Segoe UI, Arial, sans-serif" font-size="31" font-weight="700">$overallDisplay%</text>
  <rect x="50" y="84" width="130" height="26" rx="13" fill="#07111C" stroke="#0088FF"/>
  <text x="115" y="102" text-anchor="middle" fill="#62E5FF" font-family="Segoe UI, Arial, sans-serif" font-size="12" font-weight="700">$projectStatus</text>
  <text x="198" y="102" fill="#F4FAFF" font-family="Segoe UI, Arial, sans-serif" font-size="14">Completed milestones: $completedStages/$totalStages</text>
  <rect x="50" y="126" width="1100" height="14" rx="7" fill="#07111C" stroke="#24475E"/>
$overallFillElement
  <text x="50" y="162" fill="#8DA8B8" font-family="Segoe UI, Arial, sans-serif" font-size="13">Weighted roadmap metric &#8226; release readiness is tracked separately</text>
</svg>
"@

$mini = @"
<!-- SWIR-PROGRESS-SVG-PRO:v1 -->
<svg xmlns="http://www.w3.org/2000/svg" width="900" height="96" viewBox="0 0 900 96" role="img" aria-labelledby="miniTitle miniDesc">
  <title id="miniTitle">Dragon DiskForge milestone 0.9 progress</title>
  <desc id="miniDesc">Milestone 0.9 Quality, Security plus Beta Hardening is $milestoneCompletedDisplay of $milestoneTotalDisplay verified deliverables, $milestoneDisplay percent, $($milestoneStatus.ToLowerInvariant()).</desc>
  <!-- source: docs/ROADMAP.md; scope: 0.9 Quality, Security + Beta Hardening; calculation: verified_completed / total_in_scope -->
  <defs>
    <linearGradient id="miniGradient" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0%" stop-color="#0088FF"/>
      <stop offset="100%" stop-color="#62E5FF"/>
    </linearGradient>
    <filter id="miniGlow" x="-10%" y="-150%" width="120%" height="400%">
      <feGaussianBlur stdDeviation="2" result="blur"/>
      <feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
    </filter>
    <clipPath id="miniTrackClip"><rect x="170" y="50" width="700" height="12" rx="6"/></clipPath>
  </defs>
  <rect x="0" y="0" width="900" height="96" rx="16" fill="#02050A"/>
  <rect x="1" y="1" width="898" height="94" rx="15" fill="#07111C" stroke="#12314A"/>
  <text x="20" y="28" fill="#F4FAFF" font-family="Segoe UI, Arial, sans-serif" font-size="16" font-weight="700">0.9 Quality, Security + Beta Hardening</text>
  <text x="870" y="28" text-anchor="end" fill="#62E5FF" font-family="Segoe UI, Arial, sans-serif" font-size="18" font-weight="700">$milestoneDisplay%</text>
  <text x="20" y="61" fill="#F4FAFF" font-family="Segoe UI, Arial, sans-serif" font-size="14" font-weight="700">$milestoneCompletedDisplay/$milestoneTotalDisplay</text>
  <rect x="170" y="50" width="700" height="12" rx="6" fill="#02050A" stroke="#24475E"/>
$milestoneFillElement
  <text x="20" y="84" fill="#62E5FF" font-family="Segoe UI, Arial, sans-serif" font-size="11" font-weight="700">$milestoneStatus</text>
  <text x="170" y="84" fill="#8DA8B8" font-family="Segoe UI, Arial, sans-serif" font-size="12">$milestoneCompletedDisplay/$milestoneTotalDisplay verified deliverables &#8226; beta readiness tracked separately</text>
</svg>
"@

$template = @'
<!-- SWIR-PROGRESS-SVG-PRO:v1 -->
<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="180" viewBox="0 0 1200 180" role="img" aria-labelledby="templateTitle templateDesc">
  <title id="templateTitle">SWIR progress card template</title>
  <desc id="templateDesc">Reusable template only. This file does not contain project progress data.</desc>
  <!-- TEMPLATE / NOT PROJECT DATA. Use the deterministic generator to create live project assets from an authoritative source. -->
  <defs>
    <linearGradient id="templateGradient" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0%" stop-color="#0088FF"/>
      <stop offset="100%" stop-color="#62E5FF"/>
    </linearGradient>
    <pattern id="templateGrid" width="24" height="24" patternUnits="userSpaceOnUse">
      <path d="M 24 0 L 0 0 0 24" fill="none" stroke="#12314A" stroke-width="0.7" opacity="0.22"/>
    </pattern>
  </defs>
  <rect x="0" y="0" width="1200" height="180" rx="22" fill="#02050A"/>
  <rect x="1" y="1" width="1198" height="178" rx="21" fill="url(#templateGrid)" stroke="#12314A"/>
  <text x="50" y="35" fill="#62E5FF" font-family="Segoe UI, Arial, sans-serif" font-size="12" font-weight="700">TEMPLATE / NOT PROJECT DATA</text>
  <text x="50" y="66" fill="#F4FAFF" font-family="Segoe UI, Arial, sans-serif" font-size="25" font-weight="700">PROJECT NAME</text>
  <text x="50" y="92" fill="#8DA8B8" font-family="Segoe UI, Arial, sans-serif" font-size="15">Measured scope: MEASURED SCOPE</text>
  <text x="1150" y="66" text-anchor="end" fill="#62E5FF" font-family="Segoe UI, Arial, sans-serif" font-size="31" font-weight="700">N/A</text>
  <rect x="50" y="108" width="130" height="26" rx="13" fill="#07111C" stroke="#0088FF"/>
  <text x="115" y="126" text-anchor="middle" fill="#62E5FF" font-family="Segoe UI, Arial, sans-serif" font-size="12" font-weight="700">PLANNING</text>
  <text x="198" y="126" fill="#F4FAFF" font-family="Segoe UI, Arial, sans-serif" font-size="14">Counter: N/A</text>
  <rect x="50" y="148" width="1100" height="14" rx="7" fill="#07111C" stroke="#24475E"/>
</svg>
'@

$expected = [ordered]@{
    'progress-card.svg' = Normalize-Text $card
    'progress-mini.svg' = Normalize-Text $mini
    'progress-template.svg' = Normalize-Text $template
}

foreach ($entry in $expected.GetEnumerator()) {
    Assert-XmlDocument -Name $entry.Key -Content $entry.Value
}

if ($Check) {
    foreach ($entry in $expected.GetEnumerator()) {
        $path = Join-Path $assetDir $entry.Key
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Missing generated progress asset: $($entry.Key)"
        }

        $actual = Normalize-Text ([System.IO.File]::ReadAllText($path))
        if ($actual -cne $entry.Value) {
            throw "Stale progress asset detected: $($entry.Key). Run scripts/generate-progress-assets.ps1 and commit the result."
        }
    }

    if (-not $readme.Contains('src="assets/readme/progress-card.svg"')) {
        throw 'README.md does not embed assets/readme/progress-card.svg.'
    }
    $readmeFallback = "**Weighted project progress:** **$overallDisplay%** toward 1.0 · **Completed roadmap stages:** **$completedStages/$totalStages** · **Release readiness:** tracked separately in [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md)."
    if (-not $readme.Contains($readmeFallback)) {
        throw 'README.md progress fallback does not match authoritative roadmap data.'
    }

    if (-not $roadmap.Contains('src="../assets/readme/progress-mini.svg"')) {
        throw 'docs/ROADMAP.md does not embed ../assets/readme/progress-mini.svg.'
    }
    $roadmapFallback = "**Current active milestone:** **0.9 Quality, Security + Beta Hardening — $milestoneCompletedDisplay/$milestoneTotalDisplay verified deliverables ($milestoneDisplay%), $milestoneStatus.**"
    if (-not $roadmap.Contains($roadmapFallback)) {
        throw 'docs/ROADMAP.md milestone fallback does not match authoritative roadmap data.'
    }

    Write-Host "Progress assets are current: project $overallDisplay%; milestone 0.9 $milestoneCompletedDisplay/$milestoneTotalDisplay ($milestoneDisplay%)."
    exit 0
}

[System.IO.Directory]::CreateDirectory($assetDir) | Out-Null
foreach ($entry in $expected.GetEnumerator()) {
    Write-Utf8NoBom -Path (Join-Path $assetDir $entry.Key) -Content $entry.Value
}

Write-Host "Generated progress assets from docs/ROADMAP.md: project $overallDisplay%; milestone 0.9 $milestoneCompletedDisplay/$milestoneTotalDisplay ($milestoneDisplay%)."
