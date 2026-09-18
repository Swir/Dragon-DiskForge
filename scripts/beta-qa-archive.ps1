[CmdletBinding()]
param(
    [ValidateSet("archive", "verify", "self-test")]
    [string]$Mode = "archive",
    [string]$WorkspacePath = "",
    [string]$ArchivePath = "",
    [string]$ArchiveRoot = "artifacts/manual-qa/evidence-archive",
    [switch]$IncludeWitness
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:ArchiveSchemaVersion = 2
$Script:LegacyArchiveSchemaVersion = 1
$Script:SessionSchemaVersion = 1
$Script:EvidenceSchemaVersion = 3
$Script:WitnessSchemaVersion = 1
$Script:SessionFileName = "beta-qa-session.json"
$Script:EvidenceFileName = "beta-manual-qa.json"
$Script:WitnessFileName = "beta-qa-desktop-witness.json"
$Script:EvidenceGateName = "Dragon DiskForge interactive beta QA"

function Write-Utf8NoBom {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Text)
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
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

function Get-Sha256Text {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally { $sha.Dispose() }
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
        Write-Utf8NoBom -Path $tempPath -Text (($Value | ConvertTo-Json -Depth 14) + [Environment]::NewLine)
        Move-Item -LiteralPath $tempPath -Destination $fullPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
    }
    return $fullPath
}

function Get-EvidenceIdentity {
    param([Parameter(Mandatory = $true)]$Evidence, [Parameter(Mandatory = $true)]$Session)
    if ([int]$Evidence.schemaVersion -ne $Script:EvidenceSchemaVersion -or [string]$Evidence.gate -ne $Script:EvidenceGateName) {
        throw "Manual-QA evidence identity/schema is invalid."
    }
    if ([string]$Evidence.targetVersion -ne [string]$Session.package.version -or
        [string]$Evidence.package.version -ne [string]$Session.package.version -or
        ([string]$Evidence.package.sha256).ToLowerInvariant() -ne ([string]$Session.package.sha256).ToLowerInvariant() -or
        ([string]$Evidence.package.entryPointSha256).ToLowerInvariant() -ne ([string]$Session.package.entryPointSha256).ToLowerInvariant() -or
        ([string]$Evidence.package.betaManualQaEntryPointSha256).ToLowerInvariant() -ne ([string]$Session.package.qaToolSha256).ToLowerInvariant() -or
        ([string]$Evidence.createdQaToolSha256).ToLowerInvariant() -ne ([string]$Session.package.qaToolSha256).ToLowerInvariant()) {
        throw "Manual-QA evidence is bound to a different candidate identity than the QA session."
    }

    $created = $Evidence.createdEnvironment
    $canonical = @(
        [string]$Evidence.schemaVersion,
        [string]$Evidence.gate,
        [string]$Evidence.targetVersion,
        ([string]$Evidence.package.sha256).ToLowerInvariant(),
        ([string]$Evidence.package.entryPointSha256).ToLowerInvariant(),
        ([string]$Evidence.package.betaManualQaEntryPointSha256).ToLowerInvariant(),
        ([string]$Evidence.createdQaToolSha256).ToLowerInvariant(),
        [string]$Evidence.createdUtc,
        [string]$created.osBuild,
        [string]$created.processArchitecture,
        [string]$created.sessionId,
        [string]$created.userInteractive,
        [string]$created.processElevated,
        [string]$created.uacEnabled
    ) -join "|"
    return Get-Sha256Text -Text $canonical
}

