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

function Assert-HexSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Label must be a 64-character SHA-256 value."
    }
}

function Read-RetainedCandidateEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained beta candidate evidence is missing: $Path"
    }

    $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $schemaVersion = [int]$evidence.schemaVersion
    if ($schemaVersion -ne 1 -and $schemaVersion -ne 2) {
        throw "Unsupported retained beta candidate evidence schema '$schemaVersion'."
    }
    if ([string]$evidence.kind -ne "DragonDiskForgeRetainedBetaCandidateEvidence") { throw "Unexpected retained beta candidate evidence kind." }
    if ([string]$evidence.product -ne "Dragon DiskForge") { throw "Unexpected retained beta candidate product." }
    if ([string]$evidence.version -ne "0.5.0-beta.1") { throw "Unexpected retained beta candidate version '$($evidence.version)'." }
    if ([string]$evidence.architecture -ne "x64") { throw "Retained beta candidate architecture must be x64." }
    if ([string]$evidence.sourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw "Retained candidate sourceCommit must be an exact Git SHA." }
    if ([string]$evidence.workflowRunId -notmatch '^[1-9][0-9]*$') { throw "Retained candidate workflowRunId must be a positive integer string." }
    if ([int64]$evidence.workflowRunNumber -le 0) { throw "Retained candidate workflowRunNumber must be positive." }
    if ([string]$evidence.artifactId -notmatch '^[1-9][0-9]*$') { throw "Retained candidate artifactId must be a positive integer string." }
    Assert-HexSha256 -Value ([string]$evidence.artifactDigestSha256) -Label "artifactDigestSha256"
    Assert-HexSha256 -Value ([string]$evidence.packageSha256) -Label "packageSha256"
    Assert-HexSha256 -Value ([string]$evidence.candidateMetadataSha256) -Label "candidateMetadataSha256"
    Assert-HexSha256 -Value ([string]$evidence.qaKitManifestSha256) -Label "qaKitManifestSha256"
    Assert-HexSha256 -Value ([string]$evidence.entryPointSha256) -Label "entryPointSha256"
    Assert-HexSha256 -Value ([string]$evidence.betaManualQaEntryPointSha256) -Label "betaManualQaEntryPointSha256"

    if ([string]$evidence.artifactName -ne "DragonDiskForge-$($evidence.version)-win-x64-candidate-$($evidence.workflowRunId)") {
        throw "Retained candidate artifactName does not match version/run identity."
    }
    if ([string]$evidence.packageFile -ne "DragonDiskForge-win-x64.zip") { throw "Unexpected retained package filename." }

    $packageManifestSchema = [int]$evidence.packageManifestSchema
    if ($packageManifestSchema -ne 5 -and $packageManifestSchema -ne 6) {
        throw "Retained package evidence must bind package manifest schema 5 or 6."
    }
    if ($packageManifestSchema -eq 6) {
        if (-not ($evidence.PSObject.Properties.Name -contains 'betaUacWitnessEntryPointSha256')) {
            throw "Package manifest schema 6 retained evidence must bind betaUacWitnessEntryPointSha256."
        }
        Assert-HexSha256 -Value ([string]$evidence.betaUacWitnessEntryPointSha256) -Label "betaUacWitnessEntryPointSha256"
    }

    if ([int]$evidence.qaKitSchema -ne 2) { throw "Retained package evidence must bind beta QA kit schema 2." }

    if ($schemaVersion -eq 2) {
        if ([int]$evidence.witnessKitSchema -ne 1) { throw "Retained schema v2 must bind beta QA witness kit schema 1." }
        Assert-HexSha256 -Value ([string]$evidence.witnessKitManifestSha256) -Label "witnessKitManifestSha256"
        Assert-HexSha256 -Value ([string]$evidence.witnessVerifierSha256) -Label "witnessVerifierSha256"
        Assert-HexSha256 -Value ([string]$evidence.desktopWitnessHelperSha256) -Label "desktopWitnessHelperSha256"
        Assert-HexSha256 -Value ([string]$evidence.desktopWitnessGuideSha256) -Label "desktopWitnessGuideSha256"
    }

    if ([string]$evidence.runtimeDeployment.dotNet -ne "self-contained") { throw ".NET runtime deployment must be self-contained." }
    if ([string]$evidence.runtimeDeployment.windowsAppSdk -ne "self-contained") { throw "Windows App SDK runtime deployment must be self-contained." }
    if ([string]$evidence.runtimeDeployment.visualCpp -ne "app-local") { throw "Visual C++ runtime deployment must be app-local." }
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

