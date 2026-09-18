[CmdletBinding()]
param(
    [ValidateSet("verify", "self-test")]
    [string]$Mode = "self-test",

    [string]$CandidateMetadataPath = "artifacts/windows/beta-candidate.json",
    [string]$CandidateMetadataChecksumFile = "",
    [string]$QaKitManifestPath = "artifacts/windows/beta-qa-kit.json",
    [string]$QaKitManifestChecksumFile = "",
    [string]$PackagePath = "artifacts/windows/DragonDiskForge-win-x64.zip",
    [string]$PackageChecksumFile = "",
    [string]$EvidencePath = "artifacts/manual-qa/beta-manual-qa.json",
    [string]$ExpectedVersion = "0.5.0-beta.1",
    [string]$ExpectedSourceCommit = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Assert-ExactCommit {
    param(
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "$Label must be an exact 40-character Git commit SHA."
    }
    return $Commit.ToLowerInvariant()
}

function Assert-SafeLeafName {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "$Label file name is empty."
    }
    if ([System.IO.Path]::GetFileName($Name) -ne $Name -or $Name.Contains('/') -or $Name.Contains('\')) {
        throw "$Label must be a leaf file name without path traversal."
    }
    return $Name
}

function Assert-FileSidecar {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][string]$Label
    )

    $file = (Resolve-Path -LiteralPath $FilePath).Path
    if ([string]::IsNullOrWhiteSpace($SidecarPath)) {
        $SidecarPath = "$file.sha256"
    }
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path

    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "$Label SHA-256 sidecar has an invalid format."
    }

    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    $fileName = [System.IO.Path]::GetFileName($file)
    if ($declaredName -ne $fileName) {
        throw "$Label SHA-256 sidecar targets '$declaredName' instead of '$fileName'."
    }

    $actualHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "$Label SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'."
    }

    return [pscustomobject]@{
        path = $file
        sidecar = $sidecar
        fileName = $fileName
        sha256 = $actualHash
    }
}

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][string]$Version
    )

    $packageProof = Assert-FileSidecar -FilePath $ZipPath -SidecarPath $SidecarPath -Label "Package"
    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-release-proof-package-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $packageProof.path -DestinationPath $workspace -Force
        $manifestPath = Join-Path $workspace "package-manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Candidate package manifest is missing."
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -lt 5) { throw "Candidate package manifest schema predates the beta evidence contract." }
        if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected package product '$($manifest.product)'." }
        if ([string]$manifest.version -ne $Version) { throw "Package version '$($manifest.version)' does not match '$Version'." }
        if ([string]$manifest.architecture -ne "x64") { throw "Candidate package architecture must be x64." }

        $entryPoint = Join-Path $workspace ([string]$manifest.entryPoint)
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) { throw "Candidate desktop entry point is missing." }
        $entryHash = (Get-FileHash -LiteralPath $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($entryHash -ne ([string]$manifest.entryPointSha256).ToLowerInvariant()) {
            throw "Candidate desktop entry-point SHA-256 does not match its manifest."
        }

        $qaRelative = ([string]$manifest.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $qaTool = Join-Path $workspace $qaRelative
        if (-not (Test-Path -LiteralPath $qaTool -PathType Leaf)) { throw "Candidate packaged beta manual-QA tool is missing." }
        $qaHash = (Get-FileHash -LiteralPath $qaTool -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($qaHash -ne ([string]$manifest.betaManualQaEntryPointSha256).ToLowerInvariant()) {
            throw "Candidate packaged beta manual-QA tool SHA-256 does not match its manifest."
        }

        return [pscustomobject]@{
            path = $packageProof.path
            checksumPath = $packageProof.sidecar
            fileName = $packageProof.fileName
            sha256 = $packageProof.sha256
            version = [string]$manifest.version
            architecture = [string]$manifest.architecture
            manifestSchema = [int]$manifest.schemaVersion
            entryPointSha256 = $entryHash
            betaManualQaEntryPoint = [string]$manifest.betaManualQaEntryPoint
            betaManualQaEntryPointSha256 = $qaHash
        }
    }
    finally {
        if (Test-Path -LiteralPath $workspace) {
            Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-CandidateIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$MetadataPath,
        [string]$MetadataSidecarPath = "",
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$SourceCommit
    )

    $metadataProof = Assert-FileSidecar -FilePath $MetadataPath -SidecarPath $MetadataSidecarPath -Label "Candidate metadata"
    $metadata = Get-Content -LiteralPath $metadataProof.path -Raw | ConvertFrom-Json

    if ([int]$metadata.schemaVersion -ne 1) { throw "Unsupported beta-candidate metadata schema '$($metadata.schemaVersion)'." }
    if ([string]$metadata.kind -ne "DragonDiskForgeBetaCandidate") { throw "Unexpected beta-candidate metadata kind '$($metadata.kind)'." }
    if ([string]$metadata.product -ne "Dragon DiskForge") { throw "Unexpected beta-candidate product '$($metadata.product)'." }
    if ([string]$metadata.version -ne $Version) { throw "Beta-candidate metadata version '$($metadata.version)' does not match '$Version'." }
    if ([string]$metadata.architecture -ne "x64") { throw "Beta-candidate metadata architecture must be x64." }

    $metadataCommit = Assert-ExactCommit -Commit ([string]$metadata.sourceCommit) -Label "Candidate sourceCommit"
    if ($metadataCommit -ne $SourceCommit) { throw "Candidate source commit '$metadataCommit' does not match expected final source '$SourceCommit'." }
    if ([string]$metadata.workflowRunId -notmatch '^[0-9]+$') { throw "Candidate workflowRunId is missing or invalid." }
    if ([bool]$metadata.publicRelease) { throw "Candidate metadata is already marked as a public release; expected a verified non-public candidate." }

    if ([string]$metadata.packageFile -ne [string]$PackageIdentity.fileName) { throw "Candidate metadata points at a different package filename." }
    if ([string]$metadata.packageSha256 -ne [string]$PackageIdentity.sha256) { throw "Candidate metadata package SHA-256 does not match the supplied package." }
    if ([int]$metadata.packageManifestSchema -ne [int]$PackageIdentity.manifestSchema) { throw "Candidate metadata manifest schema does not match the supplied package." }
    if ([string]$metadata.entryPointSha256 -ne [string]$PackageIdentity.entryPointSha256) { throw "Candidate metadata desktop entry-point SHA-256 does not match the supplied package." }
    if ([string]$metadata.betaManualQaEntryPointSha256 -ne [string]$PackageIdentity.betaManualQaEntryPointSha256) { throw "Candidate metadata manual-QA tool SHA-256 does not match the supplied package." }

    return [pscustomobject]@{
        path = $metadataProof.path
        checksumPath = $metadataProof.sidecar
        fileName = $metadataProof.fileName
        sha256 = $metadataProof.sha256
        sourceCommit = $metadataCommit
        workflowRunId = [string]$metadata.workflowRunId
        packageSha256 = [string]$metadata.packageSha256
        entryPointSha256 = [string]$metadata.entryPointSha256
        betaManualQaEntryPointSha256 = [string]$metadata.betaManualQaEntryPointSha256
    }
}

function Get-QaKitIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [string]$ManifestSidecarPath = "",
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)]$CandidateIdentity,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$SourceCommit
    )

    $manifestProof = Assert-FileSidecar -FilePath $ManifestPath -SidecarPath $ManifestSidecarPath -Label "QA kit manifest"
    $manifest = Get-Content -LiteralPath $manifestProof.path -Raw | ConvertFrom-Json

    if ([int]$manifest.schemaVersion -ne 2) { throw "Unsupported beta QA kit schema '$($manifest.schemaVersion)'; final release proof requires schema 2." }
    if ([string]$manifest.kind -ne "DragonDiskForgeBetaQaKit") { throw "Unexpected beta QA kit kind '$($manifest.kind)'." }
    if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected beta QA kit product '$($manifest.product)'." }
    if ([string]$manifest.version -ne $Version) { throw "Beta QA kit version '$($manifest.version)' does not match '$Version'." }
    if ([string]$manifest.architecture -ne "x64") { throw "Beta QA kit architecture must be x64." }
    $manifestCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit) -Label "QA kit sourceCommit"
    if ($manifestCommit -ne $SourceCommit) { throw "QA kit source commit '$manifestCommit' does not match expected final source '$SourceCommit'." }
    if ([string]$manifest.workflowRunId -notmatch '^[0-9]+$') { throw "QA kit workflowRunId is missing or invalid." }
    if ([bool]$manifest.publicRelease) { throw "QA kit already claims a public release; expected retained non-public evidence." }

    if ([string]$manifest.workflowRunId -ne [string]$CandidateIdentity.workflowRunId) { throw "QA kit workflow run does not match candidate metadata." }
    if ([string]$manifest.packageFile -ne [string]$PackageIdentity.fileName) { throw "QA kit package filename does not match the supplied package." }
    if ([string]$manifest.packageSha256 -ne [string]$PackageIdentity.sha256) { throw "QA kit package SHA-256 does not match the supplied package." }
    if ([string]$manifest.candidateMetadataFile -ne [string]$CandidateIdentity.fileName) { throw "QA kit candidate-metadata filename does not match the supplied metadata." }
    if ([string]$manifest.candidateMetadataSha256 -ne [string]$CandidateIdentity.sha256) { throw "QA kit candidate-metadata SHA-256 does not match the supplied metadata." }
    if ([string]$manifest.entryPointSha256 -ne [string]$PackageIdentity.entryPointSha256) { throw "QA kit desktop entry-point SHA-256 does not match the supplied package." }
    if ([string]$manifest.betaManualQaEntryPointSha256 -ne [string]$PackageIdentity.betaManualQaEntryPointSha256) { throw "QA kit packaged manual-QA SHA-256 does not match the supplied package." }

    $directory = Split-Path -Parent $manifestProof.path
    $verifierName = Assert-SafeLeafName -Name ([string]$manifest.verifierFile) -Label "QA kit verifier"
    $sessionName = Assert-SafeLeafName -Name ([string]$manifest.sessionHelperFile) -Label "QA session helper"
    $startGuideName = Assert-SafeLeafName -Name ([string]$manifest.startGuideFile) -Label "QA kit start guide"
    $manualName = Assert-SafeLeafName -Name ([string]$manifest.manualValidationFile) -Label "Manual validation guide"

    $verifierProof = Assert-FileSidecar -FilePath (Join-Path $directory $verifierName) -Label "QA kit verifier"
    $sessionProof = Assert-FileSidecar -FilePath (Join-Path $directory $sessionName) -Label "QA session helper"
    $startGuideProof = Assert-FileSidecar -FilePath (Join-Path $directory $startGuideName) -Label "QA kit start guide"
    $manualProof = Assert-FileSidecar -FilePath (Join-Path $directory $manualName) -Label "Manual validation guide"

    if ($verifierProof.sha256 -ne ([string]$manifest.verifierSha256).ToLowerInvariant()) { throw "QA kit verifier SHA-256 does not match its manifest." }
    if ($sessionProof.sha256 -ne ([string]$manifest.sessionHelperSha256).ToLowerInvariant()) { throw "QA kit session-helper SHA-256 does not match its manifest." }
    if ($startGuideProof.sha256 -ne ([string]$manifest.startGuideSha256).ToLowerInvariant()) { throw "QA kit start-guide SHA-256 does not match its manifest." }
    if ($manualProof.sha256 -ne ([string]$manifest.manualValidationSha256).ToLowerInvariant()) { throw "QA kit manual-validation SHA-256 does not match its manifest." }

    return [pscustomobject]@{
        path = $manifestProof.path
        checksumPath = $manifestProof.sidecar
        sha256 = $manifestProof.sha256
        sourceCommit = $manifestCommit
        workflowRunId = [string]$manifest.workflowRunId
        verifierSha256 = $verifierProof.sha256
        sessionHelperSha256 = $sessionProof.sha256
        startGuideSha256 = $startGuideProof.sha256
        manualValidationSha256 = $manualProof.sha256
    }
}

