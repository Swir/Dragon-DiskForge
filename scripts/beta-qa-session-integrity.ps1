[CmdletBinding()]
param(
    [ValidateSet("verify", "self-test")]
    [string]$Mode = "verify",

    [string]$WorkspacePath = "",
    [string]$ExpectedVersion = "0.5.0-beta.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:SessionFileName = "beta-qa-session.json"
$Script:EvidenceFileName = "beta-manual-qa.json"
$Script:ExpectedSessionSchema = 1
$Script:ExpectedEvidenceSchema = 3
$Script:ExpectedEvidenceGate = "Dragon DiskForge interactive beta QA"

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

function Get-NormalizedFullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Context,
        [switch]$AllowRoot
    )

    $rootFull = Get-NormalizedFullPath -Path $Root
    $pathFull = Get-NormalizedFullPath -Path $Path
    $separator = [System.IO.Path]::DirectorySeparatorChar
    $rootPrefix = $rootFull.TrimEnd([char[]]@('\', '/')) + $separator

    if ([string]::Equals($rootFull, $pathFull, [StringComparison]::OrdinalIgnoreCase)) {
        if ($AllowRoot) {
            return $pathFull
        }
        throw "$Context resolves to the workspace root instead of a contained path."
    }

    if (-not $pathFull.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Context escapes the prepared beta-QA workspace."
    }

    return $pathFull
}

function Assert-Sha256Text {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Context is not a valid SHA-256 digest."
    }
    return $Value.ToLowerInvariant()
}

function Assert-FileSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Context is missing: $Path"
    }

    $expectedNormalized = Assert-Sha256Text -Value $Expected -Context "$Context expected digest"
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expectedNormalized) {
        throw "$Context SHA-256 mismatch. Expected '$expectedNormalized', actual '$actual'."
    }
    return $actual
}

function Resolve-VerifiedSidecar {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$SidecarPath,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $file = (Resolve-Path -LiteralPath $FilePath).Path
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path
    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "$Context SHA-256 sidecar has an invalid format."
    }

    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    if ($declaredName -ne [System.IO.Path]::GetFileName($file)) {
        throw "$Context SHA-256 sidecar targets '$declaredName' instead of '$([System.IO.Path]::GetFileName($file))'."
    }

    $actualHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "$Context SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'."
    }

    return [pscustomobject]@{
        filePath = $file
        sidecarPath = $sidecar
        sha256 = $actualHash
    }
}

function Get-CanonicalPackageIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$ChecksumFile
    )

    $verified = Resolve-VerifiedSidecar -FilePath $PackagePath -SidecarPath $ChecksumFile -Context "Candidate package"
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-session-integrity-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    try {
        Expand-Archive -LiteralPath $verified.filePath -DestinationPath $tempRoot -Force
        $manifestPath = Join-Path $tempRoot "package-manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Candidate package manifest is missing."
        }

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -lt 5) {
            throw "Candidate package manifest schema '$($manifest.schemaVersion)' predates the beta-QA contract."
        }
        if ([string]$manifest.product -ne "Dragon DiskForge") {
            throw "Unexpected candidate package product '$($manifest.product)'."
        }
        if ([string]$manifest.version -ne $ExpectedVersion) {
            throw "Candidate package version '$($manifest.version)' differs from required '$ExpectedVersion'."
        }
        if ([string]$manifest.architecture -ne "x64") {
            throw "Candidate package architecture '$($manifest.architecture)' is not x64."
        }
        if ([string]$manifest.runtimeDeployment.dotNet -ne "self-contained" -or
            [string]$manifest.runtimeDeployment.windowsAppSdk -ne "self-contained" -or
            [string]$manifest.runtimeDeployment.visualCpp -ne "app-local") {
            throw "Candidate package runtime deployment is incomplete."
        }

        $entryRelative = ([string]$manifest.entryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $qaRelative = ([string]$manifest.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $entryPath = Join-Path $tempRoot $entryRelative
        $qaPath = Join-Path $tempRoot $qaRelative
        $entryHash = Assert-FileSha256 -Path $entryPath -Expected ([string]$manifest.entryPointSha256) -Context "Candidate desktop entry point"
        $qaHash = Assert-FileSha256 -Path $qaPath -Expected ([string]$manifest.betaManualQaEntryPointSha256) -Context "Candidate beta manual-QA tool"

        return [pscustomobject]@{
            packagePath = $verified.filePath
            checksumFile = $verified.sidecarPath
            sha256 = $verified.sha256
            version = [string]$manifest.version
            architecture = [string]$manifest.architecture
            entryPointRelative = $entryRelative
            entryPointSha256 = $entryHash
            qaToolRelative = $qaRelative
            qaToolSha256 = $qaHash
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Context is missing: $Path"
    }
    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    }
    catch {
        throw "$Context is not valid JSON: $($_.Exception.Message)"
    }
}