function Get-DocumentationSpecs {
    return @(
        @{ Path = 'README.md'; EvidenceLink = 'docs/retained-beta-candidate.json' },
        @{ Path = 'docs/ROADMAP.md'; EvidenceLink = 'retained-beta-candidate.json' },
        @{ Path = 'docs/STATUS.md'; EvidenceLink = 'retained-beta-candidate.json' },
        @{ Path = 'docs/MILESTONES.md'; EvidenceLink = 'retained-beta-candidate.json' },
        @{ Path = 'docs/BETA-RELEASE.md'; EvidenceLink = 'retained-beta-candidate.json' }
    )
}

function Test-DocumentationSet {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)]$Evidence
    )

    $tokens = Get-CanonicalTokens -Evidence $Evidence
    foreach ($spec in (Get-DocumentationSpecs)) {
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
        if ([int]$Evidence.schemaVersion -eq 2 -and $block -notmatch '(?i)witness-bound|witness companion') {
            throw "$($spec.Path) retained-candidate block must state that schema-v2 evidence is witness-bound."
        }
        if ([int]$Evidence.packageManifestSchema -eq 6 -and $block -notmatch '(?i)package-UAC-witness-bound|UAC witness') {
            throw "$($spec.Path) retained-candidate block must state that package schema 6 is bound to the packaged UAC witness."
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
    $witnessText = if ([int]$Evidence.schemaVersion -eq 2) { ' Evidence is witness-bound to the packaged desktop witness companion.' } else { '' }
    $uacWitnessText = if ([int]$Evidence.packageManifestSchema -eq 6) { ' Evidence is package-UAC-witness-bound to the packaged normal-user UAC witness companion.' } else { '' }
    return @"
$StartMarker
Current retained candidate: Beta Candidate run #$($Evidence.workflowRunNumber) ($($Evidence.workflowRunId)), artifact $($Evidence.artifactName), source commit $($Evidence.sourceCommit), package SHA-256 $($Evidence.packageSha256).$witnessText$uacWitnessText This is a non-public engineering candidate, not a public release. Authoritative evidence: [$EvidenceLink]($EvidenceLink).
$EndMarker
"@
}

function New-SelfTestEvidence {
    param(
        [int]$SchemaVersion,
        [int]$PackageManifestSchema = 5
    )

    $evidence = [ordered]@{
        schemaVersion = $SchemaVersion
        kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
        product = 'Dragon DiskForge'
        version = '0.5.0-beta.1'
        architecture = 'x64'
        sourceCommit = ('a' * 40)
        workflowRunId = '123456'
        workflowRunNumber = 79
        artifactId = '654321'
        artifactName = 'DragonDiskForge-0.5.0-beta.1-win-x64-candidate-123456'
        artifactDigestSha256 = ('1' * 64)
        artifactCreatedAtUtc = '2026-09-18T00:00:00Z'
        artifactExpiresAtUtc = '2026-10-02T00:00:00Z'
        packageFile = 'DragonDiskForge-win-x64.zip'
        packageSha256 = ('b' * 64)
        candidateMetadataSha256 = ('c' * 64)
        qaKitManifestSha256 = ('d' * 64)
        packageManifestSchema = $PackageManifestSchema
        qaKitSchema = 2
        entryPointSha256 = ('e' * 64)
        betaManualQaEntryPointSha256 = ('f' * 64)
        runtimeDeployment = [ordered]@{
            dotNet = 'self-contained'
            windowsAppSdk = 'self-contained'
            visualCpp = 'app-local'
        }
        publicRelease = $false
        betaReady = $false
    }
    if ($SchemaVersion -eq 2) {
        $evidence.witnessKitSchema = 1
        $evidence.witnessKitManifestSha256 = ('2' * 64)
        $evidence.witnessVerifierSha256 = ('3' * 64)
        $evidence.desktopWitnessHelperSha256 = ('4' * 64)
        $evidence.desktopWitnessGuideSha256 = ('5' * 64)
    }
    if ($PackageManifestSchema -eq 6) {
        $evidence.betaUacWitnessEntryPointSha256 = ('6' * 64)
    }
    return $evidence
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-docs-consistency-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path (Join-Path $root 'docs') -Force | Out-Null
    try {
        foreach ($schema in @(1, 2)) {
            $evidence = New-SelfTestEvidence -SchemaVersion $schema
            $evidencePath = Join-Path $root 'docs/retained-beta-candidate.json'
            Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
            $parsed = Read-RetainedCandidateEvidence -Path $evidencePath

            foreach ($spec in (Get-DocumentationSpecs)) {
                Write-Utf8NoBom -Path (Join-Path $root $spec.Path) -Text (New-SelfTestBlock -Evidence $parsed -EvidenceLink $spec.EvidenceLink)
            }
            Test-DocumentationSet -Root $root -Evidence $parsed
        }

        $parsedV2 = Read-RetainedCandidateEvidence -Path (Join-Path $root 'docs/retained-beta-candidate.json')
        $badPath = Join-Path $root 'docs/STATUS.md'
        $badText = (Get-Content -LiteralPath $badPath -Raw).Replace(([string]$parsedV2.packageSha256), ('9' * 64))
        Write-Utf8NoBom -Path $badPath -Text $badText
        $rejected = $false
        try { Test-DocumentationSet -Root $root -Evidence $parsedV2 } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: stale package SHA-256 in documentation was accepted." }

        $badWitness = New-SelfTestEvidence -SchemaVersion 2
        $badWitness.desktopWitnessHelperSha256 = '00'
        Write-Utf8NoBom -Path (Join-Path $root 'docs/retained-beta-candidate.json') -Text (($badWitness | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
        $rejected = $false
        try { $null = Read-RetainedCandidateEvidence -Path (Join-Path $root 'docs/retained-beta-candidate.json') } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: invalid witness SHA-256 was accepted." }

        $schema6 = New-SelfTestEvidence -SchemaVersion 2 -PackageManifestSchema 6
        Write-Utf8NoBom -Path (Join-Path $root 'docs/retained-beta-candidate.json') -Text (($schema6 | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
        $parsedSchema6 = Read-RetainedCandidateEvidence -Path (Join-Path $root 'docs/retained-beta-candidate.json')
        foreach ($spec in (Get-DocumentationSpecs)) {
            Write-Utf8NoBom -Path (Join-Path $root $spec.Path) -Text (New-SelfTestBlock -Evidence $parsedSchema6 -EvidenceLink $spec.EvidenceLink)
        }
        Test-DocumentationSet -Root $root -Evidence $parsedSchema6

        $missingUac = New-SelfTestEvidence -SchemaVersion 2 -PackageManifestSchema 6
        $missingUac.Remove('betaUacWitnessEntryPointSha256')
        Write-Utf8NoBom -Path (Join-Path $root 'docs/retained-beta-candidate.json') -Text (($missingUac | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
        $rejected = $false
        try { $null = Read-RetainedCandidateEvidence -Path (Join-Path $root 'docs/retained-beta-candidate.json') } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: package schema 6 without a UAC witness SHA-256 was accepted." }

        $badUac = New-SelfTestEvidence -SchemaVersion 2 -PackageManifestSchema 6
        $badUac.betaUacWitnessEntryPointSha256 = '00'
        Write-Utf8NoBom -Path (Join-Path $root 'docs/retained-beta-candidate.json') -Text (($badUac | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
        $rejected = $false
        try { $null = Read-RetainedCandidateEvidence -Path (Join-Path $root 'docs/retained-beta-candidate.json') } catch { $rejected = $true }
        if (-not $rejected) { throw "Self-test failed: invalid package UAC witness SHA-256 was accepted." }

        Write-Host "Dragon DiskForge beta documentation consistency self-test passed for retained evidence schemas v1/v2 and package manifest schemas 5/6."
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
Write-Host ("Beta documentation is synchronized to retained candidate schema {0}, run #{1} ({2}), source {3}, package SHA-256 {4}." -f $evidence.schemaVersion, $evidence.workflowRunNumber, $evidence.workflowRunId, $evidence.sourceCommit, $evidence.packageSha256)