function Assert-WitnessForSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$WitnessPath,
        [Parameter(Mandatory = $true)][string]$WitnessSidecar,
        [Parameter(Mandatory = $true)]$Session,
        [Parameter(Mandatory = $true)][string]$SessionSha256,
        [Parameter(Mandatory = $true)][string]$EvidenceIdentitySha256,
        [Parameter(Mandatory = $true)][string]$PackageSha256,
        [Parameter(Mandatory = $true)][string]$Workspace
    )

    Assert-Inside -Child $WitnessPath -Parent $Workspace -Label "Desktop witness"
    Assert-Inside -Child $WitnessSidecar -Parent $Workspace -Label "Desktop witness sidecar"
    $witnessHash = Assert-Sidecar -PayloadPath $WitnessPath -SidecarPath $WitnessSidecar -Label "Desktop witness"
    $witness = Load-Json -Path $WitnessPath -Label "Desktop witness"
    if ([int]$witness.schemaVersion -ne $Script:WitnessSchemaVersion) { throw "Unsupported desktop witness schema '$($witness.schemaVersion)'." }
    if ([bool]$witness.humanGateClaimed) { throw "Desktop witness metadata cannot claim that a human release gate passed." }
    if (([string]$witness.packageSha256).ToLowerInvariant() -ne $PackageSha256) { throw "Desktop witness is bound to a different candidate package." }
    if (([string]$witness.sessionSha256).ToLowerInvariant() -ne $SessionSha256) { throw "Desktop witness is bound to different QA session metadata." }
    if (([string]$witness.evidenceIdentitySha256).ToLowerInvariant() -ne $EvidenceIdentitySha256) { throw "Desktop witness is bound to a different immutable manual-QA evidence identity." }

    $dropTarget = Get-FullPath ([string]$Session.dropTarget)
    if (-not [string]::Equals((Get-FullPath ([string]$witness.dropTarget)), $dropTarget, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Desktop witness is bound to a different Explorer drop target."
    }
    if ($null -ne $witness.observation) {
        Assert-Sha256Value -Value ([string]$witness.observation.treeSha256) -Label "Desktop witness destination tree SHA-256"
        if ([int]$witness.observation.itemCount -le 0 -or [long]$witness.observation.hashedBytes -lt 0) {
            throw "Desktop witness observation is not a valid positive bounded destination snapshot."
        }
    }

    return [pscustomobject]@{
        path = (Resolve-Path -LiteralPath $WitnessPath).Path
        sidecar = (Resolve-Path -LiteralPath $WitnessSidecar).Path
        sha256 = $witnessHash
        value = $witness
        observationCaptured = ($null -ne $witness.observation)
    }
}

function Resolve-WorkspaceSnapshot {
    param([Parameter(Mandatory = $true)][string]$Workspace, [switch]$PreserveWitness)

    $workspaceFull = (Resolve-Path -LiteralPath $Workspace).Path
    if (-not (Test-Path -LiteralPath $workspaceFull -PathType Container)) { throw "QA workspace is not a directory: $workspaceFull" }

    $sessionPath = Join-Path $workspaceFull $Script:SessionFileName
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) { throw "QA session metadata is missing: $sessionPath" }
    $session = Load-Json -Path $sessionPath -Label "QA session metadata"
    if ([int]$session.schemaVersion -ne $Script:SessionSchemaVersion) { throw "Unsupported QA session schema '$($session.schemaVersion)'." }
    if ([bool]$session.manualGatePassed) { throw "Session metadata cannot authoritatively claim that a human release gate passed." }
    $sessionHash = Get-Sha256 $sessionPath

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

    $evidenceIdentity = Get-EvidenceIdentity -Evidence $evidence -Session $session

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

    $witness = $null
    if ($PreserveWitness) {
        $witnessPath = Join-Path $workspaceFull $Script:WitnessFileName
        $witnessSidecar = "$witnessPath.sha256"
        if (-not (Test-Path -LiteralPath $witnessPath -PathType Leaf) -or -not (Test-Path -LiteralPath $witnessSidecar -PathType Leaf)) {
            throw "-IncludeWitness requires a completed desktop witness and sidecar in the QA workspace."
        }
        $witness = Assert-WitnessForSnapshot -WitnessPath $witnessPath -WitnessSidecar $witnessSidecar -Session $session -SessionSha256 $sessionHash -EvidenceIdentitySha256 $evidenceIdentity -PackageSha256 $packageHash -Workspace $workspaceFull
    }

    return [pscustomobject]@{
        workspace = $workspaceFull
        sessionPath = $sessionPath
        sessionHash = $sessionHash
        session = $session
        evidencePath = $evidencePath
        evidenceSidecar = $evidenceSidecar
        evidence = $evidence
        evidenceHash = $evidenceHash
        evidenceIdentity = $evidenceIdentity
        packageHash = $packageHash
        packageManifestPath = $packageManifestPath
        packageManifestHash = (Get-Sha256 $packageManifestPath)
        entryHash = $entryHash
        qaHash = $qaHash
        witness = $witness
    }
}

