[CmdletBinding()]
param(
    [ValidateSet("build", "verify", "self-test")]
    [string]$Mode = "self-test",

    [string]$OutputDirectory = "artifacts/windows",
    [string]$QaKitManifestPath = "artifacts/windows/beta-qa-kit.json",
    [string]$QaKitManifestChecksumFile = "",
    [string]$VerifierPath = "scripts/beta-qa-witness-kit-verify.ps1",
    [string]$DesktopWitnessHelperPath = "scripts/beta-qa-desktop-witness.ps1",
    [string]$DesktopWitnessGuidePath = "docs/BETA-QA-DESKTOP-WITNESS.md",
    [string]$WitnessKitPath = "artifacts/windows/beta-qa-witness-kit.json"
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
    if (-not (Test-Path -LiteralPath $DestinationPath -PathType Leaf)) { throw "$Label was not copied into the witness companion." }
    return Write-Sha256Sidecar -Path $DestinationPath
}

function Get-CoreQaKitIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [string]$ManifestSidecarPath = ""
    )
    $proof = Assert-FileSidecar -FilePath $ManifestPath -SidecarPath $ManifestSidecarPath -Label "Core QA kit manifest"
    $manifest = Get-Content -LiteralPath $proof.path -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 2) { throw "Witness companion requires core beta QA kit schema 2." }
    if ([string]$manifest.kind -ne "DragonDiskForgeBetaQaKit") { throw "Unexpected core QA kit kind '$($manifest.kind)'." }
    if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected core QA kit product '$($manifest.product)'." }
    if ([string]$manifest.version -ne "0.5.0-beta.1") { throw "Unexpected core QA kit version '$($manifest.version)'." }
    if ([string]$manifest.architecture -ne "x64") { throw "Core QA kit architecture must be x64." }
    $sourceCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit)
    if ([string]$manifest.workflowRunId -notmatch '^[0-9]+$') { throw "Core QA kit workflowRunId is missing or invalid." }
    if ([bool]$manifest.publicRelease) { throw "Witness companion expects a retained non-public core QA kit." }
    if ([string]$manifest.packageFile -notmatch '^.+\.zip$') { throw "Core QA kit packageFile is missing or invalid." }
    if ([string]$manifest.packageSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Core QA kit packageSha256 is missing or invalid." }
    return [pscustomobject]@{
        proof = $proof
        version = [string]$manifest.version
        architecture = [string]$manifest.architecture
        sourceCommit = $sourceCommit
        workflowRunId = [string]$manifest.workflowRunId
        packageFile = [string]$manifest.packageFile
        packageSha256 = ([string]$manifest.packageSha256).ToLowerInvariant()
    }
}

function Invoke-StandaloneVerify {
    param(
        [Parameter(Mandatory = $true)][string]$VerifierFile,
        [Parameter(Mandatory = $true)][string]$ManifestFile
    )
    & $VerifierFile -ManifestPath $ManifestFile
}

function Invoke-BuildWitnessKit {
    param(
        [Parameter(Mandatory = $true)][string]$Output,
        [Parameter(Mandatory = $true)][string]$CoreManifest,
        [string]$CoreManifestSidecar,
        [Parameter(Mandatory = $true)][string]$VerifierSource,
        [Parameter(Mandatory = $true)][string]$HelperSource,
        [Parameter(Mandatory = $true)][string]$GuideSource,
        [Parameter(Mandatory = $true)][string]$ManifestOutput
    )
    $core = Get-CoreQaKitIdentity -ManifestPath $CoreManifest -ManifestSidecarPath $CoreManifestSidecar
    $outputFull = [System.IO.Path]::GetFullPath($Output)
    if (-not (Test-Path -LiteralPath $outputFull)) { New-Item -ItemType Directory -Path $outputFull -Force | Out-Null }

    $verifierProof = Copy-BoundFile -SourcePath $VerifierSource -DestinationPath (Join-Path $outputFull "beta-qa-witness-kit-verify.ps1") -Label "Witness companion verifier"
    $helperProof = Copy-BoundFile -SourcePath $HelperSource -DestinationPath (Join-Path $outputFull "beta-qa-desktop-witness.ps1") -Label "Desktop witness helper"
    $guideProof = Copy-BoundFile -SourcePath $GuideSource -DestinationPath (Join-Path $outputFull "BETA-QA-DESKTOP-WITNESS.md") -Label "Desktop witness guide"

    $manifest = [ordered]@{
        schemaVersion = 1
        kind = "DragonDiskForgeBetaQaWitnessKit"
        product = "Dragon DiskForge"
        version = $core.version
        architecture = $core.architecture
        sourceCommit = $core.sourceCommit
        workflowRunId = $core.workflowRunId
        packageFile = $core.packageFile
        packageSha256 = $core.packageSha256
        qaKitManifestFile = $core.proof.fileName
        qaKitManifestSha256 = $core.proof.sha256
        verifierFile = $verifierProof.fileName
        verifierSha256 = $verifierProof.sha256
        desktopWitnessHelperFile = $helperProof.fileName
        desktopWitnessHelperSha256 = $helperProof.sha256
        desktopWitnessGuideFile = $guideProof.fileName
        desktopWitnessGuideSha256 = $guideProof.sha256
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        humanGateClaimed = $false
        publicRelease = $false
    }

    $manifestPath = [System.IO.Path]::GetFullPath($ManifestOutput)
    Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
    $manifestProof = Write-Sha256Sidecar -Path $manifestPath
    Invoke-StandaloneVerify -VerifierFile $verifierProof.path -ManifestFile $manifestProof.path

    Write-Host "Built and verified hash-bound beta QA desktop-witness companion."
    Write-Host "Core QA kit SHA-256: $($core.proof.sha256)"
    Write-Host "Desktop witness helper SHA-256: $($helperProof.sha256)"
    Write-Host "Desktop witness guide SHA-256: $($guideProof.sha256)"
    Write-Host "Human gate claimed: false"
}

