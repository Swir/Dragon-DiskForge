[CmdletBinding()]
param(
    [ValidateSet("archive", "verify", "self-test")]
    [string]$Mode = "archive",

    [string]$WorkspacePath = "",
    [string]$ArchivePath = "",
    [string]$ArchiveRoot = "artifacts/manual-qa/evidence-archive"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:ArchiveSchemaVersion = 1
$Script:SessionSchemaVersion = 1
$Script:EvidenceSchemaVersion = 3
$Script:SessionFileName = "beta-qa-session.json"
$Script:EvidenceFileName = "beta-manual-qa.json"
$Script:EvidenceGateName = "Dragon DiskForge interactive beta QA"

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

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Test-PathInside {
    param(
        [Parameter(Mandatory = $true)][string]$Child,
        [Parameter(Mandatory = $true)][string]$Parent
    )

    $childFull = (Get-FullPath -Path $Child).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $parentFull = (Get-FullPath -Path $Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ([string]::Equals($childFull, $parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $prefix = $parentFull + [System.IO.Path]::DirectorySeparatorChar
    return $childFull.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-PathInside {
    param(
        [Parameter(Mandatory = $true)][string]$Child,
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-PathInside -Child $Child -Parent $Parent)) {
        throw "$Label must resolve inside the QA workspace."
    }
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Sha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Label is not a valid SHA-256 value."
    }
}

function Assert-Sidecar {
    param(
        [Parameter(Mandatory = $true)][string]$PayloadPath,
        [Parameter(Mandatory = $true)][string]$SidecarPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $payload = (Resolve-Path -LiteralPath $PayloadPath).Path
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path
    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "$Label SHA-256 sidecar has an invalid format."
    }

    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    if ($declaredName -ne [System.IO.Path]::GetFileName($payload)) {
        throw "$Label SHA-256 sidecar targets '$declaredName' instead of '$([System.IO.Path]::GetFileName($payload))'."
    }

    $actualHash = Get-FileSha256 -Path $payload
    if ($actualHash -ne $declaredHash) {
        throw "$Label SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'."
    }

    return $actualHash
}

function Load-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    }
    catch {
        throw "$Label is not valid JSON: $($_.Exception.Message)"
    }
}

function Resolve-WorkspaceSnapshot {
    param([Parameter(Mandatory = $true)][string]$Path)

    $workspace = (Resolve-Path -LiteralPath $Path).Path
    if (-not (Test-Path -LiteralPath $workspace -PathType Container)) {
        throw "QA workspace is not a directory: $workspace"
    }

    $sessionPath = Join-Path $workspace $Script:SessionFileName
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) {
        throw "QA session metadata is missing: $sessionPath"
    }
    $session = Load-JsonFile -Path $sessionPath -Label "QA session metadata"
    if ([int]$session.schemaVersion -ne $Script:SessionSchemaVersion) {
        throw "Unsupported QA session schema '$($session.schemaVersion)'."
    }
    if ([bool]$session.manualGatePassed) {
        throw "QA session metadata must not claim that the manual release gate passed. Only package-bound human evidence can prove that gate."
    }

    $evidencePath = Get-FullPath -Path ([string]$session.evidencePath)
    $extractRoot = Get-FullPath -Path ([string]$session.extractRoot)
    Assert-PathInside -Child $evidencePath -Parent $workspace -Label "Manual-QA evidence"
    Assert-PathInside -Child $extractRoot -Parent $workspace -Label "Extracted candidate"

    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
        throw "Manual-QA evidence is missing: $evidencePath"
    }
    $evidenceSidecar = "$evidencePath.sha256"
    if (-not (Test-Path -LiteralPath $evidenceSidecar -PathType Leaf)) {
        throw "Manual-QA evidence sidecar is missing: $evidenceSidecar"
    }
    $evidenceHash = Assert-Sidecar -PayloadPath $evidencePath -SidecarPath $evidenceSidecar -Label "Manual-QA evidence"
    $evidence = Load-JsonFile -Path $evidencePath -Label "Manual-QA evidence"
    if ([int]$evidence.schemaVersion -ne $Script:EvidenceSchemaVersion) {
        throw "Unsupported manual-QA evidence schema '$($evidence.schemaVersion)'."
    }
    if ([string]$evidence.gate -ne $Script:EvidenceGateName) {
        throw "Unexpected manual-QA evidence gate '$($evidence.gate)'."
    }

    $packagePath = (Resolve-Path -LiteralPath ([string]$session.package.path)).Path
    $packageSidecar = (Resolve-Path -LiteralPath ([string]$session.package.checksumFile)).Path
    $packageHash = Assert-Sidecar -PayloadPath $packagePath -SidecarPath $packageSidecar -Label "Candidate package"
    Assert-Sha256 -Value ([string]$session.package.sha256) -Label "Session package SHA-256"
    if ($packageHash -ne ([string]$session.package.sha256).ToLowerInvariant()) {
        throw "Candidate package SHA-256 differs from QA session metadata."
    }

    if ([string]$evidence.targetVersion -ne [string]$session.package.version -or
        [string]$evidence.package.version -ne [string]$session.package.version -or
        [string]$evidence.package.sha256 -ne $packageHash) {
        throw "Manual-QA evidence is bound to a different candidate package/version than the QA session."
    }
    if ([string]$evidence.package.entryPointSha256 -ne [string]$session.package.entryPointSha256 -or
        [string]$evidence.package.betaManualQaEntryPointSha256 -ne [string]$session.package.qaToolSha256 -or
        [string]$evidence.createdQaToolSha256 -ne [string]$session.package.qaToolSha256) {
        throw "Manual-QA evidence is bound to different candidate entry-point or QA-tool hashes than the QA session."
    }

    $manifestPath = Join-Path $extractRoot "package-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Extracted package manifest is missing: $manifestPath"
    }
    $manifest = Load-JsonFile -Path $manifestPath -Label "Package manifest"
    if ([string]$manifest.version -ne [string]$session.package.version -or [string]$manifest.architecture -ne [string]$session.package.architecture) {
        throw "Extracted package manifest version/architecture differs from QA session metadata."
    }

    $entryRelative = ([string]$manifest.entryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $entryPath = Join-Path $extractRoot $entryRelative
    Assert-PathInside -Child $entryPath -Parent $extractRoot -Label "Desktop entry point"
    if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) {
        throw "Extracted desktop entry point is missing: $entryPath"
    }
    $entryHash = Get-FileSha256 -Path $entryPath
    if ($entryHash -ne ([string]$session.package.entryPointSha256).ToLowerInvariant() -or
        $entryHash -ne ([string]$manifest.entryPointSha256).ToLowerInvariant()) {
        throw "Extracted desktop entry-point SHA-256 differs from session/manifest identity."
    }

    $qaRelative = ([string]$manifest.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $qaToolPath = Join-Path $extractRoot $qaRelative
    Assert-PathInside -Child $qaToolPath -Parent $extractRoot -Label "Packaged QA tool"
    if (-not (Test-Path -LiteralPath $qaToolPath -PathType Leaf)) {
        throw "Extracted packaged QA tool is missing: $qaToolPath"
    }
    $qaHash = Get-FileSha256 -Path $qaToolPath
    if ($qaHash -ne ([string]$session.package.qaToolSha256).ToLowerInvariant() -or
        $qaHash -ne ([string]$manifest.betaManualQaEntryPointSha256).ToLowerInvariant()) {
        throw "Extracted packaged QA-tool SHA-256 differs from session/manifest identity."
    }

    return [pscustomobject]@{
        workspace = $workspace
        sessionPath = $sessionPath
        session = $session
        evidencePath = $evidencePath
        evidenceSidecar = $evidenceSidecar
        evidenceHash = $evidenceHash
        evidence = $evidence
        packagePath = $packagePath
        packageSidecar = $packageSidecar
        packageHash = $packageHash
        manifestPath = $manifestPath
        manifest = $manifest
        manifestHash = (Get-FileSha256 -Path $manifestPath)
        entryHash = $entryHash
        qaHash = $qaHash
    }
}

