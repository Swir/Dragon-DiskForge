[CmdletBinding()]
param(
    [ValidateSet("build", "verify", "self-test")]
    [string]$Mode = "self-test",

    [string]$OutputDirectory = "artifacts/windows",
    [string]$QaKitManifestPath = "artifacts/windows/beta-qa-kit.json",
    [string]$QaKitManifestChecksumFile = "",
    [string]$ExplorerWitnessHelperPath = "scripts/beta-qa-explorer-witness.ps1",
    [string]$ExplorerWitnessGuidePath = "docs/BETA-QA-EXPLORER-WITNESS.md",
    [string]$ExplorerKitPath = "artifacts/windows/beta-qa-explorer-kit.json"
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

    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Assert-ExactCommit {
    param([Parameter(Mandatory = $true)][string]$Commit)

    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Explorer witness companion source commit must be an exact 40-character Git SHA."
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
        fileName = [System.IO.Path]::GetFileName($resolved)
        sha256 = $hash
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
        fileName = $fileName
        sha256 = $actualHash
    }
}

function Copy-BoundFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $source = (Resolve-Path -LiteralPath $SourcePath).Path
    $directory = Split-Path -Parent $DestinationPath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Copy-Item -LiteralPath $source -Destination $DestinationPath -Force
    if (-not (Test-Path -LiteralPath $DestinationPath -PathType Leaf)) {
        throw "$Label was not copied into the Explorer witness companion."
    }

    return Write-Sha256Sidecar -Path $DestinationPath
}

function Get-CoreQaKitIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [string]$ManifestSidecarPath = ""
    )

    $proof = Assert-FileSidecar -FilePath $ManifestPath -SidecarPath $ManifestSidecarPath -Label "Core QA kit manifest"
    $manifest = Get-Content -LiteralPath $proof.path -Raw | ConvertFrom-Json

    if ([int]$manifest.schemaVersion -ne 2) { throw "Explorer witness companion requires core beta QA kit schema 2." }
    if ([string]$manifest.kind -ne "DragonDiskForgeBetaQaKit") { throw "Unexpected core QA kit kind '$($manifest.kind)'." }
    if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected core QA kit product '$($manifest.product)'." }
    if ([string]$manifest.version -ne "0.5.0-beta.1") { throw "Unexpected core QA kit version '$($manifest.version)'." }
    if ([string]$manifest.architecture -ne "x64") { throw "Core QA kit architecture must be x64." }

    $sourceCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit)
    if ([string]$manifest.workflowRunId -notmatch '^[0-9]+$') { throw "Core QA kit workflowRunId is missing or invalid." }
    if ([bool]$manifest.publicRelease) { throw "Explorer witness companion expects non-public beta-candidate evidence." }

    $directory = Split-Path -Parent $proof.path
    $packageName = Assert-SafeLeafName -Name ([string]$manifest.packageFile) -Label "Candidate package"
    $packageProof = Assert-FileSidecar -FilePath (Join-Path $directory $packageName) -Label "Candidate package"
    if ([string]$manifest.packageSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Core QA kit packageSha256 is missing or invalid." }
    if ($packageProof.sha256 -ne ([string]$manifest.packageSha256).ToLowerInvariant()) {
        throw "Core QA kit package SHA-256 does not match the candidate package."
    }

    return [pscustomobject]@{
        proof = $proof
        package = $packageProof
        version = [string]$manifest.version
        architecture = [string]$manifest.architecture
        sourceCommit = $sourceCommit
        workflowRunId = [string]$manifest.workflowRunId
    }
}

