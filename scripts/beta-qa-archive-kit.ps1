[CmdletBinding()]
param(
    [ValidateSet("build", "verify", "self-test")]
    [string]$Mode = "self-test",

    [string]$OutputDirectory = "artifacts/windows",
    [string]$QaKitManifestPath = "artifacts/windows/beta-qa-kit.json",
    [string]$QaKitManifestChecksumFile = "",
    [string]$WitnessKitManifestPath = "artifacts/windows/beta-qa-witness-kit.json",
    [string]$WitnessKitManifestChecksumFile = "",
    [string]$VerifierPath = "scripts/beta-qa-archive-kit-verify.ps1",
    [string]$ArchiveHelperPath = "scripts/beta-qa-archive.ps1",
    [string]$ArchiveGuidePath = "docs/BETA-QA-EVIDENCE-ARCHIVE.md",
    [string]$ArchiveKitPath = "artifacts/windows/beta-qa-archive-kit.json"
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
    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') { throw "Source commit must be an exact 40-character Git SHA." }
    return $Commit.ToLowerInvariant()
}

function Assert-FileSidecar {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][string]$Label
    )
    $file = (Resolve-Path -LiteralPath $FilePath).Path
    if ([string]::IsNullOrWhiteSpace($SidecarPath)) { $SidecarPath = "$file.sha256" }
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path
    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') { throw "$Label SHA-256 sidecar has an invalid format." }
    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    $fileName = [System.IO.Path]::GetFileName($file)
    if ($declaredName -ne $fileName) { throw "$Label SHA-256 sidecar targets '$declaredName' instead of '$fileName'." }
    $actualHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) { throw "$Label SHA-256 mismatch." }
    return [pscustomobject]@{ path = $file; sidecar = $sidecar; fileName = $fileName; sha256 = $actualHash }
}

function Write-Sha256Sidecar {
    param([Parameter(Mandatory = $true)][string]$Path)
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $hash = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Utf8NoBom -Path "$resolved.sha256" -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($resolved), [Environment]::NewLine)
    return [pscustomobject]@{ path = $resolved; sidecar = "$resolved.sha256"; fileName = [System.IO.Path]::GetFileName($resolved); sha256 = $hash }
}

function Copy-BoundFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$Label
    )
    $source = (Resolve-Path -LiteralPath $SourcePath).Path
    $directory = Split-Path -Parent $DestinationPath
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    Copy-Item -LiteralPath $source -Destination $DestinationPath -Force
    if (-not (Test-Path -LiteralPath $DestinationPath -PathType Leaf)) { throw "$Label was not copied into the archive kit." }
    return Write-Sha256Sidecar -Path $DestinationPath
}

function Get-BoundCandidateContext {
    param(
        [Parameter(Mandatory = $true)][string]$CoreManifestPath,
        [string]$CoreManifestSidecarPath = "",
        [Parameter(Mandatory = $true)][string]$WitnessManifestPath,
        [string]$WitnessManifestSidecarPath = ""
    )

    $coreProof = Assert-FileSidecar -FilePath $CoreManifestPath -SidecarPath $CoreManifestSidecarPath -Label "Core QA kit manifest"
    $witnessProof = Assert-FileSidecar -FilePath $WitnessManifestPath -SidecarPath $WitnessManifestSidecarPath -Label "Desktop witness kit manifest"
    $core = Get-Content -LiteralPath $coreProof.path -Raw | ConvertFrom-Json
    $witness = Get-Content -LiteralPath $witnessProof.path -Raw | ConvertFrom-Json

    if ([int]$core.schemaVersion -ne 2 -or [string]$core.kind -ne "DragonDiskForgeBetaQaKit" -or [string]$core.product -ne "Dragon DiskForge") {
        throw "Portable archive kit requires core beta QA kit schema 2."
    }
    if ([int]$witness.schemaVersion -ne 1 -or [string]$witness.kind -ne "DragonDiskForgeBetaQaWitnessKit" -or [string]$witness.product -ne "Dragon DiskForge") {
        throw "Portable archive kit requires desktop witness kit schema 1."
    }
    if ([bool]$core.publicRelease -or [bool]$witness.publicRelease -or [bool]$witness.humanGateClaimed) {
        throw "Portable archive kit requires fail-closed non-public source manifests."
    }
    if ([string]$core.version -ne "0.5.0-beta.1" -or [string]$core.architecture -ne "x64") {
        throw "Unexpected core QA kit version/architecture."
    }
    if ([string]$witness.version -ne [string]$core.version -or [string]$witness.architecture -ne [string]$core.architecture) {
        throw "Desktop witness kit version/architecture differs from the core QA kit."
    }

    $sourceCommit = Assert-ExactCommit -Commit ([string]$core.sourceCommit)
    if ((Assert-ExactCommit -Commit ([string]$witness.sourceCommit)) -ne $sourceCommit) { throw "Source commit differs between core and witness manifests." }
    if ([string]$core.workflowRunId -notmatch '^[1-9][0-9]*$' -or [string]$witness.workflowRunId -ne [string]$core.workflowRunId) {
        throw "Workflow run differs between core and witness manifests."
    }
    if ([string]$core.packageFile -ne [string]$witness.packageFile) { throw "Package file differs between core and witness manifests." }
    if ([string]$core.packageSha256 -notmatch '^[0-9a-fA-F]{64}$' -or ([string]$witness.packageSha256).ToLowerInvariant() -ne ([string]$core.packageSha256).ToLowerInvariant()) {
        throw "Package SHA-256 differs between core and witness manifests."
    }
    if ([string]$witness.qaKitManifestSha256 -notmatch '^[0-9a-fA-F]{64}$' -or ([string]$witness.qaKitManifestSha256).ToLowerInvariant() -ne $coreProof.sha256) {
        throw "Desktop witness kit is not bound to the supplied core QA kit manifest."
    }

    return [pscustomobject]@{
        coreProof = $coreProof
        witnessProof = $witnessProof
        version = [string]$core.version
        architecture = [string]$core.architecture
        sourceCommit = $sourceCommit
        workflowRunId = [string]$core.workflowRunId
        packageFile = [string]$core.packageFile
        packageSha256 = ([string]$core.packageSha256).ToLowerInvariant()
    }
}