function Save-JsonAtomic {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullPath = Get-FullPath -Path $Path
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $tempPath = "$fullPath.tmp.$([guid]::NewGuid().ToString('N'))"
    try {
        $json = $Value | ConvertTo-Json -Depth 12
        Write-Utf8NoBom -Path $tempPath -Text ($json + [Environment]::NewLine)
        Move-Item -LiteralPath $tempPath -Destination $fullPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) {
            Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
        }
    }
    return $fullPath
}

function Invoke-Archive {
    if ([string]::IsNullOrWhiteSpace($WorkspacePath)) {
        throw "-WorkspacePath is required for -Mode archive."
    }

    $snapshot = Resolve-WorkspaceSnapshot -Path $WorkspacePath
    $root = Get-FullPath -Path $ArchiveRoot
    if (Test-PathInside -Child $root -Parent $snapshot.workspace) {
        throw "ArchiveRoot must be outside the disposable QA workspace so cleanup cannot delete the preserved evidence."
    }
    if (Test-PathInside -Child $snapshot.workspace -Parent $root) {
        # The default archive root may be a sibling/parent tree of the workspace. Reject only if the workspace itself would be the archive root.
        if ([string]::Equals($root.TrimEnd('\','/'), $snapshot.workspace.TrimEnd('\','/'), [StringComparison]::OrdinalIgnoreCase)) {
            throw "ArchiveRoot cannot be the QA workspace itself."
        }
    }

    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $versionSafe = ([string]$snapshot.session.package.version) -replace '[^A-Za-z0-9._-]', '_'
    $archiveName = "{0}-{1}-{2}" -f $versionSafe, $snapshot.packageHash.Substring(0, 12), $snapshot.evidenceHash.Substring(0, 12)
    $destination = Join-Path $root $archiveName
    if (Test-Path -LiteralPath $destination) {
        throw "Evidence archive already exists: $destination"
    }

    $tempDestination = Join-Path $root (".tmp-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempDestination -Force | Out-Null
    try {
        $evidenceCopy = Join-Path $tempDestination $Script:EvidenceFileName
        $evidenceSidecarCopy = "$evidenceCopy.sha256"
        $manifestCopy = Join-Path $tempDestination "package-manifest.json"
        Copy-Item -LiteralPath $snapshot.evidencePath -Destination $evidenceCopy
        Copy-Item -LiteralPath $snapshot.evidenceSidecar -Destination $evidenceSidecarCopy
        Copy-Item -LiteralPath $snapshot.manifestPath -Destination $manifestCopy

        $provenance = [ordered]@{
            schemaVersion = 1
            createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
            source = "beta-qa-session"
            sessionSchemaVersion = [int]$snapshot.session.schemaVersion
            package = [ordered]@{
                version = [string]$snapshot.session.package.version
                architecture = [string]$snapshot.session.package.architecture
                sha256 = $snapshot.packageHash
                entryPointSha256 = $snapshot.entryHash
                betaManualQaEntryPointSha256 = $snapshot.qaHash
            }
            environment = $snapshot.session.environment
            launchProbeSeconds = [int]$snapshot.session.launchProbeSeconds
            launchProbePassed = [bool]$snapshot.session.launchProbePassed
            releaseGateClaimed = $false
            note = "Integrity-preserving snapshot only. Human QA completion must be verified separately against the exact candidate package."
        }
        $provenancePath = Save-JsonAtomic -Value $provenance -Path (Join-Path $tempDestination "session-provenance.json")

        $archiveManifest = [ordered]@{
            schemaVersion = $Script:ArchiveSchemaVersion
            product = "Dragon DiskForge"
            purpose = "beta-manual-qa-evidence-snapshot"
            createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
            targetVersion = [string]$snapshot.session.package.version
            releaseGateClaimed = $false
            package = [ordered]@{
                sha256 = $snapshot.packageHash
                architecture = [string]$snapshot.session.package.architecture
                entryPointSha256 = $snapshot.entryHash
                betaManualQaEntryPointSha256 = $snapshot.qaHash
                packageManifestSha256 = $snapshot.manifestHash
            }
            evidence = [ordered]@{
                schemaVersion = [int]$snapshot.evidence.schemaVersion
                sha256 = $snapshot.evidenceHash
                gate = [string]$snapshot.evidence.gate
            }
            files = @(
                [ordered]@{ name = $Script:EvidenceFileName; sha256 = (Get-FileSha256 -Path $evidenceCopy) },
                [ordered]@{ name = "$($Script:EvidenceFileName).sha256"; sha256 = (Get-FileSha256 -Path $evidenceSidecarCopy) },
                [ordered]@{ name = "package-manifest.json"; sha256 = (Get-FileSha256 -Path $manifestCopy) },
                [ordered]@{ name = "session-provenance.json"; sha256 = (Get-FileSha256 -Path $provenancePath) }
            )
        }
        Save-JsonAtomic -Value $archiveManifest -Path (Join-Path $tempDestination "archive-manifest.json") | Out-Null

        Move-Item -LiteralPath $tempDestination -Destination $destination
        Invoke-Verify -Path $destination | Out-Null
        Write-Host "Preserved beta manual-QA evidence snapshot: $destination"
        Write-Host "This archive proves integrity/provenance only; it does not mark any human release gate as passed."
        return $destination
    }
    finally {
        if (Test-Path -LiteralPath $tempDestination) {
            Remove-Item -LiteralPath $tempDestination -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-Verify {
    param([string]$Path = "")

    $target = $Path
    if ([string]::IsNullOrWhiteSpace($target)) {
        $target = $ArchivePath
    }
    if ([string]::IsNullOrWhiteSpace($target)) {
        throw "-ArchivePath is required for -Mode verify."
    }

    $root = (Resolve-Path -LiteralPath $target).Path
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Evidence archive is not a directory: $root"
    }
    $manifestPath = Join-Path $root "archive-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Evidence archive manifest is missing: $manifestPath"
    }
    $manifest = Load-JsonFile -Path $manifestPath -Label "Evidence archive manifest"
    if ([int]$manifest.schemaVersion -ne $Script:ArchiveSchemaVersion -or [string]$manifest.product -ne "Dragon DiskForge" -or [string]$manifest.purpose -ne "beta-manual-qa-evidence-snapshot") {
        throw "Evidence archive manifest identity/schema is invalid."
    }
    if ([bool]$manifest.releaseGateClaimed) {
        throw "Evidence archive must never claim that a release gate passed."
    }

    $fileEntries = @($manifest.files)
    if ($fileEntries.Count -ne 4) {
        throw "Evidence archive manifest must bind exactly four payload files."
    }
    foreach ($entry in $fileEntries) {
        $name = [string]$entry.name
        if ([string]::IsNullOrWhiteSpace($name) -or $name -ne [System.IO.Path]::GetFileName($name)) {
            throw "Evidence archive contains an unsafe payload filename '$name'."
        }
        Assert-Sha256 -Value ([string]$entry.sha256) -Label "Archive payload '$name' SHA-256"
        $payload = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $payload -PathType Leaf)) {
            throw "Evidence archive payload is missing: $name"
        }
        $actual = Get-FileSha256 -Path $payload
        if ($actual -ne ([string]$entry.sha256).ToLowerInvariant()) {
            throw "Evidence archive payload '$name' SHA-256 mismatch."
        }
    }

    $evidencePath = Join-Path $root $Script:EvidenceFileName
    $evidenceSidecar = "$evidencePath.sha256"
    $evidenceHash = Assert-Sidecar -PayloadPath $evidencePath -SidecarPath $evidenceSidecar -Label "Archived manual-QA evidence"
    if ($evidenceHash -ne ([string]$manifest.evidence.sha256).ToLowerInvariant()) {
        throw "Archived evidence hash differs from archive manifest."
    }
    $evidence = Load-JsonFile -Path $evidencePath -Label "Archived manual-QA evidence"
    if ([int]$evidence.schemaVersion -ne $Script:EvidenceSchemaVersion -or [string]$evidence.gate -ne $Script:EvidenceGateName) {
        throw "Archived manual-QA evidence identity/schema is invalid."
    }
    if ([string]$evidence.targetVersion -ne [string]$manifest.targetVersion -or
        [string]$evidence.package.sha256 -ne [string]$manifest.package.sha256 -or
        [string]$evidence.package.entryPointSha256 -ne [string]$manifest.package.entryPointSha256 -or
        [string]$evidence.package.betaManualQaEntryPointSha256 -ne [string]$manifest.package.betaManualQaEntryPointSha256) {
        throw "Archived manual-QA evidence is not bound to the package identity recorded in the archive manifest."
    }

    $packageManifestPath = Join-Path $root "package-manifest.json"
    if ((Get-FileSha256 -Path $packageManifestPath) -ne ([string]$manifest.package.packageManifestSha256).ToLowerInvariant()) {
        throw "Archived package-manifest SHA-256 differs from archive manifest."
    }
    $packageManifest = Load-JsonFile -Path $packageManifestPath -Label "Archived package manifest"
    if ([string]$packageManifest.version -ne [string]$manifest.targetVersion -or
        [string]$packageManifest.architecture -ne [string]$manifest.package.architecture -or
        [string]$packageManifest.entryPointSha256 -ne [string]$manifest.package.entryPointSha256 -or
        [string]$packageManifest.betaManualQaEntryPointSha256 -ne [string]$manifest.package.betaManualQaEntryPointSha256) {
        throw "Archived package manifest disagrees with archive package identity."
    }

    $provenance = Load-JsonFile -Path (Join-Path $root "session-provenance.json") -Label "Archived session provenance"
    if ([bool]$provenance.releaseGateClaimed) {
        throw "Archived session provenance must never claim release-gate completion."
    }
    if ([string]$provenance.package.sha256 -ne [string]$manifest.package.sha256 -or [string]$provenance.package.version -ne [string]$manifest.targetVersion) {
        throw "Archived session provenance disagrees with archive package identity."
    }

    Write-Host "Beta manual-QA evidence archive verified: $root"
    Write-Host "Integrity/provenance verified; human QA completion is intentionally not inferred."
    return $true
}

function New-SelfTestWorkspace {
    param([Parameter(Mandatory = $true)][string]$Root)

    $workspace = Join-Path $Root "session-workspace"
    $extractRoot = Join-Path $workspace "package"
    New-Item -ItemType Directory -Path (Join-Path $extractRoot "tools") -Force | Out-Null

    $entryPoint = Join-Path $extractRoot "DragonDiskForge.App.exe"
    $qaTool = Join-Path $extractRoot "tools\beta-manual-qa.ps1"
    Write-Utf8NoBom -Path $entryPoint -Text "self-test-app"
    Write-Utf8NoBom -Path $qaTool -Text "Write-Host 'self-test-qa'"
    $entryHash = Get-FileSha256 -Path $entryPoint
    $qaHash = Get-FileSha256 -Path $qaTool

    $packageManifest = [ordered]@{
        schemaVersion = 5
        product = "Dragon DiskForge"
        version = "0.5.0-beta.1"
        architecture = "x64"
        entryPoint = "DragonDiskForge.App.exe"
        entryPointSha256 = $entryHash
        betaManualQaEntryPoint = "tools/beta-manual-qa.ps1"
        betaManualQaEntryPointSha256 = $qaHash
    }
    Write-Utf8NoBom -Path (Join-Path $extractRoot "package-manifest.json") -Text (($packageManifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)

    $packagePath = Join-Path $Root "DragonDiskForge-win-x64.zip"
    Compress-Archive -Path (Join-Path $extractRoot "*") -DestinationPath $packagePath -Force
    $packageHash = Get-FileSha256 -Path $packagePath
    $packageSidecar = "$packagePath.sha256"
    Write-Utf8NoBom -Path $packageSidecar -Text ("{0}  {1}{2}" -f $packageHash, [System.IO.Path]::GetFileName($packagePath), [Environment]::NewLine)

    $evidence = [ordered]@{
        schemaVersion = $Script:EvidenceSchemaVersion
        gate = $Script:EvidenceGateName
        targetVersion = "0.5.0-beta.1"
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        updatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        package = [ordered]@{
            fileName = [System.IO.Path]::GetFileName($packagePath)
            sha256 = $packageHash
            version = "0.5.0-beta.1"
            architecture = "x64"
            entryPointSha256 = $entryHash
            betaManualQaEntryPointSha256 = $qaHash
        }
        createdQaToolSha256 = $qaHash
        createdEnvironment = [ordered]@{ osBuild = "self-test"; processArchitecture = "X64"; userInteractive = $true; processElevated = $false; sessionId = 1; uacEnabled = $true }
        checks = @()
    }
    $evidencePath = Join-Path $workspace $Script:EvidenceFileName
    Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
    $evidenceHash = Get-FileSha256 -Path $evidencePath
    Write-Utf8NoBom -Path "$evidencePath.sha256" -Text ("{0}  {1}{2}" -f $evidenceHash, [System.IO.Path]::GetFileName($evidencePath), [Environment]::NewLine)

    $session = [ordered]@{
        schemaVersion = $Script:SessionSchemaVersion
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        package = [ordered]@{
            path = (Get-FullPath -Path $packagePath)
            checksumFile = (Get-FullPath -Path $packageSidecar)
            sha256 = $packageHash
            version = "0.5.0-beta.1"
            architecture = "x64"
            entryPointSha256 = $entryHash
            qaToolSha256 = $qaHash
        }
        environment = [ordered]@{ osBuild = "self-test"; processArchitecture = "X64"; userInteractive = $true; processElevated = $false; sessionId = 1; uacEnabled = $true }
        workspace = (Get-FullPath -Path $workspace)
        extractRoot = (Get-FullPath -Path $extractRoot)
        evidencePath = (Get-FullPath -Path $evidencePath)
        dropTarget = (Get-FullPath -Path (Join-Path $workspace "drop"))
        appProcessId = 0
        appPath = (Get-FullPath -Path $entryPoint)
        launchProbeSeconds = 1
        launchProbePassed = $true
        manualGatePassed = $false
        note = "self-test"
    }
    New-Item -ItemType Directory -Path $session.dropTarget -Force | Out-Null
    Write-Utf8NoBom -Path (Join-Path $workspace $Script:SessionFileName) -Text (($session | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
    return $workspace
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-archive-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $workspace = New-SelfTestWorkspace -Root $root
        $archiveRootPath = Join-Path $root "archives"
        $archive = Invoke-Archive -WorkspacePath $workspace -ArchiveRoot $archiveRootPath
        Invoke-Verify -Path $archive | Out-Null

        $tamperedEvidence = Join-Path $archive $Script:EvidenceFileName
        Add-Content -LiteralPath $tamperedEvidence -Value "tamper"
        $tamperRejected = $false
        try { Invoke-Verify -Path $archive | Out-Null } catch { $tamperRejected = $true }
        if (-not $tamperRejected) {
            throw "Self-test failed: tampered archived evidence did not fail closed."
        }

        $overlapRejected = $false
        try { Invoke-Archive -WorkspacePath $workspace -ArchiveRoot (Join-Path $workspace "archive") | Out-Null } catch { $overlapRejected = $true }
        if (-not $overlapRejected) {
            throw "Self-test failed: archive root inside disposable workspace did not fail closed."
        }

        Write-Host "Dragon DiskForge beta QA evidence archive self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $root) {
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    "archive" { Invoke-Archive; break }
    "verify" { Invoke-Verify; break }
    "self-test" { Invoke-SelfTest; break }
    default { throw "Unsupported mode '$Mode'." }
}
