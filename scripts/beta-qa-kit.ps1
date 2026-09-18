[CmdletBinding()]
param(
    [ValidateSet("build", "verify", "self-test")]
    [string]$Mode = "self-test",

    [string]$OutputDirectory = "artifacts/windows",
    [string]$PackagePath = "artifacts/windows/DragonDiskForge-win-x64.zip",
    [string]$PackageChecksumFile = "",
    [string]$CandidateMetadataPath = "artifacts/windows/beta-candidate.json",
    [string]$CandidateMetadataChecksumFile = "",
    [string]$VerifierPath = "scripts/beta-qa-kit-verify.ps1",
    [string]$SessionHelperPath = "scripts/beta-qa-session.ps1",
    [string]$StartGuidePath = "docs/BETA-QA-KIT.md",
    [string]$ManualValidationPath = "docs/MANUAL-VALIDATION.md",
    [string]$QaKitPath = "artifacts/windows/beta-qa-kit.json"
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
    param([Parameter(Mandatory = $true)][string]$Commit)
    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Candidate source commit must be an exact 40-character Git SHA."
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

function Write-Sha256Sidecar {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    $sidecar = "$resolved.sha256"
    Write-Utf8NoBom -Path $sidecar -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($resolved), [Environment]::NewLine)
    return [pscustomobject]@{
        path = $resolved
        sidecar = $sidecar
        sha256 = $hash
        fileName = [System.IO.Path]::GetFileName($resolved)
    }
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
        sha256 = $actualHash
        fileName = $fileName
    }
}

function Get-CandidateContext {
    param(
        [Parameter(Mandatory = $true)][string]$Package,
        [string]$PackageSidecar = "",
        [Parameter(Mandatory = $true)][string]$Metadata,
        [string]$MetadataSidecar = ""
    )

    $packageProof = Assert-FileSidecar -FilePath $Package -SidecarPath $PackageSidecar -Label "Candidate package"
    $metadataProof = Assert-FileSidecar -FilePath $Metadata -SidecarPath $MetadataSidecar -Label "Candidate metadata"
    $candidate = Get-Content -LiteralPath $metadataProof.path -Raw | ConvertFrom-Json

    if ([int]$candidate.schemaVersion -ne 1) { throw "Unsupported beta-candidate metadata schema '$($candidate.schemaVersion)'." }
    if ([string]$candidate.kind -ne "DragonDiskForgeBetaCandidate") { throw "Unexpected beta-candidate metadata kind '$($candidate.kind)'." }
    if ([string]$candidate.product -ne "Dragon DiskForge") { throw "Unexpected beta-candidate product '$($candidate.product)'." }
    if ([string]$candidate.version -ne "0.5.0-beta.1") { throw "Unexpected beta-candidate version '$($candidate.version)'." }
    if ([string]$candidate.architecture -ne "x64") { throw "Beta-candidate architecture must be x64." }

    $sourceCommit = Assert-ExactCommit -Commit ([string]$candidate.sourceCommit)
    if ([string]$candidate.workflowRunId -notmatch '^[0-9]+$') { throw "Candidate workflowRunId is missing or invalid." }
    if ([bool]$candidate.publicRelease) { throw "QA kit expects a retained non-public beta candidate." }
    if ([string]$candidate.packageFile -ne $packageProof.fileName) { throw "Candidate metadata points at a different package file." }
    if ([string]$candidate.packageSha256 -ne $packageProof.sha256) { throw "Candidate metadata package SHA-256 does not match the supplied package." }

    return [pscustomobject]@{
        package = $packageProof
        metadata = $metadataProof
        version = [string]$candidate.version
        architecture = [string]$candidate.architecture
        sourceCommit = $sourceCommit
        workflowRunId = [string]$candidate.workflowRunId
        entryPointSha256 = ([string]$candidate.entryPointSha256).ToLowerInvariant()
        betaManualQaEntryPointSha256 = ([string]$candidate.betaManualQaEntryPointSha256).ToLowerInvariant()
    }
}

function Copy-BoundFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $source = (Resolve-Path -LiteralPath $SourcePath).Path
    $destinationDirectory = Split-Path -Parent $DestinationPath
    if (-not (Test-Path -LiteralPath $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }
    Copy-Item -LiteralPath $source -Destination $DestinationPath -Force
    if (-not (Test-Path -LiteralPath $DestinationPath -PathType Leaf)) {
        throw "$Label was not copied into the QA kit."
    }
    return Write-Sha256Sidecar -Path $DestinationPath
}