function Invoke-VerifyExplorerKit {
    param([Parameter(Mandatory = $true)][string]$ManifestFile)

    $runningVerifier = Assert-FileSidecar -FilePath $PSCommandPath -Label "Explorer witness companion verifier"
    $manifestProof = Assert-FileSidecar -FilePath $ManifestFile -Label "Explorer witness companion manifest"
    $manifest = Get-Content -LiteralPath $manifestProof.path -Raw | ConvertFrom-Json

    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported Explorer witness companion schema '$($manifest.schemaVersion)'; expected schema 1." }
    if ([string]$manifest.kind -ne "DragonDiskForgeBetaQaExplorerKit") { throw "Unexpected Explorer witness companion kind '$($manifest.kind)'." }
    if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected Explorer witness companion product '$($manifest.product)'." }
    if ([string]$manifest.version -ne "0.5.0-beta.1") { throw "Unexpected Explorer witness companion version '$($manifest.version)'." }
    if ([string]$manifest.architecture -ne "x64") { throw "Explorer witness companion architecture must be x64." }

    $sourceCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit)
    if ([string]$manifest.workflowRunId -notmatch '^[0-9]+$') { throw "Explorer witness companion workflowRunId is missing or invalid." }
    if ([bool]$manifest.publicRelease) { throw "Explorer witness companion must remain non-public beta-candidate evidence." }
    if ([bool]$manifest.humanGateClaimed) { throw "Explorer witness companion may never claim a human beta gate passed." }
    if ([string]$manifest.packageSha256 -notmatch '^[0-9a-f]{64}$') { throw "Explorer witness companion package SHA-256 is missing or invalid." }

    $directory = Split-Path -Parent $manifestProof.path
    $packageName = Assert-SafeLeafName -Name ([string]$manifest.packageFile) -Label "Candidate package"
    $coreManifestName = Assert-SafeLeafName -Name ([string]$manifest.qaKitManifestFile) -Label "Core QA kit manifest"
    $verifierName = Assert-SafeLeafName -Name ([string]$manifest.verifierFile) -Label "Explorer witness companion verifier"
    $helperName = Assert-SafeLeafName -Name ([string]$manifest.explorerWitnessHelperFile) -Label "Explorer witness helper"
    $guideName = Assert-SafeLeafName -Name ([string]$manifest.explorerWitnessGuideFile) -Label "Explorer witness guide"

    if ($runningVerifier.fileName -ne $verifierName) { throw "Running Explorer witness verifier file name does not match the manifest." }
    if ($runningVerifier.sha256 -ne ([string]$manifest.verifierSha256).ToLowerInvariant()) { throw "Running Explorer witness verifier SHA-256 does not match the manifest." }

    $packageProof = Assert-FileSidecar -FilePath (Join-Path $directory $packageName) -Label "Candidate package"
    $coreProof = Assert-FileSidecar -FilePath (Join-Path $directory $coreManifestName) -Label "Core QA kit manifest"
    $helperProof = Assert-FileSidecar -FilePath (Join-Path $directory $helperName) -Label "Explorer witness helper"
    $guideProof = Assert-FileSidecar -FilePath (Join-Path $directory $guideName) -Label "Explorer witness guide"

    if ($packageProof.sha256 -ne ([string]$manifest.packageSha256).ToLowerInvariant()) { throw "Candidate package SHA-256 does not match the Explorer witness companion." }
    if ($coreProof.sha256 -ne ([string]$manifest.qaKitManifestSha256).ToLowerInvariant()) { throw "Core QA kit manifest SHA-256 does not match the Explorer witness companion." }
    if ($helperProof.sha256 -ne ([string]$manifest.explorerWitnessHelperSha256).ToLowerInvariant()) { throw "Explorer witness helper SHA-256 does not match the companion." }
    if ($guideProof.sha256 -ne ([string]$manifest.explorerWitnessGuideSha256).ToLowerInvariant()) { throw "Explorer witness guide SHA-256 does not match the companion." }

    $core = Get-Content -LiteralPath $coreProof.path -Raw | ConvertFrom-Json
    if ([int]$core.schemaVersion -ne 2 -or [string]$core.kind -ne "DragonDiskForgeBetaQaKit" -or [string]$core.product -ne "Dragon DiskForge") {
        throw "Core QA kit identity is invalid."
    }
    if ([string]$core.version -ne [string]$manifest.version -or [string]$core.architecture -ne [string]$manifest.architecture) {
        throw "Explorer witness companion version/architecture does not match the core QA kit."
    }
    if ((Assert-ExactCommit -Commit ([string]$core.sourceCommit)) -ne $sourceCommit) { throw "Explorer witness companion source commit does not match the core QA kit." }
    if ([string]$core.workflowRunId -ne [string]$manifest.workflowRunId) { throw "Explorer witness companion workflow run does not match the core QA kit." }
    if ([bool]$core.publicRelease) { throw "Core QA kit unexpectedly claims a public release." }
    if ([string]$core.packageFile -ne $packageName) { throw "Explorer witness companion package file does not match the core QA kit." }
    if (([string]$core.packageSha256).ToLowerInvariant() -ne $packageProof.sha256) { throw "Explorer witness companion package SHA-256 does not match the core QA kit." }

    Write-Host "Dragon DiskForge beta QA Explorer witness companion verification passed."
    Write-Host "Version: $($manifest.version) / $($manifest.architecture)"
    Write-Host "Source commit: $sourceCommit"
    Write-Host "Workflow run: $($manifest.workflowRunId)"
    Write-Host "Package SHA-256: $($packageProof.sha256)"
    Write-Host "Core QA kit manifest SHA-256: $($coreProof.sha256)"
    Write-Host "Explorer witness helper SHA-256: $($helperProof.sha256)"
    Write-Host "Explorer witness guide SHA-256: $($guideProof.sha256)"
    Write-Host "Human gate claimed: false"
}