function Invoke-StandaloneVerify {
    param(
        [Parameter(Mandatory = $true)][string]$VerifierFile,
        [Parameter(Mandatory = $true)][string]$ManifestFile
    )
    & $VerifierFile -ManifestPath $ManifestFile
}

function Invoke-BuildArchiveKit {
    param(
        [Parameter(Mandatory = $true)][string]$Output,
        [Parameter(Mandatory = $true)][string]$CoreManifest,
        [string]$CoreManifestSidecar,
        [Parameter(Mandatory = $true)][string]$WitnessManifest,
        [string]$WitnessManifestSidecar,
        [Parameter(Mandatory = $true)][string]$VerifierSource,
        [Parameter(Mandatory = $true)][string]$HelperSource,
        [Parameter(Mandatory = $true)][string]$GuideSource,
        [Parameter(Mandatory = $true)][string]$ManifestOutput
    )

    $context = Get-BoundCandidateContext -CoreManifestPath $CoreManifest -CoreManifestSidecarPath $CoreManifestSidecar -WitnessManifestPath $WitnessManifest -WitnessManifestSidecarPath $WitnessManifestSidecar
    $outputFull = [System.IO.Path]::GetFullPath($Output)
    if (-not (Test-Path -LiteralPath $outputFull)) { New-Item -ItemType Directory -Path $outputFull -Force | Out-Null }

    $verifierProof = Copy-BoundFile -SourcePath $VerifierSource -DestinationPath (Join-Path $outputFull "beta-qa-archive-kit-verify.ps1") -Label "Archive kit verifier"
    $helperProof = Copy-BoundFile -SourcePath $HelperSource -DestinationPath (Join-Path $outputFull "beta-qa-archive.ps1") -Label "Evidence archive helper"
    $guideProof = Copy-BoundFile -SourcePath $GuideSource -DestinationPath (Join-Path $outputFull "BETA-QA-EVIDENCE-ARCHIVE.md") -Label "Evidence archive guide"

    $manifest = [ordered]@{
        schemaVersion = 1
        kind = "DragonDiskForgeBetaQaArchiveKit"
        product = "Dragon DiskForge"
        version = $context.version
        architecture = $context.architecture
        sourceCommit = $context.sourceCommit
        workflowRunId = $context.workflowRunId
        packageFile = $context.packageFile
        packageSha256 = $context.packageSha256
        qaKitManifestFile = $context.coreProof.fileName
        qaKitManifestSha256 = $context.coreProof.sha256
        witnessKitManifestFile = $context.witnessProof.fileName
        witnessKitManifestSha256 = $context.witnessProof.sha256
        verifierFile = $verifierProof.fileName
        verifierSha256 = $verifierProof.sha256
        archiveHelperFile = $helperProof.fileName
        archiveHelperSha256 = $helperProof.sha256
        archiveGuideFile = $guideProof.fileName
        archiveGuideSha256 = $guideProof.sha256
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        humanGateClaimed = $false
        publicRelease = $false
    }

    $manifestPath = [System.IO.Path]::GetFullPath($ManifestOutput)
    Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
    $manifestProof = Write-Sha256Sidecar -Path $manifestPath
    Invoke-StandaloneVerify -VerifierFile $verifierProof.path -ManifestFile $manifestProof.path

    Write-Host "Built and verified hash-bound portable beta QA evidence-archive kit."
    Write-Host "Core QA kit SHA-256: $($context.coreProof.sha256)"
    Write-Host "Desktop witness kit SHA-256: $($context.witnessProof.sha256)"
    Write-Host "Evidence archive helper SHA-256: $($helperProof.sha256)"
    Write-Host "Evidence archive guide SHA-256: $($guideProof.sha256)"
    Write-Host "Human gate claimed: false"
}

