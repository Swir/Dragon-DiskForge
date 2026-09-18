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
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Text)
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
    param([Parameter(Mandatory = $true)][string]$Child, [Parameter(Mandatory = $true)][string]$Parent)
    $childFull = (Get-FullPath $Child).TrimEnd([char]92, [char]47)
    $parentFull = (Get-FullPath $Parent).TrimEnd([char]92, [char]47)
    if ([string]::Equals($childFull, $parentFull, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $childFull.StartsWith(($parentFull + [System.IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)
}

function Assert-Inside {
    param([string]$Child, [string]$Parent, [string]$Label)
    if (-not (Test-PathInside -Child $Child -Parent $Parent)) { throw "$Label must resolve inside the QA workspace." }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Sha256Value {
    param([string]$Value, [string]$Label)
    if ($Value -notmatch '^[0-9a-fA-F]{64}$') { throw "$Label is not a valid SHA-256 value." }
}

function Assert-Sidecar {
    param([string]$PayloadPath, [string]$SidecarPath, [string]$Label)
    $payload = (Resolve-Path -LiteralPath $PayloadPath).Path
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path
    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') { throw "$Label SHA-256 sidecar has an invalid format." }
    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    if ($declaredName -ne [System.IO.Path]::GetFileName($payload)) { throw "$Label SHA-256 sidecar targets the wrong file." }
    $actualHash = Get-Sha256 $payload
    if ($actualHash -ne $declaredHash) { throw "$Label SHA-256 mismatch." }
    return $actualHash
}

function Load-Json {
    param([string]$Path, [string]$Label)
    try { return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json) }
    catch { throw "$Label is not valid JSON: $($_.Exception.Message)" }
}

function Save-JsonAtomic {
    param([Parameter(Mandatory = $true)]$Value, [Parameter(Mandatory = $true)][string]$Path)
    $fullPath = Get-FullPath $Path
    $directory = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $tempPath = "$fullPath.tmp.$([guid]::NewGuid().ToString('N'))"
    try {
        Write-Utf8NoBom -Path $tempPath -Text (($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
        Move-Item -LiteralPath $tempPath -Destination $fullPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
    }
    return $fullPath
}

function Resolve-WorkspaceSnapshot {
    param([Parameter(Mandatory = $true)][string]$Workspace)

    $workspaceFull = (Resolve-Path -LiteralPath $Workspace).Path
    if (-not (Test-Path -LiteralPath $workspaceFull -PathType Container)) { throw "QA workspace is not a directory: $workspaceFull" }

    $sessionPath = Join-Path $workspaceFull $Script:SessionFileName
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) { throw "QA session metadata is missing: $sessionPath" }
    $session = Load-Json -Path $sessionPath -Label "QA session metadata"
    if ([int]$session.schemaVersion -ne $Script:SessionSchemaVersion) { throw "Unsupported QA session schema '$($session.schemaVersion)'." }
    if ([bool]$session.manualGatePassed) { throw "Session metadata cannot authoritatively claim that a human release gate passed." }

    $evidencePath = Get-FullPath ([string]$session.evidencePath)
    $extractRoot = Get-FullPath ([string]$session.extractRoot)
    Assert-Inside -Child $evidencePath -Parent $workspaceFull -Label "Manual-QA evidence"
    Assert-Inside -Child $extractRoot -Parent $workspaceFull -Label "Extracted candidate"

    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { throw "Manual-QA evidence is missing: $evidencePath" }
    $evidenceSidecar = "$evidencePath.sha256"
    if (-not (Test-Path -LiteralPath $evidenceSidecar -PathType Leaf)) { throw "Manual-QA evidence sidecar is missing." }
    $evidenceHash = Assert-Sidecar -PayloadPath $evidencePath -SidecarPath $evidenceSidecar -Label "Manual-QA evidence"
    $evidence = Load-Json -Path $evidencePath -Label "Manual-QA evidence"
    if ([int]$evidence.schemaVersion -ne $Script:EvidenceSchemaVersion -or [string]$evidence.gate -ne $Script:EvidenceGateName) {
        throw "Manual-QA evidence identity/schema is invalid."
    }

    $packagePath = (Resolve-Path -LiteralPath ([string]$session.package.path)).Path
    $packageSidecar = (Resolve-Path -LiteralPath ([string]$session.package.checksumFile)).Path
    $packageHash = Assert-Sidecar -PayloadPath $packagePath -SidecarPath $packageSidecar -Label "Candidate package"
    Assert-Sha256Value -Value ([string]$session.package.sha256) -Label "Session package SHA-256"
    if ($packageHash -ne ([string]$session.package.sha256).ToLowerInvariant()) { throw "Candidate package differs from QA session metadata." }

    if ([string]$evidence.targetVersion -ne [string]$session.package.version -or
        [string]$evidence.package.version -ne [string]$session.package.version -or
        [string]$evidence.package.sha256 -ne $packageHash -or
        [string]$evidence.package.entryPointSha256 -ne [string]$session.package.entryPointSha256 -or
        [string]$evidence.package.betaManualQaEntryPointSha256 -ne [string]$session.package.qaToolSha256 -or
        [string]$evidence.createdQaToolSha256 -ne [string]$session.package.qaToolSha256) {
        throw "Manual-QA evidence is bound to a different candidate identity than the QA session."
    }

    $packageManifestPath = Join-Path $extractRoot "package-manifest.json"
    if (-not (Test-Path -LiteralPath $packageManifestPath -PathType Leaf)) { throw "Extracted package manifest is missing." }
    $packageManifest = Load-Json -Path $packageManifestPath -Label "Package manifest"
    if ([string]$packageManifest.version -ne [string]$session.package.version -or [string]$packageManifest.architecture -ne [string]$session.package.architecture) {
        throw "Extracted package manifest version/architecture differs from session metadata."
    }

    $entryPath = Join-Path $extractRoot (([string]$packageManifest.entryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $qaToolPath = Join-Path $extractRoot (([string]$packageManifest.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    Assert-Inside -Child $entryPath -Parent $extractRoot -Label "Desktop entry point"
    Assert-Inside -Child $qaToolPath -Parent $extractRoot -Label "Packaged QA tool"
    if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf) -or -not (Test-Path -LiteralPath $qaToolPath -PathType Leaf)) { throw "Extracted package entry points are incomplete." }

    $entryHash = Get-Sha256 $entryPath
    $qaHash = Get-Sha256 $qaToolPath
    if ($entryHash -ne ([string]$session.package.entryPointSha256).ToLowerInvariant() -or $entryHash -ne ([string]$packageManifest.entryPointSha256).ToLowerInvariant()) {
        throw "Extracted desktop entry-point SHA-256 differs from session/manifest identity."
    }
    if ($qaHash -ne ([string]$session.package.qaToolSha256).ToLowerInvariant() -or $qaHash -ne ([string]$packageManifest.betaManualQaEntryPointSha256).ToLowerInvariant()) {
        throw "Extracted QA-tool SHA-256 differs from session/manifest identity."
    }

    return [pscustomobject]@{
        workspace = $workspaceFull
        session = $session
        evidencePath = $evidencePath
        evidenceSidecar = $evidenceSidecar
        evidence = $evidence
        evidenceHash = $evidenceHash
        packageHash = $packageHash
        packageManifestPath = $packageManifestPath
        packageManifestHash = (Get-Sha256 $packageManifestPath)
        entryHash = $entryHash
        qaHash = $qaHash
    }
}

function Invoke-VerifyArchive {
    param([Parameter(Mandatory = $true)][string]$Path)

    $root = (Resolve-Path -LiteralPath $Path).Path
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Evidence archive is not a directory: $root" }
    $manifestPath = Join-Path $root "archive-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Evidence archive manifest is missing." }
    $manifest = Load-Json -Path $manifestPath -Label "Evidence archive manifest"
    if ([int]$manifest.schemaVersion -ne $Script:ArchiveSchemaVersion -or [string]$manifest.product -ne "Dragon DiskForge" -or [string]$manifest.purpose -ne "beta-manual-qa-evidence-snapshot") {
        throw "Evidence archive manifest identity/schema is invalid."
    }
    if ([bool]$manifest.releaseGateClaimed) { throw "An evidence archive cannot claim release-gate completion." }

    $files = @($manifest.files)
    if ($files.Count -ne 4) { throw "Evidence archive manifest must bind exactly four payload files." }
    foreach ($file in $files) {
        $name = [string]$file.name
        if ([string]::IsNullOrWhiteSpace($name) -or $name -ne [System.IO.Path]::GetFileName($name)) { throw "Unsafe archive payload filename '$name'." }
        Assert-Sha256Value -Value ([string]$file.sha256) -Label "Archive payload '$name' SHA-256"
        $payloadPath = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $payloadPath -PathType Leaf)) { throw "Archive payload is missing: $name" }
        if ((Get-Sha256 $payloadPath) -ne ([string]$file.sha256).ToLowerInvariant()) { throw "Archive payload '$name' SHA-256 mismatch." }
    }

    $evidencePath = Join-Path $root $Script:EvidenceFileName
    $evidenceHash = Assert-Sidecar -PayloadPath $evidencePath -SidecarPath "$evidencePath.sha256" -Label "Archived manual-QA evidence"
    if ($evidenceHash -ne ([string]$manifest.evidence.sha256).ToLowerInvariant()) { throw "Archived evidence hash differs from archive manifest." }
    $evidence = Load-Json -Path $evidencePath -Label "Archived manual-QA evidence"
    if ([int]$evidence.schemaVersion -ne $Script:EvidenceSchemaVersion -or [string]$evidence.gate -ne $Script:EvidenceGateName) { throw "Archived manual-QA evidence identity/schema is invalid." }
    if ([string]$evidence.targetVersion -ne [string]$manifest.targetVersion -or
        [string]$evidence.package.sha256 -ne [string]$manifest.package.sha256 -or
        [string]$evidence.package.entryPointSha256 -ne [string]$manifest.package.entryPointSha256 -or
        [string]$evidence.package.betaManualQaEntryPointSha256 -ne [string]$manifest.package.betaManualQaEntryPointSha256) {
        throw "Archived manual-QA evidence disagrees with archive package identity."
    }

    $packageManifestPath = Join-Path $root "package-manifest.json"
    if ((Get-Sha256 $packageManifestPath) -ne ([string]$manifest.package.packageManifestSha256).ToLowerInvariant()) { throw "Archived package-manifest SHA-256 differs from archive manifest." }
    $packageManifest = Load-Json -Path $packageManifestPath -Label "Archived package manifest"
    if ([string]$packageManifest.version -ne [string]$manifest.targetVersion -or
        [string]$packageManifest.architecture -ne [string]$manifest.package.architecture -or
        [string]$packageManifest.entryPointSha256 -ne [string]$manifest.package.entryPointSha256 -or
        [string]$packageManifest.betaManualQaEntryPointSha256 -ne [string]$manifest.package.betaManualQaEntryPointSha256) {
        throw "Archived package manifest disagrees with archive package identity."
    }

    $provenance = Load-Json -Path (Join-Path $root "session-provenance.json") -Label "Archived session provenance"
    if ([bool]$provenance.releaseGateClaimed) { throw "Session provenance cannot claim release-gate completion." }
    if ([string]$provenance.package.sha256 -ne [string]$manifest.package.sha256 -or [string]$provenance.package.version -ne [string]$manifest.targetVersion) {
        throw "Archived session provenance disagrees with archive package identity."
    }

    Write-Host "Beta manual-QA evidence archive verified: $root"
    Write-Host "Integrity/provenance verified; human QA completion is intentionally not inferred."
    return $true
}

function Invoke-ArchiveWorkspace {
    param([Parameter(Mandatory = $true)][string]$Workspace, [Parameter(Mandatory = $true)][string]$Root)

    $snapshot = Resolve-WorkspaceSnapshot -Workspace $Workspace
    $archiveRootFull = Get-FullPath $Root
    if (Test-PathInside -Child $archiveRootFull -Parent $snapshot.workspace) {
        throw "ArchiveRoot must be outside the disposable QA workspace so cleanup cannot delete preserved evidence."
    }

    New-Item -ItemType Directory -Path $archiveRootFull -Force | Out-Null
    $safeVersion = ([string]$snapshot.session.package.version) -replace '[^A-Za-z0-9._-]', '_'
    $archiveName = "{0}-{1}-{2}" -f $safeVersion, $snapshot.packageHash.Substring(0, 12), $snapshot.evidenceHash.Substring(0, 12)
    $destination = Join-Path $archiveRootFull $archiveName
    if (Test-Path -LiteralPath $destination) { throw "Evidence archive already exists: $destination" }

    $temp = Join-Path $archiveRootFull (".tmp-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try {
        $evidenceCopy = Join-Path $temp $Script:EvidenceFileName
        $evidenceSidecarCopy = "$evidenceCopy.sha256"
        $packageManifestCopy = Join-Path $temp "package-manifest.json"
        Copy-Item -LiteralPath $snapshot.evidencePath -Destination $evidenceCopy
        Copy-Item -LiteralPath $snapshot.evidenceSidecar -Destination $evidenceSidecarCopy
        Copy-Item -LiteralPath $snapshot.packageManifestPath -Destination $packageManifestCopy

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
        $provenancePath = Save-JsonAtomic -Value $provenance -Path (Join-Path $temp "session-provenance.json")

        $manifest = [ordered]@{
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
                packageManifestSha256 = $snapshot.packageManifestHash
            }
            evidence = [ordered]@{ schemaVersion = [int]$snapshot.evidence.schemaVersion; sha256 = $snapshot.evidenceHash; gate = [string]$snapshot.evidence.gate }
            files = @(
                [ordered]@{ name = $Script:EvidenceFileName; sha256 = (Get-Sha256 $evidenceCopy) },
                [ordered]@{ name = "$($Script:EvidenceFileName).sha256"; sha256 = (Get-Sha256 $evidenceSidecarCopy) },
                [ordered]@{ name = "package-manifest.json"; sha256 = (Get-Sha256 $packageManifestCopy) },
                [ordered]@{ name = "session-provenance.json"; sha256 = (Get-Sha256 $provenancePath) }
            )
        }
        Save-JsonAtomic -Value $manifest -Path (Join-Path $temp "archive-manifest.json") | Out-Null
        Move-Item -LiteralPath $temp -Destination $destination
        Invoke-VerifyArchive -Path $destination | Out-Null
        Write-Host "Preserved beta manual-QA evidence snapshot: $destination"
        Write-Host "This snapshot proves integrity/provenance only; it does not mark a human release gate as passed."
        return $destination
    }
    finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function New-SelfTestWorkspace {
    param([string]$Root)
    $workspace = Join-Path $Root "session-workspace"
    $extractRoot = Join-Path $workspace "package"
    New-Item -ItemType Directory -Path (Join-Path $extractRoot "tools") -Force | Out-Null

    $entry = Join-Path $extractRoot "DragonDiskForge.App.exe"
    $qaTool = Join-Path $extractRoot "tools\beta-manual-qa.ps1"
    Write-Utf8NoBom -Path $entry -Text "self-test-app"
    Write-Utf8NoBom -Path $qaTool -Text "Write-Host 'self-test-qa'"
    $entryHash = Get-Sha256 $entry
    $qaHash = Get-Sha256 $qaTool

    $packageManifest = [ordered]@{ schemaVersion = 5; product = "Dragon DiskForge"; version = "0.5.0-beta.1"; architecture = "x64"; entryPoint = "DragonDiskForge.App.exe"; entryPointSha256 = $entryHash; betaManualQaEntryPoint = "tools/beta-manual-qa.ps1"; betaManualQaEntryPointSha256 = $qaHash }
    Write-Utf8NoBom -Path (Join-Path $extractRoot "package-manifest.json") -Text (($packageManifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)

    $package = Join-Path $Root "DragonDiskForge-win-x64.zip"
    Compress-Archive -Path (Join-Path $extractRoot "*") -DestinationPath $package -Force
    $packageHash = Get-Sha256 $package
    $packageSidecar = "$package.sha256"
    Write-Utf8NoBom -Path $packageSidecar -Text ("{0}  {1}{2}" -f $packageHash, [System.IO.Path]::GetFileName($package), [Environment]::NewLine)

    $evidence = [ordered]@{
        schemaVersion = $Script:EvidenceSchemaVersion; gate = $Script:EvidenceGateName; targetVersion = "0.5.0-beta.1"; createdUtc = [DateTimeOffset]::UtcNow.ToString("O"); updatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        package = [ordered]@{ fileName = [System.IO.Path]::GetFileName($package); sha256 = $packageHash; version = "0.5.0-beta.1"; architecture = "x64"; entryPointSha256 = $entryHash; betaManualQaEntryPointSha256 = $qaHash }
        createdQaToolSha256 = $qaHash
        createdEnvironment = [ordered]@{ osBuild = "self-test"; processArchitecture = "X64"; userInteractive = $true; processElevated = $false; sessionId = 1; uacEnabled = $true }
        checks = @()
    }
    $evidencePath = Join-Path $workspace $Script:EvidenceFileName
    Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
    $evidenceHash = Get-Sha256 $evidencePath
    Write-Utf8NoBom -Path "$evidencePath.sha256" -Text ("{0}  {1}{2}" -f $evidenceHash, [System.IO.Path]::GetFileName($evidencePath), [Environment]::NewLine)

    $dropTarget = Join-Path $workspace "drop"
    New-Item -ItemType Directory -Path $dropTarget -Force | Out-Null
    $session = [ordered]@{
        schemaVersion = $Script:SessionSchemaVersion; createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        package = [ordered]@{ path = (Get-FullPath $package); checksumFile = (Get-FullPath $packageSidecar); sha256 = $packageHash; version = "0.5.0-beta.1"; architecture = "x64"; entryPointSha256 = $entryHash; qaToolSha256 = $qaHash }
        environment = [ordered]@{ osBuild = "self-test"; processArchitecture = "X64"; userInteractive = $true; processElevated = $false; sessionId = 1; uacEnabled = $true }
        workspace = (Get-FullPath $workspace); extractRoot = (Get-FullPath $extractRoot); evidencePath = (Get-FullPath $evidencePath); dropTarget = (Get-FullPath $dropTarget)
        appProcessId = 0; appPath = (Get-FullPath $entry); launchProbeSeconds = 1; launchProbePassed = $true; manualGatePassed = $false; note = "self-test"
    }
    Write-Utf8NoBom -Path (Join-Path $workspace $Script:SessionFileName) -Text (($session | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
    return $workspace
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-archive-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $workspace = New-SelfTestWorkspace -Root $root
        $archiveRootPath = Join-Path $root "archives"
        $archive = Invoke-ArchiveWorkspace -Workspace $workspace -Root $archiveRootPath
        Invoke-VerifyArchive -Path $archive | Out-Null

        Add-Content -LiteralPath (Join-Path $archive $Script:EvidenceFileName) -Value "tamper"
        $tamperRejected = $false
        try { Invoke-VerifyArchive -Path $archive | Out-Null } catch { $tamperRejected = $true }
        if (-not $tamperRejected) { throw "Self-test failed: tampered evidence did not fail closed." }

        $overlapRejected = $false
        try { Invoke-ArchiveWorkspace -Workspace $workspace -Root (Join-Path $workspace "archive") | Out-Null } catch { $overlapRejected = $true }
        if (-not $overlapRejected) { throw "Self-test failed: archive root inside disposable workspace did not fail closed." }

        Write-Host "Dragon DiskForge beta QA evidence archive self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

switch ($Mode) {
    "archive" {
        if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required for -Mode archive." }
        Invoke-ArchiveWorkspace -Workspace $WorkspacePath -Root $ArchiveRoot | Out-Null
        break
    }
    "verify" {
        if ([string]::IsNullOrWhiteSpace($ArchivePath)) { throw "-ArchivePath is required for -Mode verify." }
        Invoke-VerifyArchive -Path $ArchivePath | Out-Null
        break
    }
    "self-test" { Invoke-SelfTest; break }
    default { throw "Unsupported mode '$Mode'." }
}