function Invoke-BuildExplorerKit {
    $core = Get-CoreQaKitIdentity -ManifestPath $QaKitManifestPath -ManifestSidecarPath $QaKitManifestChecksumFile
    $output = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (-not (Test-Path -LiteralPath $output)) {
        New-Item -ItemType Directory -Path $output -Force | Out-Null
    }

    if ((Split-Path -Parent $core.proof.path) -ne $output) {
        throw "Core QA kit manifest must be in the Explorer witness output directory so the binding remains self-contained."
    }
    if ((Split-Path -Parent $core.package.path) -ne $output) {
        throw "Candidate package must be in the Explorer witness output directory so the binding remains self-contained."
    }

    $verifierProof = Copy-BoundFile -SourcePath $PSCommandPath -DestinationPath (Join-Path $output "beta-qa-explorer-kit.ps1") -Label "Explorer witness companion verifier"
    $helperProof = Copy-BoundFile -SourcePath $ExplorerWitnessHelperPath -DestinationPath (Join-Path $output "beta-qa-explorer-witness.ps1") -Label "Explorer witness helper"
    $guideProof = Copy-BoundFile -SourcePath $ExplorerWitnessGuidePath -DestinationPath (Join-Path $output "BETA-QA-EXPLORER-WITNESS.md") -Label "Explorer witness guide"

    $manifest = [ordered]@{
        schemaVersion = 1
        kind = "DragonDiskForgeBetaQaExplorerKit"
        product = "Dragon DiskForge"
        version = $core.version
        architecture = $core.architecture
        sourceCommit = $core.sourceCommit
        workflowRunId = $core.workflowRunId
        packageFile = $core.package.fileName
        packageSha256 = $core.package.sha256
        qaKitManifestFile = $core.proof.fileName
        qaKitManifestSha256 = $core.proof.sha256
        verifierFile = $verifierProof.fileName
        verifierSha256 = $verifierProof.sha256
        explorerWitnessHelperFile = $helperProof.fileName
        explorerWitnessHelperSha256 = $helperProof.sha256
        explorerWitnessGuideFile = $guideProof.fileName
        explorerWitnessGuideSha256 = $guideProof.sha256
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        humanGateClaimed = $false
        publicRelease = $false
    }

    $manifestPath = [System.IO.Path]::GetFullPath($ExplorerKitPath)
    if ((Split-Path -Parent $manifestPath) -ne $output) {
        throw "Explorer witness manifest must be written into the output directory."
    }

    Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
    Write-Sha256Sidecar -Path $manifestPath | Out-Null

    & $verifierProof.path -Mode verify -ExplorerKitPath $manifestPath

    Write-Host "Built and verified hash-bound Explorer witness companion."
    Write-Host "Candidate package SHA-256: $($core.package.sha256)"
    Write-Host "Core QA kit SHA-256: $($core.proof.sha256)"
    Write-Host "Explorer witness helper SHA-256: $($helperProof.sha256)"
    Write-Host "Explorer witness guide SHA-256: $($guideProof.sha256)"
    Write-Host "Human gate claimed: false"
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-explorer-kit-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    try {
        $packagePath = Join-Path $tempRoot "DragonDiskForge-win-x64.zip"
        Write-Utf8NoBom -Path $packagePath -Text "candidate-package-bytes"
        $packageProof = Write-Sha256Sidecar -Path $packagePath

        $corePath = Join-Path $tempRoot "beta-qa-kit.json"
        $core = [ordered]@{
            schemaVersion = 2
            kind = "DragonDiskForgeBetaQaKit"
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            sourceCommit = ("a" * 40)
            workflowRunId = "123456"
            packageFile = $packageProof.fileName
            packageSha256 = $packageProof.sha256
            publicRelease = $false
        }
        Write-Utf8NoBom -Path $corePath -Text (($core | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        Write-Sha256Sidecar -Path $corePath | Out-Null

        $manifestPath = Join-Path $tempRoot "beta-qa-explorer-kit.json"
        $savedOutput = $OutputDirectory
        $savedCore = $QaKitManifestPath
        $savedCoreSidecar = $QaKitManifestChecksumFile
        $savedManifest = $ExplorerKitPath
        try {
            $script:OutputDirectory = $tempRoot
            $script:QaKitManifestPath = $corePath
            $script:QaKitManifestChecksumFile = "$corePath.sha256"
            $script:ExplorerKitPath = $manifestPath
            Invoke-BuildExplorerKit
        }
        finally {
            $script:OutputDirectory = $savedOutput
            $script:QaKitManifestPath = $savedCore
            $script:QaKitManifestChecksumFile = $savedCoreSidecar
            $script:ExplorerKitPath = $savedManifest
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -ne 1 -or [bool]$manifest.humanGateClaimed -or [bool]$manifest.publicRelease) {
            throw "Self-test failed: valid Explorer witness companion metadata is not fail-closed."
        }

        $verifierCopy = Join-Path $tempRoot "beta-qa-explorer-kit.ps1"
        $helperCopy = Join-Path $tempRoot "beta-qa-explorer-witness.ps1"
        Add-Content -LiteralPath $helperCopy -Value "# tampered"
        $tamperRejected = $false
        try { & $verifierCopy -Mode verify -ExplorerKitPath $manifestPath } catch { $tamperRejected = $true }
        if (-not $tamperRejected) { throw "Self-test failed: tampered Explorer witness helper was accepted." }

        Copy-Item -LiteralPath $ExplorerWitnessHelperPath -Destination $helperCopy -Force
        Write-Sha256Sidecar -Path $helperCopy | Out-Null

        $manifest.humanGateClaimed = $true
        Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        Write-Sha256Sidecar -Path $manifestPath | Out-Null
        $gateClaimRejected = $false
        try { & $verifierCopy -Mode verify -ExplorerKitPath $manifestPath } catch { $gateClaimRejected = $true }
        if (-not $gateClaimRejected) { throw "Self-test failed: humanGateClaimed=true was accepted." }

        $manifest.humanGateClaimed = $false
        $manifest.explorerWitnessHelperFile = "..\evil.ps1"
        Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        Write-Sha256Sidecar -Path $manifestPath | Out-Null
        $traversalRejected = $false
        try { & $verifierCopy -Mode verify -ExplorerKitPath $manifestPath } catch { $traversalRejected = $true }
        if (-not $traversalRejected) { throw "Self-test failed: path traversal in Explorer witness helper name was accepted." }

        Write-Host "Dragon DiskForge beta QA Explorer witness companion self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    "build" { Invoke-BuildExplorerKit }
    "verify" { Invoke-VerifyExplorerKit -ManifestFile $ExplorerKitPath }
    "self-test" { Invoke-SelfTest }
    default { throw "Unsupported mode '$Mode'." }
}