function Invoke-VerifyArchiveKit {
    param(
        [Parameter(Mandatory = $true)][string]$VerifierFile,
        [Parameter(Mandatory = $true)][string]$ManifestFile
    )
    Invoke-StandaloneVerify -VerifierFile (Resolve-Path -LiteralPath $VerifierFile).Path -ManifestFile (Resolve-Path -LiteralPath $ManifestFile).Path
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-archive-kit-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $output = Join-Path $tempRoot "output"
        New-Item -ItemType Directory -Path $output -Force | Out-Null

        $corePath = Join-Path $output "beta-qa-kit.json"
        $core = [ordered]@{
            schemaVersion = 2
            kind = "DragonDiskForgeBetaQaKit"
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            sourceCommit = ("a" * 40)
            workflowRunId = "123456"
            packageFile = "DragonDiskForge-win-x64.zip"
            packageSha256 = ("b" * 64)
            publicRelease = $false
        }
        Write-Utf8NoBom -Path $corePath -Text (($core | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        $coreProof = Write-Sha256Sidecar -Path $corePath

        $witnessPath = Join-Path $output "beta-qa-witness-kit.json"
        $witness = [ordered]@{
            schemaVersion = 1
            kind = "DragonDiskForgeBetaQaWitnessKit"
            product = "Dragon DiskForge"
            version = "0.5.0-beta.1"
            architecture = "x64"
            sourceCommit = ("a" * 40)
            workflowRunId = "123456"
            packageFile = "DragonDiskForge-win-x64.zip"
            packageSha256 = ("b" * 64)
            qaKitManifestFile = "beta-qa-kit.json"
            qaKitManifestSha256 = $coreProof.sha256
            publicRelease = $false
            humanGateClaimed = $false
        }
        Write-Utf8NoBom -Path $witnessPath -Text (($witness | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        Write-Sha256Sidecar -Path $witnessPath | Out-Null

        $manifestPath = Join-Path $output "beta-qa-archive-kit.json"
        Invoke-BuildArchiveKit -Output $output -CoreManifest $corePath -CoreManifestSidecar "$corePath.sha256" -WitnessManifest $witnessPath -WitnessManifestSidecar "$witnessPath.sha256" -VerifierSource $VerifierPath -HelperSource $ArchiveHelperPath -GuideSource $ArchiveGuidePath -ManifestOutput $manifestPath

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -ne 1 -or [bool]$manifest.humanGateClaimed -or [bool]$manifest.publicRelease) {
            throw "Self-test failed: valid archive kit metadata is not fail-closed."
        }

        $helperCopy = Join-Path $output "beta-qa-archive.ps1"
        Add-Content -LiteralPath $helperCopy -Value "# tampered"
        $tamperRejected = $false
        try { Invoke-StandaloneVerify -VerifierFile (Join-Path $output "beta-qa-archive-kit-verify.ps1") -ManifestFile $manifestPath } catch { $tamperRejected = $true }
        if (-not $tamperRejected) { throw "Self-test failed: tampered evidence archive helper was accepted." }

        Copy-Item -LiteralPath $ArchiveHelperPath -Destination $helperCopy -Force
        $helperProof = Write-Sha256Sidecar -Path $helperCopy
        $manifest.archiveHelperSha256 = $helperProof.sha256
        $manifest.humanGateClaimed = $true
        Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        Write-Sha256Sidecar -Path $manifestPath | Out-Null
        $gateClaimRejected = $false
        try { Invoke-StandaloneVerify -VerifierFile (Join-Path $output "beta-qa-archive-kit-verify.ps1") -ManifestFile $manifestPath } catch { $gateClaimRejected = $true }
        if (-not $gateClaimRejected) { throw "Self-test failed: humanGateClaimed=true was accepted." }

        $manifest.humanGateClaimed = $false
        $manifest.packageSha256 = ("c" * 64)
        Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        Write-Sha256Sidecar -Path $manifestPath | Out-Null
        $identityRejected = $false
        try { Invoke-StandaloneVerify -VerifierFile (Join-Path $output "beta-qa-archive-kit-verify.ps1") -ManifestFile $manifestPath } catch { $identityRejected = $true }
        if (-not $identityRejected) { throw "Self-test failed: mismatched package identity was accepted." }

        Write-Host "Dragon DiskForge portable beta QA evidence-archive kit self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

switch ($Mode) {
    "build" {
        Invoke-BuildArchiveKit -Output $OutputDirectory -CoreManifest $QaKitManifestPath -CoreManifestSidecar $QaKitManifestChecksumFile -WitnessManifest $WitnessKitManifestPath -WitnessManifestSidecar $WitnessKitManifestChecksumFile -VerifierSource $VerifierPath -HelperSource $ArchiveHelperPath -GuideSource $ArchiveGuidePath -ManifestOutput $ArchiveKitPath
    }
    "verify" {
        Invoke-VerifyArchiveKit -VerifierFile $VerifierPath -ManifestFile $ArchiveKitPath
    }
    "self-test" {
        Invoke-SelfTest
    }
    default { throw "Unsupported mode '$Mode'." }
}