function Invoke-BuildKit {
    $context = Get-CandidateContext -Package $PackagePath -PackageSidecar $PackageChecksumFile -Metadata $CandidateMetadataPath -MetadataSidecar $CandidateMetadataChecksumFile
    $output = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (-not (Test-Path -LiteralPath $output)) {
        New-Item -ItemType Directory -Path $output -Force | Out-Null
    }

    $verifierDestination = Join-Path $output "beta-qa-kit-verify.ps1"
    $sessionDestination = Join-Path $output "beta-qa-session.ps1"
    $startGuideDestination = Join-Path $output "BETA-QA-KIT.md"
    $manualDestination = Join-Path $output "BETA-MANUAL-VALIDATION.md"
    $verifierProof = Copy-BoundFile -SourcePath $VerifierPath -DestinationPath $verifierDestination -Label "QA kit verifier"
    $sessionProof = Copy-BoundFile -SourcePath $SessionHelperPath -DestinationPath $sessionDestination -Label "QA session helper"
    $startGuideProof = Copy-BoundFile -SourcePath $StartGuidePath -DestinationPath $startGuideDestination -Label "QA kit start guide"
    $manualProof = Copy-BoundFile -SourcePath $ManualValidationPath -DestinationPath $manualDestination -Label "Manual validation guide"

    $kit = [ordered]@{
        schemaVersion = 2
        kind = "DragonDiskForgeBetaQaKit"
        product = "Dragon DiskForge"
        version = $context.version
        architecture = $context.architecture
        sourceCommit = $context.sourceCommit
        workflowRunId = $context.workflowRunId
        packageFile = $context.package.fileName
        packageSha256 = $context.package.sha256
        candidateMetadataFile = $context.metadata.fileName
        candidateMetadataSha256 = $context.metadata.sha256
        entryPointSha256 = $context.entryPointSha256
        betaManualQaEntryPointSha256 = $context.betaManualQaEntryPointSha256
        verifierFile = $verifierProof.fileName
        verifierSha256 = $verifierProof.sha256
        sessionHelperFile = $sessionProof.fileName
        sessionHelperSha256 = $sessionProof.sha256
        startGuideFile = $startGuideProof.fileName
        startGuideSha256 = $startGuideProof.sha256
        manualValidationFile = $manualProof.fileName
        manualValidationSha256 = $manualProof.sha256
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        publicRelease = $false
    }

    $kitPath = [System.IO.Path]::GetFullPath($QaKitPath)
    Write-Utf8NoBom -Path $kitPath -Text (($kit | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
    Write-Sha256Sidecar -Path $kitPath | Out-Null

    Invoke-VerifyKit -KitPath $kitPath
    Write-Host "Built and verified hash-bound beta QA kit schema v2."
    Write-Host "Source commit: $($context.sourceCommit)"
    Write-Host "Package SHA-256: $($context.package.sha256)"
    Write-Host "Verifier SHA-256: $($verifierProof.sha256)"
    Write-Host "Session helper SHA-256: $($sessionProof.sha256)"
    Write-Host "Start guide SHA-256: $($startGuideProof.sha256)"
    Write-Host "QA kit manifest: $kitPath"
}

function Invoke-VerifyKit {
    param([Parameter(Mandatory = $true)][string]$KitPath)

    $kitProof = Assert-FileSidecar -FilePath $KitPath -Label "QA kit manifest"
    $kit = Get-Content -LiteralPath $kitProof.path -Raw | ConvertFrom-Json
    if ([int]$kit.schemaVersion -ne 2) { throw "Unsupported beta QA kit schema '$($kit.schemaVersion)'; expected schema 2." }
    if ([string]$kit.kind -ne "DragonDiskForgeBetaQaKit") { throw "Unexpected beta QA kit kind '$($kit.kind)'." }
    if ([string]$kit.product -ne "Dragon DiskForge") { throw "Unexpected beta QA kit product '$($kit.product)'." }
    if ([string]$kit.version -ne "0.5.0-beta.1") { throw "Unexpected beta QA kit version '$($kit.version)'." }
    if ([string]$kit.architecture -ne "x64") { throw "Beta QA kit architecture must be x64." }
    $sourceCommit = Assert-ExactCommit -Commit ([string]$kit.sourceCommit)
    if ([string]$kit.workflowRunId -notmatch '^[0-9]+$') { throw "Beta QA kit workflowRunId is missing or invalid." }
    if ([bool]$kit.publicRelease) { throw "Beta QA kit must remain non-public release evidence." }

    $directory = Split-Path -Parent $kitProof.path
    $packageName = Assert-SafeLeafName -Name ([string]$kit.packageFile) -Label "Package"
    $metadataName = Assert-SafeLeafName -Name ([string]$kit.candidateMetadataFile) -Label "Candidate metadata"
    $verifierName = Assert-SafeLeafName -Name ([string]$kit.verifierFile) -Label "QA kit verifier"
    $sessionName = Assert-SafeLeafName -Name ([string]$kit.sessionHelperFile) -Label "Session helper"
    $startGuideName = Assert-SafeLeafName -Name ([string]$kit.startGuideFile) -Label "QA kit start guide"
    $manualName = Assert-SafeLeafName -Name ([string]$kit.manualValidationFile) -Label "Manual validation guide"

    $packageProof = Assert-FileSidecar -FilePath (Join-Path $directory $packageName) -Label "Candidate package"
    $metadataProof = Assert-FileSidecar -FilePath (Join-Path $directory $metadataName) -Label "Candidate metadata"
    $verifierProof = Assert-FileSidecar -FilePath (Join-Path $directory $verifierName) -Label "QA kit verifier"
    $sessionProof = Assert-FileSidecar -FilePath (Join-Path $directory $sessionName) -Label "QA session helper"
    $startGuideProof = Assert-FileSidecar -FilePath (Join-Path $directory $startGuideName) -Label "QA kit start guide"
    $manualProof = Assert-FileSidecar -FilePath (Join-Path $directory $manualName) -Label "Manual validation guide"

    if ($packageProof.sha256 -ne ([string]$kit.packageSha256).ToLowerInvariant()) { throw "QA kit package SHA-256 does not match its manifest." }
    if ($metadataProof.sha256 -ne ([string]$kit.candidateMetadataSha256).ToLowerInvariant()) { throw "QA kit candidate-metadata SHA-256 does not match its manifest." }
    if ($verifierProof.sha256 -ne ([string]$kit.verifierSha256).ToLowerInvariant()) { throw "QA kit verifier SHA-256 does not match its manifest." }
    if ($sessionProof.sha256 -ne ([string]$kit.sessionHelperSha256).ToLowerInvariant()) { throw "QA kit session-helper SHA-256 does not match its manifest." }
    if ($startGuideProof.sha256 -ne ([string]$kit.startGuideSha256).ToLowerInvariant()) { throw "QA kit start-guide SHA-256 does not match its manifest." }
    if ($manualProof.sha256 -ne ([string]$kit.manualValidationSha256).ToLowerInvariant()) { throw "QA kit manual-validation SHA-256 does not match its manifest." }

    $candidate = Get-Content -LiteralPath $metadataProof.path -Raw | ConvertFrom-Json
    if ([int]$candidate.schemaVersion -ne 1 -or [string]$candidate.kind -ne "DragonDiskForgeBetaCandidate") {
        throw "QA kit candidate metadata has an unsupported identity."
    }
    if ([string]$candidate.product -ne "Dragon DiskForge" -or [string]$candidate.version -ne [string]$kit.version -or [string]$candidate.architecture -ne [string]$kit.architecture) {
        throw "QA kit candidate product/version/architecture does not match its manifest."
    }
    if ((Assert-ExactCommit -Commit ([string]$candidate.sourceCommit)) -ne $sourceCommit) { throw "QA kit source commit does not match candidate metadata." }
    if ([string]$candidate.workflowRunId -ne [string]$kit.workflowRunId) { throw "QA kit workflow run does not match candidate metadata." }
    if ([bool]$candidate.publicRelease) { throw "Candidate metadata unexpectedly claims a public release." }
    if ([string]$candidate.packageFile -ne $packageName) { throw "QA kit package file does not match candidate metadata." }
    if ([string]$candidate.packageSha256 -ne $packageProof.sha256) { throw "QA kit package hash does not match candidate metadata." }
    if ([string]$candidate.entryPointSha256 -ne [string]$kit.entryPointSha256) { throw "QA kit desktop entry-point hash does not match candidate metadata." }
    if ([string]$candidate.betaManualQaEntryPointSha256 -ne [string]$kit.betaManualQaEntryPointSha256) { throw "QA kit packaged manual-QA tool hash does not match candidate metadata." }

    Write-Host "Beta QA kit schema v2 verification passed."
    Write-Host "Source commit: $sourceCommit"
    Write-Host "Package SHA-256: $($packageProof.sha256)"
    Write-Host "Verifier SHA-256: $($verifierProof.sha256)"
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-kit-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $package = Join-Path $tempRoot "DragonDiskForge-win-x64.zip"
        Write-Utf8NoBom -Path $package -Text "self-test-package"
        $packageProof = Write-Sha256Sidecar -Path $package

        $verifier = Join-Path $tempRoot "source-verifier.ps1"
        Write-Utf8NoBom -Path $verifier -Text "Write-Host 'verifier'"
        $helper = Join-Path $tempRoot "source-helper.ps1"
        Write-Utf8NoBom -Path $helper -Text "Write-Host 'helper'"
        $startGuide = Join-Path $tempRoot "source-start-guide.md"
        Write-Utf8NoBom -Path $startGuide -Text "# Beta QA Kit"
        $manual = Join-Path $tempRoot "source-manual.md"
        Write-Utf8NoBom -Path $manual -Text "# Manual QA"

        $metadata = Join-Path $tempRoot "beta-candidate.json"
        $candidate = [ordered]@{
            schemaVersion = 1
            kind = "DragonDiskForgeBetaCandidate"
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            sourceCommit = ("a" * 40)
            workflowRunId = "123456"
            packageFile = [System.IO.Path]::GetFileName($package)
            packageSha256 = $packageProof.sha256
            packageManifestSchema = 5
            entryPointSha256 = ("b" * 64)
            betaManualQaEntryPointSha256 = ("c" * 64)
            createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
            publicRelease = $false
        }
        Write-Utf8NoBom -Path $metadata -Text (($candidate | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        Write-Sha256Sidecar -Path $metadata | Out-Null

        $oldOutput = $script:OutputDirectory
        $oldPackage = $script:PackagePath
        $oldPackageChecksum = $script:PackageChecksumFile
        $oldMetadata = $script:CandidateMetadataPath
        $oldMetadataChecksum = $script:CandidateMetadataChecksumFile
        $oldVerifier = $script:VerifierPath
        $oldHelper = $script:SessionHelperPath
        $oldStartGuide = $script:StartGuidePath
        $oldManual = $script:ManualValidationPath
        $oldKit = $script:QaKitPath
        try {
            $script:OutputDirectory = $tempRoot
            $script:PackagePath = $package
            $script:PackageChecksumFile = "$package.sha256"
            $script:CandidateMetadataPath = $metadata
            $script:CandidateMetadataChecksumFile = "$metadata.sha256"
            $script:VerifierPath = $verifier
            $script:SessionHelperPath = $helper
            $script:StartGuidePath = $startGuide
            $script:ManualValidationPath = $manual
            $script:QaKitPath = Join-Path $tempRoot "beta-qa-kit.json"
            Invoke-BuildKit

            Add-Content -LiteralPath (Join-Path $tempRoot "beta-qa-kit-verify.ps1") -Value "# tampered"
            $verifierTamperRejected = $false
            try { Invoke-VerifyKit -KitPath $script:QaKitPath } catch { $verifierTamperRejected = $true }
            if (-not $verifierTamperRejected) { throw "Self-test failed: tampered QA kit verifier was accepted." }

            Copy-Item -LiteralPath $verifier -Destination (Join-Path $tempRoot "beta-qa-kit-verify.ps1") -Force
            Write-Sha256Sidecar -Path (Join-Path $tempRoot "beta-qa-kit-verify.ps1") | Out-Null
            Add-Content -LiteralPath (Join-Path $tempRoot "BETA-QA-KIT.md") -Value "# tampered"
            $guideTamperRejected = $false
            try { Invoke-VerifyKit -KitPath $script:QaKitPath } catch { $guideTamperRejected = $true }
            if (-not $guideTamperRejected) { throw "Self-test failed: tampered QA kit start guide was accepted." }
        }
        finally {
            $script:OutputDirectory = $oldOutput
            $script:PackagePath = $oldPackage
            $script:PackageChecksumFile = $oldPackageChecksum
            $script:CandidateMetadataPath = $oldMetadata
            $script:CandidateMetadataChecksumFile = $oldMetadataChecksum
            $script:VerifierPath = $oldVerifier
            $script:SessionHelperPath = $oldHelper
            $script:StartGuidePath = $oldStartGuide
            $script:ManualValidationPath = $oldManual
            $script:QaKitPath = $oldKit
        }

        Write-Host "Dragon DiskForge beta QA kit schema v2 contract self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    "build" {
        Invoke-BuildKit
        exit 0
    }
    "verify" {
        Invoke-VerifyKit -KitPath $QaKitPath
        exit 0
    }
    "self-test" {
        Invoke-SelfTest
        exit 0
    }
}