function Assert-PreparedSessionIntegrity {
    param([Parameter(Mandatory = $true)][string]$Workspace)

    $workspaceFull = (Resolve-Path -LiteralPath $Workspace).Path
    if (-not (Test-Path -LiteralPath $workspaceFull -PathType Container)) {
        throw "Prepared beta-QA workspace is missing: $workspaceFull"
    }

    $sessionPath = Join-Path $workspaceFull $Script:SessionFileName
    $session = Read-JsonFile -Path $sessionPath -Context "Beta-QA session metadata"
    if ([int]$session.schemaVersion -ne $Script:ExpectedSessionSchema) {
        throw "Unsupported beta-QA session schema '$($session.schemaVersion)'."
    }

    $recordedWorkspace = Get-NormalizedFullPath -Path ([string]$session.workspace)
    if (-not [string]::Equals($workspaceFull, $recordedWorkspace, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Session workspace path does not match the workspace being verified."
    }

    $extractRoot = Assert-PathWithinRoot -Root $workspaceFull -Path ([string]$session.extractRoot) -Context "Extracted package path"
    $evidencePath = Assert-PathWithinRoot -Root $workspaceFull -Path ([string]$session.evidencePath) -Context "Manual-QA evidence path"
    $dropTarget = Assert-PathWithinRoot -Root $workspaceFull -Path ([string]$session.dropTarget) -Context "Explorer drop target"
    $appPath = Assert-PathWithinRoot -Root $extractRoot -Path ([string]$session.appPath) -Context "Recorded desktop entry point"

    if (-not (Test-Path -LiteralPath $extractRoot -PathType Container)) {
        throw "Extracted candidate directory is missing: $extractRoot"
    }
    if (-not (Test-Path -LiteralPath $dropTarget -PathType Container)) {
        throw "Explorer drop-target directory is missing: $dropTarget"
    }
    $dropTargetItem = Get-Item -LiteralPath $dropTarget -Force
    if (($dropTargetItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Explorer drop target is a reparse point; refusing to treat it as the prepared isolated target."
    }

    if (-not [bool]$session.launchProbePassed) {
        throw "Session metadata does not record a successful local launch liveness preflight."
    }
    if ([bool]$session.manualGatePassed) {
        throw "Prepared-session metadata must not claim that the human beta gate passed."
    }

    $package = Get-CanonicalPackageIdentity -PackagePath ([string]$session.package.path) -ChecksumFile ([string]$session.package.checksumFile)
    if ([string]$session.package.sha256 -ne [string]$package.sha256) {
        throw "Session metadata is bound to a different candidate package SHA-256."
    }
    if ([string]$session.package.version -ne [string]$package.version -or [string]$session.package.architecture -ne [string]$package.architecture) {
        throw "Session metadata candidate version/architecture does not match the verified package."
    }
    if ([string]$session.package.entryPointSha256 -ne [string]$package.entryPointSha256) {
        throw "Session metadata desktop entry-point SHA-256 does not match the verified package."
    }
    if ([string]$session.package.qaToolSha256 -ne [string]$package.qaToolSha256) {
        throw "Session metadata beta manual-QA tool SHA-256 does not match the verified package."
    }

    Assert-FileSha256 -Path $appPath -Expected $package.entryPointSha256 -Context "Prepared extracted desktop entry point" | Out-Null
    $qaToolPath = Assert-PathWithinRoot -Root $extractRoot -Path (Join-Path $extractRoot $package.qaToolRelative) -Context "Prepared extracted beta manual-QA tool"
    Assert-FileSha256 -Path $qaToolPath -Expected $package.qaToolSha256 -Context "Prepared extracted beta manual-QA tool" | Out-Null

    $evidenceSidecar = "$evidencePath.sha256"
    Resolve-VerifiedSidecar -FilePath $evidencePath -SidecarPath $evidenceSidecar -Context "Manual-QA evidence" | Out-Null
    $evidence = Read-JsonFile -Path $evidencePath -Context "Manual-QA evidence"
    if ([int]$evidence.schemaVersion -ne $Script:ExpectedEvidenceSchema) {
        throw "Prepared manual-QA evidence schema '$($evidence.schemaVersion)' is not schema v$Script:ExpectedEvidenceSchema."
    }
    if ([string]$evidence.gate -ne $Script:ExpectedEvidenceGate) {
        throw "Prepared manual-QA evidence has an unexpected gate '$($evidence.gate)'."
    }
    if ([string]$evidence.targetVersion -ne $ExpectedVersion) {
        throw "Prepared manual-QA evidence targets '$($evidence.targetVersion)' instead of '$ExpectedVersion'."
    }
    if ([string]$evidence.package.sha256 -ne $package.sha256 -or [string]$evidence.package.version -ne $package.version) {
        throw "Prepared manual-QA evidence is bound to a different candidate package."
    }
    if ([string]$evidence.package.entryPointSha256 -ne $package.entryPointSha256 -or
        [string]$evidence.package.betaManualQaEntryPointSha256 -ne $package.qaToolSha256 -or
        [string]$evidence.createdQaToolSha256 -ne $package.qaToolSha256) {
        throw "Prepared manual-QA evidence is not bound to the verified packaged entry points."
    }

    if (-not [bool]$evidence.createdEnvironment.userInteractive -or [int]$evidence.createdEnvironment.sessionId -le 0) {
        throw "Prepared manual-QA evidence was not initialized from a normal interactive desktop session."
    }
    if ($null -eq $evidence.createdEnvironment.processElevated -or [bool]$evidence.createdEnvironment.processElevated) {
        throw "Prepared manual-QA evidence cannot prove an unelevated initialization boundary."
    }
    if ($null -eq $evidence.createdEnvironment.uacEnabled -or -not [bool]$evidence.createdEnvironment.uacEnabled) {
        throw "Prepared manual-QA evidence cannot prove that UAC was enabled at initialization."
    }
    if ([int]$evidence.createdEnvironment.sessionId -ne [int]$session.environment.sessionId -or
        [string]$evidence.createdEnvironment.osBuild -ne [string]$session.environment.osBuild -or
        [string]$evidence.createdEnvironment.processArchitecture -ne [string]$session.environment.processArchitecture) {
        throw "Prepared manual-QA evidence baseline does not match the session metadata environment."
    }

    return [pscustomobject]@{
        workspace = $workspaceFull
        sessionPath = $sessionPath
        packageSha256 = $package.sha256
        version = $package.version
        evidencePath = $evidencePath
        evidenceSha256 = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
        entryPointSha256 = $package.entryPointSha256
        qaToolSha256 = $package.qaToolSha256
        dropTarget = $dropTarget
    }
}

function New-SelfTestWorkspace {
    param([Parameter(Mandatory = $true)][string]$Root)

    $workspace = Join-Path $Root "workspace"
    $source = Join-Path $Root "package-source"
    $extractRoot = Join-Path $workspace "package"
    $dropTarget = Join-Path $workspace "explorer-drop-target"
    New-Item -ItemType Directory -Path $workspace, $source, (Join-Path $source "tools"), $dropTarget -Force | Out-Null

    $appContent = "self-test-desktop"
    $qaContent = "Write-Host 'self-test-qa'"
    $appSource = Join-Path $source "DragonDiskForge.App.exe"
    $qaSource = Join-Path $source "tools\beta-manual-qa.ps1"
    Write-Utf8NoBom -Path $appSource -Text $appContent
    Write-Utf8NoBom -Path $qaSource -Text $qaContent
    foreach ($runtimeFile in @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll", "vcruntime140.dll", "msvcp140.dll", "Microsoft.WindowsAppRuntime.dll")) {
        Write-Utf8NoBom -Path (Join-Path $source $runtimeFile) -Text "self-test-runtime-$runtimeFile"
    }

    $appHash = (Get-FileHash -LiteralPath $appSource -Algorithm SHA256).Hash.ToLowerInvariant()
    $qaHash = (Get-FileHash -LiteralPath $qaSource -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{
        schemaVersion = 5
        product = "Dragon DiskForge"
        version = $ExpectedVersion
        architecture = "x64"
        entryPoint = "DragonDiskForge.App.exe"
        entryPointSha256 = $appHash
        betaManualQaEntryPoint = "tools/beta-manual-qa.ps1"
        betaManualQaEntryPointSha256 = $qaHash
        runtimeDeployment = [ordered]@{
            dotNet = "self-contained"
            windowsAppSdk = "self-contained"
            visualCpp = "app-local"
        }
    }
    Write-Utf8NoBom -Path (Join-Path $source "package-manifest.json") -Text (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine)

    $zip = Join-Path $Root "DragonDiskForge-win-x64.zip"
    Compress-Archive -Path (Join-Path $source "*") -DestinationPath $zip -Force
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $sidecar = "$zip.sha256"
    Write-Utf8NoBom -Path $sidecar -Text ("{0}  {1}{2}" -f $zipHash, [System.IO.Path]::GetFileName($zip), [Environment]::NewLine)
    Expand-Archive -LiteralPath $zip -DestinationPath $extractRoot -Force

    $environment = [ordered]@{
        osVersion = "Windows self-test"
        osBuild = "26100"
        processArchitecture = "X64"
        userInteractive = $true
        processElevated = $false
        sessionId = 1
        uacEnabled = $true
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
    $evidence = [ordered]@{
        schemaVersion = 3
        gate = $Script:ExpectedEvidenceGate
        targetVersion = $ExpectedVersion
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        updatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        package = [ordered]@{
            fileName = [System.IO.Path]::GetFileName($zip)
            sha256 = $zipHash
            version = $ExpectedVersion
            architecture = "x64"
            entryPointSha256 = $appHash
            betaManualQaEntryPointSha256 = $qaHash
        }
        createdQaToolSha256 = $qaHash
        createdEnvironment = $environment
        checks = @()
    }
    $evidencePath = Join-Path $workspace $Script:EvidenceFileName
    Write-Utf8NoBom -Path $evidencePath -Text (($evidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
    $evidenceHash = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Utf8NoBom -Path "$evidencePath.sha256" -Text ("{0}  {1}{2}" -f $evidenceHash, [System.IO.Path]::GetFileName($evidencePath), [Environment]::NewLine)

    $session = [ordered]@{
        schemaVersion = 1
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        package = [ordered]@{
            path = [System.IO.Path]::GetFullPath($zip)
            checksumFile = [System.IO.Path]::GetFullPath($sidecar)
            sha256 = $zipHash
            version = $ExpectedVersion
            architecture = "x64"
            entryPointSha256 = $appHash
            qaToolSha256 = $qaHash
        }
        environment = $environment
        workspace = [System.IO.Path]::GetFullPath($workspace)
        extractRoot = [System.IO.Path]::GetFullPath($extractRoot)
        evidencePath = [System.IO.Path]::GetFullPath($evidencePath)
        dropTarget = [System.IO.Path]::GetFullPath($dropTarget)
        appProcessId = 1234
        appPath = [System.IO.Path]::GetFullPath((Join-Path $extractRoot "DragonDiskForge.App.exe"))
        launchProbeSeconds = 6
        launchProbePassed = $true
        manualGatePassed = $false
        note = "Self-test prepared session."
    }
    $sessionPath = Join-Path $workspace $Script:SessionFileName
    Write-Utf8NoBom -Path $sessionPath -Text (($session | ConvertTo-Json -Depth 10) + [Environment]::NewLine)

    return [pscustomobject]@{
        workspace = $workspace
        qaPath = (Join-Path $extractRoot "tools\beta-manual-qa.ps1")
        qaContent = $qaContent
        evidencePath = $evidencePath
        evidence = $evidence
    }
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-session-integrity-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $fixture = New-SelfTestWorkspace -Root $tempRoot
        $verified = Assert-PreparedSessionIntegrity -Workspace $fixture.workspace
        if ([string]::IsNullOrWhiteSpace([string]$verified.packageSha256)) {
            throw "Self-test failed: valid prepared session produced no package identity."
        }

        Write-Utf8NoBom -Path $fixture.qaPath -Text "tampered-qa-tool"
        $tamperedToolRejected = $false
        try { Assert-PreparedSessionIntegrity -Workspace $fixture.workspace | Out-Null } catch { $tamperedToolRejected = $true }
        if (-not $tamperedToolRejected) {
            throw "Self-test failed: tampered extracted beta manual-QA tool did not fail closed."
        }
        Write-Utf8NoBom -Path $fixture.qaPath -Text $fixture.qaContent

        $evidence = Get-Content -LiteralPath $fixture.evidencePath -Raw | ConvertFrom-Json
        $evidence.package.sha256 = ("f" * 64)
        Write-Utf8NoBom -Path $fixture.evidencePath -Text (($evidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        $newEvidenceHash = (Get-FileHash -LiteralPath $fixture.evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Utf8NoBom -Path "$($fixture.evidencePath).sha256" -Text ("{0}  {1}{2}" -f $newEvidenceHash, [System.IO.Path]::GetFileName($fixture.evidencePath), [Environment]::NewLine)
        $reboundEvidenceRejected = $false
        try { Assert-PreparedSessionIntegrity -Workspace $fixture.workspace | Out-Null } catch { $reboundEvidenceRejected = $true }
        if (-not $reboundEvidenceRejected) {
            throw "Self-test failed: evidence rebound to another package did not fail closed."
        }

        Write-Host "Dragon DiskForge prepared beta-QA session integrity self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    "verify" {
        if ([string]::IsNullOrWhiteSpace($WorkspacePath)) {
            throw "-WorkspacePath is required for -Mode verify."
        }
        $verified = Assert-PreparedSessionIntegrity -Workspace $WorkspacePath
        Write-Host "Prepared beta-QA session integrity is VALID for the exact retained candidate."
        Write-Host "Version: $($verified.version)"
        Write-Host "Package SHA-256: $($verified.packageSha256)"
        Write-Host "Evidence SHA-256: $($verified.evidenceSha256)"
        Write-Host "Workspace: $($verified.workspace)"
        exit 0
    }
    "self-test" {
        Invoke-SelfTest
        exit 0
    }
    default { throw "Unsupported mode '$Mode'." }
}