function Get-ExpectedArchiveFileNames {
    param([int]$SchemaVersion, [bool]$WitnessIncluded)
    $names = @($Script:EvidenceFileName, "$($Script:EvidenceFileName).sha256", "package-manifest.json", "session-provenance.json")
    if ($SchemaVersion -ge 2 -and $WitnessIncluded) {
        $names += $Script:WitnessFileName
        $names += "$($Script:WitnessFileName).sha256"
    }
    return @($names | Sort-Object)
}

function Invoke-VerifyArchive {
    param([Parameter(Mandatory = $true)][string]$Path)

    $root = (Resolve-Path -LiteralPath $Path).Path
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Evidence archive is not a directory: $root" }
    $manifestPath = Join-Path $root "archive-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Evidence archive manifest is missing." }
    $manifest = Load-Json -Path $manifestPath -Label "Evidence archive manifest"
    $schema = [int]$manifest.schemaVersion
    if (($schema -ne $Script:LegacyArchiveSchemaVersion -and $schema -ne $Script:ArchiveSchemaVersion) -or [string]$manifest.product -ne "Dragon DiskForge" -or [string]$manifest.purpose -ne "beta-manual-qa-evidence-snapshot") {
        throw "Evidence archive manifest identity/schema is invalid."
    }
    if ([bool]$manifest.releaseGateClaimed) { throw "An evidence archive cannot claim release-gate completion." }

    $witnessIncluded = $false
    if ($schema -ge 2) {
        if ($null -eq $manifest.witness) { throw "Archive schema 2 requires an explicit witness descriptor." }
        $witnessIncluded = [bool]$manifest.witness.included
        if ([bool]$manifest.witness.humanGateClaimed) { throw "Archived desktop witness metadata cannot claim a human release gate passed." }
    }

    $files = @($manifest.files)
    $actualNames = @($files | ForEach-Object { [string]$_.name } | Sort-Object)
    $expectedNames = Get-ExpectedArchiveFileNames -SchemaVersion $schema -WitnessIncluded $witnessIncluded
    if ($actualNames.Count -ne $expectedNames.Count -or @(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $actualNames).Count -ne 0) {
        throw "Evidence archive manifest payload set is not canonical for schema $schema."
    }

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
        ([string]$evidence.package.sha256).ToLowerInvariant() -ne ([string]$manifest.package.sha256).ToLowerInvariant() -or
        ([string]$evidence.package.entryPointSha256).ToLowerInvariant() -ne ([string]$manifest.package.entryPointSha256).ToLowerInvariant() -or
        ([string]$evidence.package.betaManualQaEntryPointSha256).ToLowerInvariant() -ne ([string]$manifest.package.betaManualQaEntryPointSha256).ToLowerInvariant()) {
        throw "Archived manual-QA evidence disagrees with archive package identity."
    }

    $packageManifestPath = Join-Path $root "package-manifest.json"
    if ((Get-Sha256 $packageManifestPath) -ne ([string]$manifest.package.packageManifestSha256).ToLowerInvariant()) { throw "Archived package-manifest SHA-256 differs from archive manifest." }
    $packageManifest = Load-Json -Path $packageManifestPath -Label "Archived package manifest"
    if ([string]$packageManifest.version -ne [string]$manifest.targetVersion -or
        [string]$packageManifest.architecture -ne [string]$manifest.package.architecture -or
        ([string]$packageManifest.entryPointSha256).ToLowerInvariant() -ne ([string]$manifest.package.entryPointSha256).ToLowerInvariant() -or
        ([string]$packageManifest.betaManualQaEntryPointSha256).ToLowerInvariant() -ne ([string]$manifest.package.betaManualQaEntryPointSha256).ToLowerInvariant()) {
        throw "Archived package manifest disagrees with archive package identity."
    }

    $provenancePath = Join-Path $root "session-provenance.json"
    $provenance = Load-Json -Path $provenancePath -Label "Archived session provenance"
    if ([bool]$provenance.releaseGateClaimed) { throw "Session provenance cannot claim release-gate completion." }
    if (([string]$provenance.package.sha256).ToLowerInvariant() -ne ([string]$manifest.package.sha256).ToLowerInvariant() -or [string]$provenance.package.version -ne [string]$manifest.targetVersion) {
        throw "Archived session provenance disagrees with archive package identity."
    }

    if ($schema -ge 2) {
        Assert-Sha256Value -Value ([string]$manifest.sessionMetadataSha256) -Label "Archive session metadata SHA-256"
        if (([string]$provenance.sessionMetadataSha256).ToLowerInvariant() -ne ([string]$manifest.sessionMetadataSha256).ToLowerInvariant()) {
            throw "Archived session provenance disagrees with the recorded session metadata SHA-256."
        }

        if ($witnessIncluded) {
            $witnessPath = Join-Path $root $Script:WitnessFileName
            $witnessHash = Assert-Sidecar -PayloadPath $witnessPath -SidecarPath "$witnessPath.sha256" -Label "Archived desktop witness"
            if ($witnessHash -ne ([string]$manifest.witness.sha256).ToLowerInvariant()) { throw "Archived desktop witness hash differs from archive manifest." }
            $witness = Load-Json -Path $witnessPath -Label "Archived desktop witness"
            if ([int]$witness.schemaVersion -ne $Script:WitnessSchemaVersion -or [bool]$witness.humanGateClaimed) { throw "Archived desktop witness identity/schema is invalid." }
            if (([string]$witness.packageSha256).ToLowerInvariant() -ne ([string]$manifest.package.sha256).ToLowerInvariant()) { throw "Archived desktop witness disagrees with candidate package identity." }
            if (([string]$witness.sessionSha256).ToLowerInvariant() -ne ([string]$manifest.sessionMetadataSha256).ToLowerInvariant()) { throw "Archived desktop witness disagrees with original QA session metadata identity." }

            $sessionProxy = [pscustomobject]@{ package = [pscustomobject]@{
                version = [string]$manifest.targetVersion
                sha256 = [string]$manifest.package.sha256
                entryPointSha256 = [string]$manifest.package.entryPointSha256
                qaToolSha256 = [string]$manifest.package.betaManualQaEntryPointSha256
            } }
            $identity = Get-EvidenceIdentity -Evidence $evidence -Session $sessionProxy
            if (([string]$witness.evidenceIdentitySha256).ToLowerInvariant() -ne $identity) { throw "Archived desktop witness disagrees with immutable manual-QA evidence identity." }
            if ([bool]$manifest.witness.observationCaptured -ne ($null -ne $witness.observation)) { throw "Archive witness observation state disagrees with the witness payload." }
            if ($null -ne $witness.observation) {
                Assert-Sha256Value -Value ([string]$witness.observation.treeSha256) -Label "Archived desktop witness destination tree SHA-256"
                if ([int]$witness.observation.itemCount -le 0 -or [long]$witness.observation.hashedBytes -lt 0) { throw "Archived desktop witness observation is invalid." }
            }
        }
    }

    Write-Host "Beta manual-QA evidence archive verified: $root"
    if ($witnessIncluded) { Write-Host "Desktop witness preserved and package/session/evidence binding verified." }
    Write-Host "Integrity/provenance verified; human QA completion is intentionally not inferred."
    return $true
}