function Invoke-PackagedQaVerification {
    param(
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)][string]$EvidenceFile,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw "Final beta release proof verification must run on Windows so the packaged manual-QA verifier executes in its supported environment."
    }

    $evidenceProof = Assert-FileSidecar -FilePath $EvidenceFile -Label "Manual QA evidence"
    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-release-proof-qa-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $PackageIdentity.path -DestinationPath $workspace -Force
        $qaRelative = ([string]$PackageIdentity.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $qaTool = Join-Path $workspace $qaRelative
        if (-not (Test-Path -LiteralPath $qaTool -PathType Leaf)) { throw "Packaged beta manual-QA verifier is missing after extraction." }
        $qaHash = (Get-FileHash -LiteralPath $qaTool -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($qaHash -ne [string]$PackageIdentity.betaManualQaEntryPointSha256) { throw "Extracted beta manual-QA verifier hash changed unexpectedly." }

        & $qaTool -Mode verify -EvidencePath $evidenceProof.path -PackagePath $PackageIdentity.path -ChecksumFile $PackageIdentity.checksumPath -ExpectedVersion $Version
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged beta manual-QA verifier exited with code $LASTEXITCODE."
        }

        return $evidenceProof
    }
    finally {
        if (Test-Path -LiteralPath $workspace) {
            Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-TestSidecar {
    param([Parameter(Mandatory = $true)][string]$Path)
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Utf8NoBom -Path "$Path.sha256" -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($Path), [Environment]::NewLine)
    return $hash
}

function Invoke-SelfTest {
    $commit = "a" * 40
    $package = [pscustomobject]@{
        fileName = "DragonDiskForge-win-x64.zip"
        sha256 = ("b" * 64)
        version = "0.5.0-beta.1"
        architecture = "x64"
        manifestSchema = 5
        entryPointSha256 = ("c" * 64)
        betaManualQaEntryPoint = "tools/beta-manual-qa.ps1"
        betaManualQaEntryPointSha256 = ("d" * 64)
    }

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-release-proof-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $metadataPath = Join-Path $tempRoot "beta-candidate.json"
        $metadata = [ordered]@{
            schemaVersion = 1
            kind = "DragonDiskForgeBetaCandidate"
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            sourceCommit = $commit
            workflowRunId = "123456"
            packageFile = $package.fileName
            packageSha256 = $package.sha256
            packageManifestSchema = $package.manifestSchema
            entryPointSha256 = $package.entryPointSha256
            betaManualQaEntryPointSha256 = $package.betaManualQaEntryPointSha256
            createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
            publicRelease = $false
        }
        Write-Utf8NoBom -Path $metadataPath -Text (($metadata | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        Write-TestSidecar -Path $metadataPath | Out-Null

        $candidate = Get-CandidateIdentity -MetadataPath $metadataPath -PackageIdentity $package -Version "0.5.0-beta.1" -SourceCommit $commit
        if ([string]$candidate.sourceCommit -ne $commit -or [string]$candidate.packageSha256 -ne [string]$package.sha256) {
            throw "Self-test failed: valid candidate metadata did not preserve exact source/package identity."
        }

        $wrongCommitRejected = $false
        try { Get-CandidateIdentity -MetadataPath $metadataPath -PackageIdentity $package -Version "0.5.0-beta.1" -SourceCommit ("e" * 40) | Out-Null } catch { $wrongCommitRejected = $true }
        if (-not $wrongCommitRejected) { throw "Self-test failed: mismatched source commit was accepted." }

        $wrongPackage = $package.PSObject.Copy()
        $wrongPackage.sha256 = "f" * 64
        $wrongPackageRejected = $false
        try { Get-CandidateIdentity -MetadataPath $metadataPath -PackageIdentity $wrongPackage -Version "0.5.0-beta.1" -SourceCommit $commit | Out-Null } catch { $wrongPackageRejected = $true }
        if (-not $wrongPackageRejected) { throw "Self-test failed: mismatched package SHA-256 was accepted." }

        $kitPackagePath = Join-Path $tempRoot $package.fileName
        Write-Utf8NoBom -Path $kitPackagePath -Text "self-test-package"
        $package.sha256 = Write-TestSidecar -Path $kitPackagePath
        $metadata.packageSha256 = $package.sha256
        Write-Utf8NoBom -Path $metadataPath -Text (($metadata | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        Write-TestSidecar -Path $metadataPath | Out-Null
        $candidate = Get-CandidateIdentity -MetadataPath $metadataPath -PackageIdentity $package -Version "0.5.0-beta.1" -SourceCommit $commit

        $verifierPath = Join-Path $tempRoot "beta-qa-kit-verify.ps1"
        $sessionPath = Join-Path $tempRoot "beta-qa-session.ps1"
        $startGuidePath = Join-Path $tempRoot "BETA-QA-KIT.md"
        $manualPath = Join-Path $tempRoot "BETA-MANUAL-VALIDATION.md"
        Write-Utf8NoBom -Path $verifierPath -Text "Write-Host 'verifier'"
        Write-Utf8NoBom -Path $sessionPath -Text "Write-Host 'session'"
        Write-Utf8NoBom -Path $startGuidePath -Text "# QA kit"
        Write-Utf8NoBom -Path $manualPath -Text "# Manual QA"
        $verifierHash = Write-TestSidecar -Path $verifierPath
        $sessionHash = Write-TestSidecar -Path $sessionPath
        $startGuideHash = Write-TestSidecar -Path $startGuidePath
        $manualHash = Write-TestSidecar -Path $manualPath

        $kitPath = Join-Path $tempRoot "beta-qa-kit.json"
        $kit = [ordered]@{
            schemaVersion = 2
            kind = "DragonDiskForgeBetaQaKit"
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            sourceCommit = $commit
            workflowRunId = $candidate.workflowRunId
            packageFile = $package.fileName
            packageSha256 = $package.sha256
            candidateMetadataFile = $candidate.fileName
            candidateMetadataSha256 = $candidate.sha256
            entryPointSha256 = $package.entryPointSha256
            betaManualQaEntryPointSha256 = $package.betaManualQaEntryPointSha256
            verifierFile = [System.IO.Path]::GetFileName($verifierPath)
            verifierSha256 = $verifierHash
            sessionHelperFile = [System.IO.Path]::GetFileName($sessionPath)
            sessionHelperSha256 = $sessionHash
            startGuideFile = [System.IO.Path]::GetFileName($startGuidePath)
            startGuideSha256 = $startGuideHash
            manualValidationFile = [System.IO.Path]::GetFileName($manualPath)
            manualValidationSha256 = $manualHash
            createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
            publicRelease = $false
        }
        Write-Utf8NoBom -Path $kitPath -Text (($kit | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        Write-TestSidecar -Path $kitPath | Out-Null

        $kitIdentity = Get-QaKitIdentity -ManifestPath $kitPath -PackageIdentity $package -CandidateIdentity $candidate -Version "0.5.0-beta.1" -SourceCommit $commit
        if ([string]$kitIdentity.workflowRunId -ne [string]$candidate.workflowRunId -or [string]$kitIdentity.verifierSha256 -ne $verifierHash) {
            throw "Self-test failed: valid QA kit did not preserve candidate/verifier identity."
        }

        Add-Content -LiteralPath $sessionPath -Value "# tampered"
        $kitTamperRejected = $false
        try { Get-QaKitIdentity -ManifestPath $kitPath -PackageIdentity $package -CandidateIdentity $candidate -Version "0.5.0-beta.1" -SourceCommit $commit | Out-Null } catch { $kitTamperRejected = $true }
        if (-not $kitTamperRejected) { throw "Self-test failed: tampered QA kit companion was accepted." }

        $invalidCommitRejected = $false
        try { Assert-ExactCommit -Commit "abc123" -Label "Self-test commit" | Out-Null } catch { $invalidCommitRejected = $true }
        if (-not $invalidCommitRejected) { throw "Self-test failed: invalid source commit syntax was accepted." }

        Write-Host "Dragon DiskForge beta release proof contract self-test passed with QA kit schema v2 binding."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    "self-test" {
        Invoke-SelfTest
        exit 0
    }

    "verify" {
        $sourceCommit = Assert-ExactCommit -Commit $ExpectedSourceCommit -Label "ExpectedSourceCommit"
        $package = Get-PackageIdentity -ZipPath $PackagePath -SidecarPath $PackageChecksumFile -Version $ExpectedVersion
        $candidate = Get-CandidateIdentity -MetadataPath $CandidateMetadataPath -MetadataSidecarPath $CandidateMetadataChecksumFile -PackageIdentity $package -Version $ExpectedVersion -SourceCommit $sourceCommit
        $qaKit = Get-QaKitIdentity -ManifestPath $QaKitManifestPath -ManifestSidecarPath $QaKitManifestChecksumFile -PackageIdentity $package -CandidateIdentity $candidate -Version $ExpectedVersion -SourceCommit $sourceCommit
        $evidence = Invoke-PackagedQaVerification -PackageIdentity $package -EvidenceFile $EvidencePath -Version $ExpectedVersion

        Write-Host "Dragon DiskForge beta release proof is COMPLETE for the exact candidate source, package, QA kit and manual QA evidence."
        Write-Host "Version: $ExpectedVersion"
        Write-Host "Source commit: $($candidate.sourceCommit)"
        Write-Host "Candidate workflow run: $($candidate.workflowRunId)"
        Write-Host "Candidate metadata SHA-256: $($candidate.sha256)"
        Write-Host "Package SHA-256: $($package.sha256)"
        Write-Host "QA kit manifest SHA-256: $($qaKit.sha256)"
        Write-Host "QA kit verifier SHA-256: $($qaKit.verifierSha256)"
        Write-Host "Manual QA evidence SHA-256: $($evidence.sha256)"
        exit 0
    }
}
