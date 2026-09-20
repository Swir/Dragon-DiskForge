param(
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExpectedRepository = 'Swir/Dragon-DiskForge'
$EvidencePathVariable = 'DDF_DISPOSABLE_EVIDENCE_PATH'
$HarnessRelativePath = 'tests/DragonDiskForge.PhysicalMediaWrite.DisposableTests/Program.cs'
$HarnessProjectRelativePath = 'tests/DragonDiskForge.PhysicalMediaWrite.DisposableTests/DragonDiskForge.PhysicalMediaWrite.DisposableTests.csproj'
$EvidenceVerifierRelativePath = 'scripts/verify-disposable-physical-media-evidence.ps1'
$ProvenanceVerifierRelativePath = 'scripts/verify-disposable-physical-media-provenance.ps1'
$WrapperRelativePath = 'scripts/run-disposable-physical-media-validation.ps1'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
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

function Resolve-CanonicalSourceRoot {
    $root = (& git rev-parse --show-toplevel 2>&1)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($root -join ''))) {
        throw 'Final disposable-media validation must run from a Git checkout of Swir/Dragon-DiskForge.'
    }
    $full = [IO.Path]::GetFullPath((($root | ForEach-Object { [string]$_ }) -join '').Trim())

    $remote = Invoke-GitText -Root $full -Arguments @('config', '--get', 'remote.origin.url')
    $accepted = @(
        'https://github.com/Swir/Dragon-DiskForge.git',
        'https://github.com/Swir/Dragon-DiskForge',
        'git@github.com:Swir/Dragon-DiskForge.git'
    )
    Assert-True ($accepted -contains $remote) "origin must point to canonical $ExpectedRepository; actual: $remote"

    $status = Invoke-GitText -Root $full -Arguments @('status', '--porcelain', '--untracked-files=no')
    Assert-True ([string]::IsNullOrWhiteSpace($status)) 'Final disposable-media validation refuses a checkout with tracked modifications.'
    return $full
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "Required source file is missing: $Path"
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Get-BytesSha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha.ComputeHash($Bytes)
    }
    finally {
        $sha.Dispose()
    }
    return ([BitConverter]::ToString($digest)).Replace('-', '').ToUpperInvariant()
}

function Write-ProvenancePair {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Document
    )

    $sidecarPath = $Path + '.sha256'
    if (Test-Path -LiteralPath $Path -PathType Leaf) { throw 'Disposable-media provenance output already exists. Existing provenance is never overwritten.' }
    if (Test-Path -LiteralPath $sidecarPath -PathType Leaf) { throw 'Disposable-media provenance sidecar already exists. Existing provenance is never overwritten.' }

    $json = $Document | ConvertTo-Json -Depth 8
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json + [Environment]::NewLine)
    $hash = Get-BytesSha256 $bytes
    $tmpJson = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $tmpSidecar = $sidecarPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'

    try {
        [IO.File]::WriteAllBytes($tmpJson, $bytes)
        [IO.File]::WriteAllText($tmpSidecar, "$hash  $([IO.Path]::GetFileName($Path))$([Environment]::NewLine)", [Text.UTF8Encoding]::new($false))
        [IO.File]::Move($tmpJson, $Path)
        try {
            [IO.File]::Move($tmpSidecar, $sidecarPath)
        }
        catch {
            Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
            throw
        }
    }
    finally {
        Remove-Item -LiteralPath $tmpJson -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $tmpSidecar -Force -ErrorAction SilentlyContinue
    }
}

$sourceRoot = Resolve-CanonicalSourceRoot
$provenanceVerifier = Join-Path $sourceRoot $ProvenanceVerifierRelativePath

if ($SelfTest) {
    & $provenanceVerifier -SelfTest
    Write-Host 'PASS  Disposable-media validation wrapper self-test passed without destructive opt-in.'
    exit 0
}

$evidencePathValue = [Environment]::GetEnvironmentVariable($EvidencePathVariable)
if ([string]::IsNullOrWhiteSpace($evidencePathValue)) {
    throw "Required environment variable $EvidencePathVariable is missing."
}
$evidencePath = [IO.Path]::GetFullPath($evidencePathValue)
$evidenceSidecarPath = $evidencePath + '.sha256'
$provenancePath = $evidencePath + '.provenance.json'
$provenanceSidecarPath = $provenancePath + '.sha256'

if (Test-Path -LiteralPath $provenancePath -PathType Leaf) { throw 'Provenance JSON already exists; choose a fresh evidence path.' }
if (Test-Path -LiteralPath $provenanceSidecarPath -PathType Leaf) { throw 'Provenance sidecar already exists; choose a fresh evidence path.' }

$sourceCommit = Invoke-GitText -Root $sourceRoot -Arguments @('rev-parse', 'HEAD')
Assert-True ($sourceCommit -match '^[0-9A-Fa-f]{40}$') 'Git HEAD is not a canonical 40-hex commit SHA.'

$harnessSourceHash = Get-FileSha256 (Join-Path $sourceRoot $HarnessRelativePath)
$evidenceVerifierHash = Get-FileSha256 (Join-Path $sourceRoot $EvidenceVerifierRelativePath)
$wrapperSourceHash = Get-FileSha256 (Join-Path $sourceRoot $WrapperRelativePath)
$harnessProject = Join-Path $sourceRoot $HarnessProjectRelativePath
$baseVerifier = Join-Path $sourceRoot $EvidenceVerifierRelativePath

Write-Host "INFO  Canonical source commit: $sourceCommit"
Write-Host 'INFO  Tracked source tree is clean; starting the existing fail-closed disposable-media harness.'

Push-Location $sourceRoot
try {
    & dotnet run --project $harnessProject -c Release
    $dotnetExit = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($dotnetExit -ne 0) { throw "Disposable-media harness failed with exit code $dotnetExit." }

Assert-True (Test-Path -LiteralPath $evidencePath -PathType Leaf) 'Harness completed without producing the required evidence JSON.'
Assert-True (Test-Path -LiteralPath $evidenceSidecarPath -PathType Leaf) 'Harness completed without producing the required evidence sidecar.'

& $baseVerifier -EvidencePath $evidencePath

$hardwareEvidenceHash = Get-FileSha256 $evidencePath
$hardwareEvidenceSidecarHash = Get-FileSha256 $evidenceSidecarPath
$document = [ordered]@{
    SchemaVersion = 1
    Result = 'pass'
    CompletedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    RepositoryFullName = $ExpectedRepository
    SourceCommitSha = $sourceCommit.ToLowerInvariant()
    SourceTreeClean = $true
    HardwareEvidenceSha256 = $hardwareEvidenceHash
    HardwareEvidenceSidecarSha256 = $hardwareEvidenceSidecarHash
    HarnessSourceSha256 = $harnessSourceHash
    EvidenceVerifierSha256 = $evidenceVerifierHash
    WrapperSourceSha256 = $wrapperSourceHash
}
Write-ProvenancePair -Path $provenancePath -Document $document

& $provenanceVerifier -EvidencePath $evidencePath -ProvenancePath $provenancePath -SourceRoot $sourceRoot -ExpectedSourceCommit $sourceCommit

Write-Host 'PASS  Disposable physical-media hardware evidence is now source-provenance-bound.'
Write-Host "PASS  Source commit: $sourceCommit"
Write-Host "PASS  Evidence: $evidencePath"
Write-Host "PASS  Provenance: $provenancePath"