function Invoke-ArchiveWorkspace {
    param([Parameter(Mandatory = $true)][string]$Workspace, [Parameter(Mandatory = $true)][string]$Root, [switch]$PreserveWitness)

    $snapshot = Resolve-WorkspaceSnapshot -Workspace $Workspace -PreserveWitness:$PreserveWitness
    $archiveRootFull = Get-FullPath $Root
    if (Test-PathInside -Child $archiveRootFull -Parent $snapshot.workspace) {
        throw "ArchiveRoot must be outside the disposable QA workspace so cleanup cannot delete preserved evidence."
    }

    New-Item -ItemType Directory -Path $archiveRootFull -Force | Out-Null
    $safeVersion = ([string]$snapshot.session.package.version) -replace '[^A-Za-z0-9._-]', '_'
    $suffix = if ($PreserveWitness) { "-witness" } else { "" }
    $archiveName = "{0}-{1}-{2}{3}" -f $safeVersion, $snapshot.packageHash.Substring(0, 12), $snapshot.evidenceHash.Substring(0, 12), $suffix
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

        $files = @()
        $files += [ordered]@{ name = $Script:EvidenceFileName; sha256 = (Get-Sha256 $evidenceCopy) }
        $files += [ordered]@{ name = "$($Script:EvidenceFileName).sha256"; sha256 = (Get-Sha256 $evidenceSidecarCopy) }
        $files += [ordered]@{ name = "package-manifest.json"; sha256 = (Get-Sha256 $packageManifestCopy) }

        $witnessDescriptor = [ordered]@{ included = $false; schemaVersion = $null; sha256 = $null; observationCaptured = $false; humanGateClaimed = $false }
        if ($PreserveWitness) {
            $witnessCopy = Join-Path $temp $Script:WitnessFileName
            $witnessSidecarCopy = "$witnessCopy.sha256"
            Copy-Item -LiteralPath $snapshot.witness.path -Destination $witnessCopy
            Copy-Item -LiteralPath $snapshot.witness.sidecar -Destination $witnessSidecarCopy
            $files += [ordered]@{ name = $Script:WitnessFileName; sha256 = (Get-Sha256 $witnessCopy) }
            $files += [ordered]@{ name = "$($Script:WitnessFileName).sha256"; sha256 = (Get-Sha256 $witnessSidecarCopy) }
            $witnessDescriptor = [ordered]@{
                included = $true
                schemaVersion = [int]$snapshot.witness.value.schemaVersion
                sha256 = $snapshot.witness.sha256
                observationCaptured = [bool]$snapshot.witness.observationCaptured
                humanGateClaimed = $false
            }
        }

        $provenance = [ordered]@{
            schemaVersion = 2
            createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
            source = "beta-qa-session"
            sessionSchemaVersion = [int]$snapshot.session.schemaVersion
            sessionMetadataSha256 = $snapshot.sessionHash
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
            witnessIncluded = [bool]$PreserveWitness
            releaseGateClaimed = $false
            note = "Integrity-preserving snapshot only. Human QA completion must be verified separately against the exact candidate package."
        }
        $provenancePath = Save-JsonAtomic -Value $provenance -Path (Join-Path $temp "session-provenance.json")
        $files += [ordered]@{ name = "session-provenance.json"; sha256 = (Get-Sha256 $provenancePath) }

        $manifest = [ordered]@{
            schemaVersion = $Script:ArchiveSchemaVersion
            product = "Dragon DiskForge"
            purpose = "beta-manual-qa-evidence-snapshot"
            createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
            targetVersion = [string]$snapshot.session.package.version
            releaseGateClaimed = $false
            sessionMetadataSha256 = $snapshot.sessionHash
            package = [ordered]@{
                sha256 = $snapshot.packageHash
                architecture = [string]$snapshot.session.package.architecture
                entryPointSha256 = $snapshot.entryHash
                betaManualQaEntryPointSha256 = $snapshot.qaHash
                packageManifestSha256 = $snapshot.packageManifestHash
            }
            evidence = [ordered]@{ schemaVersion = [int]$snapshot.evidence.schemaVersion; sha256 = $snapshot.evidenceHash; gate = [string]$snapshot.evidence.gate; identitySha256 = $snapshot.evidenceIdentity }
            witness = $witnessDescriptor
            files = @($files)
        }
        Save-JsonAtomic -Value $manifest -Path (Join-Path $temp "archive-manifest.json") | Out-Null
        Move-Item -LiteralPath $temp -Destination $destination
        Invoke-VerifyArchive -Path $destination | Out-Null
        Write-Host "Preserved beta manual-QA evidence snapshot: $destination"
        if ($PreserveWitness) { Write-Host "Desktop witness was explicitly included and cross-bound to package/session/evidence identity." }
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
        schemaVersion = $Script:EvidenceSchemaVersion
        gate = $Script:EvidenceGateName
        targetVersion = "0.5.0-beta.1"
        createdUtc = "2026-09-18T00:00:00Z"
        updatedUtc = "2026-09-18T00:00:00Z"
        package = [ordered]@{ fileName = [System.IO.Path]::GetFileName($package); sha256 = $packageHash; version = "0.5.0-beta.1"; architecture = "x64"; entryPointSha256 = $entryHash; betaManualQaEntryPointSha256 = $qaHash }
        createdQaToolSha256 = $qaHash
        createdEnvironment = [ordered]@{ osBuild = "26100"; processArchitecture = "X64"; userInteractive = $true; processElevated = $false; sessionId = 1; uacEnabled = $true }
        checks = @()
    }
    $evidencePath = Join-Path $workspace $Script:EvidenceFileName
    Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
    $evidenceHash = Get-Sha256 $evidencePath
    Write-Utf8NoBom -Path "$evidencePath.sha256" -Text ("{0}  {1}{2}" -f $evidenceHash, [System.IO.Path]::GetFileName($evidencePath), [Environment]::NewLine)

    $dropTarget = Join-Path $workspace "drop"
    New-Item -ItemType Directory -Path $dropTarget -Force | Out-Null
    $session = [ordered]@{
        schemaVersion = $Script:SessionSchemaVersion
        createdUtc = "2026-09-18T00:00:00Z"
        package = [ordered]@{ path = (Get-FullPath $package); checksumFile = (Get-FullPath $packageSidecar); sha256 = $packageHash; version = "0.5.0-beta.1"; architecture = "x64"; entryPointSha256 = $entryHash; qaToolSha256 = $qaHash }
        environment = [ordered]@{ osBuild = "26100"; processArchitecture = "X64"; userInteractive = $true; processElevated = $false; sessionId = 1; uacEnabled = $true }
        workspace = (Get-FullPath $workspace)
        extractRoot = (Get-FullPath $extractRoot)
        evidencePath = (Get-FullPath $evidencePath)
        dropTarget = (Get-FullPath $dropTarget)
        appProcessId = 0
        appPath = (Get-FullPath $entry)
        launchProbeSeconds = 1
        launchProbePassed = $true
        manualGatePassed = $false
        note = "self-test"
    }
    $sessionPath = Join-Path $workspace $Script:SessionFileName
    Write-Utf8NoBom -Path $sessionPath -Text (($session | ConvertTo-Json -Depth 10) + [Environment]::NewLine)

    $evidenceObject = Load-Json -Path $evidencePath -Label "Self-test evidence"
    $sessionObject = Load-Json -Path $sessionPath -Label "Self-test session"
    $evidenceIdentity = Get-EvidenceIdentity -Evidence $evidenceObject -Session $sessionObject
    $witness = [ordered]@{
        schemaVersion = 1
        createdUtc = "2026-09-18T00:00:01Z"
        packageSha256 = $packageHash
        sessionSha256 = (Get-Sha256 $sessionPath)
        evidenceIdentitySha256 = $evidenceIdentity
        evidenceSha256AtBaseline = $evidenceHash
        environment = $session.environment
        app = [ordered]@{ processId = 1; processStartUtc = "2026-09-18T00:00:00Z"; windowHandle = 1; visible = $true }
        dropTarget = (Get-FullPath $dropTarget)
        baseline = [ordered]@{ itemCount = 0; treeSha256 = (Get-Sha256Text -Text "") }
        observation = [ordered]@{ observedUtc = "2026-09-18T00:00:02Z"; evidenceSha256AtObservation = $evidenceHash; appProcessId = 1; appProcessStartUtc = "2026-09-18T00:00:00Z"; appWindowHandle = 1; itemCount = 1; fileCount = 1; directoryCount = 0; hashedBytes = 9; treeSha256 = ("d" * 64); records = @() }
        humanGateClaimed = $false
        note = "self-test objective witness"
    }
    $witnessPath = Join-Path $workspace $Script:WitnessFileName
    Write-Utf8NoBom -Path $witnessPath -Text (($witness | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
    $witnessHash = Get-Sha256 $witnessPath
    Write-Utf8NoBom -Path "$witnessPath.sha256" -Text ("{0}  {1}{2}" -f $witnessHash, [System.IO.Path]::GetFileName($witnessPath), [Environment]::NewLine)

    return $workspace
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-archive-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $workspace = New-SelfTestWorkspace -Root $root
        $archiveRootPath = Join-Path $root "archives"

        $plainArchive = Invoke-ArchiveWorkspace -Workspace $workspace -Root $archiveRootPath
        Invoke-VerifyArchive -Path $plainArchive | Out-Null
        $plainManifest = Load-Json -Path (Join-Path $plainArchive "archive-manifest.json") -Label "Plain archive manifest"
        if ([int]$plainManifest.schemaVersion -ne 2 -or [bool]$plainManifest.witness.included) { throw "Self-test failed: plain schema-v2 archive incorrectly included a witness." }

        $witnessArchive = Invoke-ArchiveWorkspace -Workspace $workspace -Root $archiveRootPath -PreserveWitness
        Invoke-VerifyArchive -Path $witnessArchive | Out-Null
        $witnessManifest = Load-Json -Path (Join-Path $witnessArchive "archive-manifest.json") -Label "Witness archive manifest"
        if (-not [bool]$witnessManifest.witness.included -or -not [bool]$witnessManifest.witness.observationCaptured) { throw "Self-test failed: witness archive did not bind the observed desktop witness." }

        Add-Content -LiteralPath (Join-Path $witnessArchive $Script:WitnessFileName) -Value "tamper"
        $witnessTamperRejected = $false
        try { Invoke-VerifyArchive -Path $witnessArchive | Out-Null } catch { $witnessTamperRejected = $true }
        if (-not $witnessTamperRejected) { throw "Self-test failed: tampered archived desktop witness did not fail closed." }

        $badWorkspace = New-SelfTestWorkspace -Root (Join-Path $root "bad")
        $badWitnessPath = Join-Path $badWorkspace $Script:WitnessFileName
        $badWitness = Load-Json -Path $badWitnessPath -Label "Bad witness"
        $badWitness.humanGateClaimed = $true
        Write-Utf8NoBom -Path $badWitnessPath -Text (($badWitness | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
        $badWitnessHash = Get-Sha256 $badWitnessPath
        Write-Utf8NoBom -Path "$badWitnessPath.sha256" -Text ("{0}  {1}{2}" -f $badWitnessHash, [System.IO.Path]::GetFileName($badWitnessPath), [Environment]::NewLine)
        $gateClaimRejected = $false
        try { Invoke-ArchiveWorkspace -Workspace $badWorkspace -Root (Join-Path $root "bad-archives") -PreserveWitness | Out-Null } catch { $gateClaimRejected = $true }
        if (-not $gateClaimRejected) { throw "Self-test failed: witness human-gate claim was accepted." }

        $overlapRejected = $false
        try { Invoke-ArchiveWorkspace -Workspace $workspace -Root (Join-Path $workspace "archive") | Out-Null } catch { $overlapRejected = $true }
        if (-not $overlapRejected) { throw "Self-test failed: archive root inside disposable workspace did not fail closed." }

        Add-Content -LiteralPath (Join-Path $plainArchive $Script:EvidenceFileName) -Value "tamper"
        $evidenceTamperRejected = $false
        try { Invoke-VerifyArchive -Path $plainArchive | Out-Null } catch { $evidenceTamperRejected = $true }
        if (-not $evidenceTamperRejected) { throw "Self-test failed: tampered evidence did not fail closed." }

        Write-Host "Dragon DiskForge beta QA evidence archive self-test passed."
        Write-Host "Schema 2 verified both plain and explicit witness-preserving archives; tamper/gate/overlap cases fail closed."
    }
    finally {
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

switch ($Mode) {
    "archive" {
        if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required for -Mode archive." }
        Invoke-ArchiveWorkspace -Workspace $WorkspacePath -Root $ArchiveRoot -PreserveWitness:$IncludeWitness | Out-Null
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