function Invoke-VerifyWitnessKit {
    param(
        [Parameter(Mandatory = $true)][string]$VerifierFile,
        [Parameter(Mandatory = $true)][string]$ManifestFile
    )
    Invoke-StandaloneVerify -VerifierFile (Resolve-Path -LiteralPath $VerifierFile).Path -ManifestFile (Resolve-Path -LiteralPath $ManifestFile).Path
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-witness-kit-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $corePath = Join-Path $tempRoot "beta-qa-kit.json"
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
        Write-Sha256Sidecar -Path $corePath | Out-Null

        $output = Join-Path $tempRoot "output"
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        Copy-Item -LiteralPath $corePath -Destination (Join-Path $output "beta-qa-kit.json") -Force
        Copy-Item -LiteralPath "$corePath.sha256" -Destination (Join-Path $output "beta-qa-kit.json.sha256") -Force
        $manifestPath = Join-Path $output "beta-qa-witness-kit.json"

        Invoke-BuildWitnessKit -Output $output -CoreManifest (Join-Path $output "beta-qa-kit.json") -CoreManifestSidecar (Join-Path $output "beta-qa-kit.json.sha256") -VerifierSource $VerifierPath -HelperSource $DesktopWitnessHelperPath -GuideSource $DesktopWitnessGuidePath -ManifestOutput $manifestPath

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -ne 1 -or [bool]$manifest.humanGateClaimed -or [bool]$manifest.publicRelease) {
            throw "Self-test failed: valid witness companion metadata is not fail-closed."
        }

        $helperCopy = Join-Path $output "beta-qa-desktop-witness.ps1"
        Add-Content -LiteralPath $helperCopy -Value "# tampered"
        $tamperRejected = $false
        try { Invoke-StandaloneVerify -VerifierFile (Join-Path $output "beta-qa-witness-kit-verify.ps1") -ManifestFile $manifestPath } catch { $tamperRejected = $true }
        if (-not $tamperRejected) { throw "Self-test failed: tampered desktop witness helper was accepted." }

        Copy-Item -LiteralPath $DesktopWitnessHelperPath -Destination $helperCopy -Force
        Write-Sha256Sidecar -Path $helperCopy | Out-Null
        $helperHash = (Get-FileHash -LiteralPath $helperCopy -Algorithm SHA256).Hash.ToLowerInvariant()
        $manifest.desktopWitnessHelperSha256 = $helperHash
        $manifest.humanGateClaimed = $true
        Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)
        Write-Sha256Sidecar -Path $manifestPath | Out-Null
        $gateClaimRejected = $false
        try { Invoke-StandaloneVerify -VerifierFile (Join-Path $output "beta-qa-witness-kit-verify.ps1") -ManifestFile $manifestPath } catch { $gateClaimRejected = $true }
        if (-not $gateClaimRejected) { throw "Self-test failed: humanGateClaimed=true was accepted." }

        Write-Host "Dragon DiskForge beta QA witness companion self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

switch ($Mode) {
    "build" {
        Invoke-BuildWitnessKit -Output $OutputDirectory -CoreManifest $QaKitManifestPath -CoreManifestSidecar $QaKitManifestChecksumFile -VerifierSource $VerifierPath -HelperSource $DesktopWitnessHelperPath -GuideSource $DesktopWitnessGuidePath -ManifestOutput $WitnessKitPath
    }
    "verify" {
        Invoke-VerifyWitnessKit -VerifierFile $VerifierPath -ManifestFile $WitnessKitPath
    }
    "self-test" {
        Invoke-SelfTest
    }
    default { throw "Unsupported mode '$Mode'." }
}