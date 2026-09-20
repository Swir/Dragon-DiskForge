param(
    [string]$EvidencePath,
    [string]$ProvenancePath,
    [string]$SourceRoot,
    [string]$ExpectedSourceCommit,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExpectedRepository = 'Swir/Dragon-DiskForge'
$HarnessRelativePath = 'tests/DragonDiskForge.PhysicalMediaWrite.DisposableTests/Program.cs'
$EvidenceVerifierRelativePath = 'scripts/verify-disposable-physical-media-evidence.ps1'
$WrapperRelativePath = 'scripts/run-disposable-physical-media-validation.ps1'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Sha256 {
    param([string]$Value, [string]$Name)
    Assert-True ($Value -match '^[0-9A-Fa-f]{64}$') "$Name must be a 64-hex SHA-256 value."
}

function Assert-CommitSha {
    param([string]$Value, [string]$Name)
    Assert-True ($Value -match '^[0-9A-Fa-f]{40}$') "$Name must be a 40-hex Git commit SHA."
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "Required file is missing: $Path"
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = & git -C $Root @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    return (($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()
}

function Assert-CanonicalRemote {
    param([string]$Remote)
    $accepted = @(
        'https://github.com/Swir/Dragon-DiskForge.git',
        'https://github.com/Swir/Dragon-DiskForge',
        'git@github.com:Swir/Dragon-DiskForge.git'
    )
    Assert-True ($accepted -contains $Remote) "origin must point to canonical $ExpectedRepository; actual: $Remote"
}

function Test-ProvenanceDocument {
    param(
        [Parameter(Mandatory = $true)]$Provenance,
        [Parameter(Mandatory = $true)][string]$ActualEvidenceSha256,
        [Parameter(Mandatory = $true)][string]$ActualEvidenceSidecarSha256,
        [string]$ExpectedCommit
    )

    Assert-True ([int]$Provenance.SchemaVersion -eq 1) 'Unsupported disposable-media provenance schema.'
    Assert-True ([string]$Provenance.Result -ceq 'pass') 'Provenance does not record a passing validation.'
    Assert-True ([string]$Provenance.RepositoryFullName -ceq $ExpectedRepository) 'RepositoryFullName is not canonical.'
    Assert-True ([bool]$Provenance.SourceTreeClean) 'SourceTreeClean must be true.'

    $commit = [string]$Provenance.SourceCommitSha
    Assert-CommitSha $commit 'SourceCommitSha'
    if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit)) {
        Assert-CommitSha $ExpectedCommit 'ExpectedSourceCommit'
        Assert-True ($commit.ToUpperInvariant() -ceq $ExpectedCommit.ToUpperInvariant()) 'Provenance source commit does not match ExpectedSourceCommit.'
    }

    foreach ($name in @('HardwareEvidenceSha256', 'HardwareEvidenceSidecarSha256', 'HarnessSourceSha256', 'EvidenceVerifierSha256', 'WrapperSourceSha256')) {
        Assert-Sha256 ([string]$Provenance.$name) $name
    }

    Assert-True (([string]$Provenance.HardwareEvidenceSha256).ToUpperInvariant() -ceq $ActualEvidenceSha256) 'HardwareEvidenceSha256 does not match the evidence JSON.'
    Assert-True (([string]$Provenance.HardwareEvidenceSidecarSha256).ToUpperInvariant() -ceq $ActualEvidenceSidecarSha256) 'HardwareEvidenceSidecarSha256 does not match the evidence sidecar.'

    $completed = [DateTimeOffset]::MinValue
    Assert-True ([DateTimeOffset]::TryParse([string]$Provenance.CompletedUtc, [ref]$completed)) 'CompletedUtc is not a valid timestamp.'
    Assert-True ($completed -le [DateTimeOffset]::UtcNow.AddMinutes(5)) 'CompletedUtc is implausibly in the future.'

    return [pscustomobject]@{
        SourceCommitSha = $commit.ToLowerInvariant()
        CompletedUtc = $completed.ToUniversalTime().ToString('o')
    }
}

function Test-ProvenancePackage {
    param(
        [Parameter(Mandatory = $true)][string]$Evidence,
        [Parameter(Mandatory = $true)][string]$ProvenanceFile,
        [Parameter(Mandatory = $true)][string]$CheckoutRoot,
        [string]$ExpectedCommit
    )

    $evidenceFull = [IO.Path]::GetFullPath($Evidence)
    $provenanceFull = [IO.Path]::GetFullPath($ProvenanceFile)
    $rootFull = [IO.Path]::GetFullPath($CheckoutRoot)

    Assert-True (Test-Path -LiteralPath $evidenceFull -PathType Leaf) "Evidence JSON does not exist: $evidenceFull"
    Assert-True (Test-Path -LiteralPath ($evidenceFull + '.sha256') -PathType Leaf) 'Evidence SHA-256 sidecar is missing.'
    Assert-True (Test-Path -LiteralPath $provenanceFull -PathType Leaf) "Provenance JSON does not exist: $provenanceFull"
    Assert-True (Test-Path -LiteralPath ($provenanceFull + '.sha256') -PathType Leaf) 'Provenance SHA-256 sidecar is missing.'

    $provenanceSidecar = (Get-Content -LiteralPath ($provenanceFull + '.sha256') -Raw).Trim()
    Assert-True ($provenanceSidecar -match '^([0-9A-Fa-f]{64})  (.+)$') 'Provenance sidecar must contain: <64-hex SHA-256><two spaces><json filename>.'
    $expectedProvenanceHash = $Matches[1].ToUpperInvariant()
    $expectedProvenanceName = $Matches[2]
    Assert-True ($expectedProvenanceName -ceq [IO.Path]::GetFileName($provenanceFull)) 'Provenance sidecar filename does not exactly match the JSON filename.'
    $actualProvenanceHash = Get-FileSha256 $provenanceFull
    Assert-True ($actualProvenanceHash -ceq $expectedProvenanceHash) 'Provenance JSON SHA-256 does not match its sidecar.'

    $actualEvidenceHash = Get-FileSha256 $evidenceFull
    $actualEvidenceSidecarHash = Get-FileSha256 ($evidenceFull + '.sha256')
    $provenance = Get-Content -LiteralPath $provenanceFull -Raw | ConvertFrom-Json
    $summary = Test-ProvenanceDocument -Provenance $provenance -ActualEvidenceSha256 $actualEvidenceHash -ActualEvidenceSidecarSha256 $actualEvidenceSidecarHash -ExpectedCommit $ExpectedCommit

    $remote = Invoke-GitText -Root $rootFull -Arguments @('config', '--get', 'remote.origin.url')
    Assert-CanonicalRemote $remote
    $head = Invoke-GitText -Root $rootFull -Arguments @('rev-parse', 'HEAD')
    Assert-CommitSha $head 'checkout HEAD'
    Assert-True ($head.ToUpperInvariant() -ceq $summary.SourceCommitSha.ToUpperInvariant()) 'Checked-out HEAD does not match the provenance source commit.'

    $trackedStatus = Invoke-GitText -Root $rootFull -Arguments @('status', '--porcelain', '--untracked-files=no')
    Assert-True ([string]::IsNullOrWhiteSpace($trackedStatus)) 'Checked-out source tree has tracked modifications; provenance verification refuses a dirty tree.'

    $harnessHash = Get-FileSha256 (Join-Path $rootFull $HarnessRelativePath)
    $evidenceVerifierHash = Get-FileSha256 (Join-Path $rootFull $EvidenceVerifierRelativePath)
    $wrapperHash = Get-FileSha256 (Join-Path $rootFull $WrapperRelativePath)
    Assert-True ($harnessHash -ceq ([string]$provenance.HarnessSourceSha256).ToUpperInvariant()) 'Harness source hash does not match provenance.'
    Assert-True ($evidenceVerifierHash -ceq ([string]$provenance.EvidenceVerifierSha256).ToUpperInvariant()) 'Evidence verifier source hash does not match provenance.'
    Assert-True ($wrapperHash -ceq ([string]$provenance.WrapperSourceSha256).ToUpperInvariant()) 'Wrapper source hash does not match provenance.'

    $baseVerifier = Join-Path $rootFull $EvidenceVerifierRelativePath
    & $baseVerifier -EvidencePath $evidenceFull
    if ($LASTEXITCODE -ne 0) { throw 'Base disposable-media evidence verifier failed.' }

    return [pscustomobject]@{
        SourceCommitSha = $summary.SourceCommitSha
        ProvenanceSha256 = $actualProvenanceHash
        EvidenceSha256 = $actualEvidenceHash
        CompletedUtc = $summary.CompletedUtc
    }
}

function Write-SyntheticFileWithSidecar {
    param([string]$Path, [string]$Content)
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
    $hash = Get-FileSha256 $Path
    [IO.File]::WriteAllText($Path + '.sha256', "$hash  $([IO.Path]::GetFileName($Path))$([Environment]::NewLine)", [Text.UTF8Encoding]::new($false))
}

if ($SelfTest) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('ddf-disposable-provenance-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    try {
        $evidence = Join-Path $root 'evidence.json'
        Write-SyntheticFileWithSidecar -Path $evidence -Content "{}`n"
        $evidenceHash = Get-FileSha256 $evidence
        $evidenceSidecarHash = Get-FileSha256 ($evidence + '.sha256')
        $commit = ('c' * 40)
        $provenance = [pscustomobject]@{
            SchemaVersion = 1
            Result = 'pass'
            RepositoryFullName = $ExpectedRepository
            SourceCommitSha = $commit
            SourceTreeClean = $true
            HardwareEvidenceSha256 = $evidenceHash
            HardwareEvidenceSidecarSha256 = $evidenceSidecarHash
            HarnessSourceSha256 = ('a' * 64)
            EvidenceVerifierSha256 = ('b' * 64)
            WrapperSourceSha256 = ('d' * 64)
            CompletedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        }
        $null = Test-ProvenanceDocument -Provenance $provenance -ActualEvidenceSha256 $evidenceHash -ActualEvidenceSidecarSha256 $evidenceSidecarHash -ExpectedCommit $commit

        $rejectedCommit = $false
        try { $null = Test-ProvenanceDocument -Provenance $provenance -ActualEvidenceSha256 $evidenceHash -ActualEvidenceSidecarSha256 $evidenceSidecarHash -ExpectedCommit ('e' * 40) } catch { $rejectedCommit = $true }
        Assert-True $rejectedCommit 'Mismatched expected source commit was not rejected.'

        $provenance.SourceTreeClean = $false
        $rejectedDirty = $false
        try { $null = Test-ProvenanceDocument -Provenance $provenance -ActualEvidenceSha256 $evidenceHash -ActualEvidenceSidecarSha256 $evidenceSidecarHash -ExpectedCommit $commit } catch { $rejectedDirty = $true }
        Assert-True $rejectedDirty 'Dirty-source provenance was not rejected.'

        Write-Host 'PASS  Disposable-media source-provenance verifier self-test passed.'
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ([string]::IsNullOrWhiteSpace($EvidencePath) -or [string]::IsNullOrWhiteSpace($ProvenancePath) -or [string]::IsNullOrWhiteSpace($SourceRoot)) {
    throw 'Provide -EvidencePath, -ProvenancePath and -SourceRoot, or use -SelfTest.'
}

$result = Test-ProvenancePackage -Evidence $EvidencePath -ProvenanceFile $ProvenancePath -CheckoutRoot $SourceRoot -ExpectedCommit $ExpectedSourceCommit
Write-Host 'PASS  Disposable-media hardware evidence is bound to a clean canonical source checkout.'
Write-Host ("PASS  source-commit={0} evidence-sha256={1} provenance-sha256={2} completed={3}" -f $result.SourceCommitSha, $result.EvidenceSha256, $result.ProvenanceSha256, $result.CompletedUtc)
