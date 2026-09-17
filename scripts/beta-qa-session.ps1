[CmdletBinding()]
param(
    [ValidateSet("prepare", "status", "cleanup", "self-test")]
    [string]$Mode = "prepare",

    [string]$PackagePath = "",
    [string]$ChecksumFile = "",
    [string]$WorkspacePath = "",
    [string]$ExpectedVersion = "0.5.0-beta.1",
    [int]$LaunchProbeSeconds = 6,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:SessionSchemaVersion = 1
$Script:SessionFileName = "beta-qa-session.json"
$Script:EvidenceFileName = "beta-manual-qa.json"

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

function Save-JsonAtomic {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
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

function Get-IsElevated {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        return $null
    }

    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $null
    }
}

function Get-UacEnabled {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        return $null
    }

    try {
        $value = Get-ItemPropertyValue -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Name EnableLUA -ErrorAction Stop
        return ([int]$value -ne 0)
    }
    catch {
        return $null
    }
}

function Get-SessionEnvironment {
    $sessionId = -1
    try {
        $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
    }
    catch {
        $sessionId = -1
    }

    $build = ""
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        try {
            $build = [string](Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction Stop).CurrentBuildNumber
        }
        catch {
            $build = [Environment]::OSVersion.Version.Build.ToString()
        }
    }

    return [pscustomobject]@{
        osVersion = [Environment]::OSVersion.VersionString
        osBuild = $build
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        userInteractive = [Environment]::UserInteractive
        processElevated = (Get-IsElevated)
        sessionId = $sessionId
        uacEnabled = (Get-UacEnabled)
        powerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
}

function Assert-InteractiveUnelevated {
    param(
        [Parameter(Mandatory = $true)]$EnvironmentInfo,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw "$Context is supported only on Windows."
    }
    if (-not [bool]$EnvironmentInfo.userInteractive -or [int]$EnvironmentInfo.sessionId -le 0) {
        throw "$Context requires an interactive desktop session with a session id greater than zero."
    }
    if ($null -eq $EnvironmentInfo.processElevated) {
        throw "$Context cannot determine whether the current process is elevated."
    }
    if ([bool]$EnvironmentInfo.processElevated) {
        throw "$Context must run from a normal unelevated session so UAC behavior remains observable."
    }
    if ($null -eq $EnvironmentInfo.uacEnabled) {
        throw "$Context cannot determine whether Windows UAC (EnableLUA) is enabled."
    }
    if (-not [bool]$EnvironmentInfo.uacEnabled) {
        throw "$Context requires Windows UAC (EnableLUA) to be enabled."
    }
}

function Resolve-ChecksumSidecar {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [string]$SidecarPath = ""
    )

    $zip = (Resolve-Path -LiteralPath $ZipPath).Path
    if ([string]::IsNullOrWhiteSpace($SidecarPath)) {
        $SidecarPath = "$zip.sha256"
    }
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path

    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "Package checksum sidecar has an invalid format."
    }

    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    if ($declaredName -ne [System.IO.Path]::GetFileName($zip)) {
        throw "Package checksum sidecar targets '$declaredName' instead of '$([System.IO.Path]::GetFileName($zip))'."
    }

    $actualHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "Package ZIP SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'."
    }

    return [pscustomobject]@{
        zipPath = $zip
        sidecarPath = $sidecar
        sha256 = $actualHash
    }
}

function Get-CandidateIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][string]$ExtractRoot,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $checksum = Resolve-ChecksumSidecar -ZipPath $ZipPath -SidecarPath $SidecarPath

    if (Test-Path -LiteralPath $ExtractRoot) {
        Remove-Item -LiteralPath $ExtractRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $ExtractRoot -Force | Out-Null
    Expand-Archive -LiteralPath $checksum.zipPath -DestinationPath $ExtractRoot -Force

    $manifestPath = Join-Path $ExtractRoot "package-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Package manifest is missing."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -lt 5) {
        throw "Package manifest schema '$($manifest.schemaVersion)' predates the beta manual-QA contract."
    }
    if ([string]$manifest.product -ne "Dragon DiskForge") {
        throw "Unexpected package product '$($manifest.product)'."
    }
    if ([string]$manifest.version -ne $Version) {
        throw "Expected beta candidate version '$Version'; package reports '$($manifest.version)'."
    }
    if ([string]$manifest.architecture -ne "x64") {
        throw "Expected x64 beta candidate; package reports '$($manifest.architecture)'."
    }

    if ([string]$manifest.runtimeDeployment.dotNet -ne "self-contained" -or
        [string]$manifest.runtimeDeployment.windowsAppSdk -ne "self-contained" -or
        [string]$manifest.runtimeDeployment.visualCpp -ne "app-local") {
        throw "Package runtime deployment is not complete (.NET, Windows App SDK, Visual C++)."
    }

    $entryPointRelative = ([string]$manifest.entryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $entryPoint = Join-Path $ExtractRoot $entryPointRelative
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        throw "Package desktop entry point is missing: $entryPointRelative"
    }
    $entryPointHash = (Get-FileHash -LiteralPath $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($entryPointHash -ne ([string]$manifest.entryPointSha256).ToLowerInvariant()) {
        throw "Package desktop entry-point SHA-256 does not match the manifest."
    }

    $qaRelative = ([string]$manifest.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $qaTool = Join-Path $ExtractRoot $qaRelative
    if (-not (Test-Path -LiteralPath $qaTool -PathType Leaf)) {
        throw "Package beta manual-QA tool is missing: $qaRelative"
    }
    $qaHash = (Get-FileHash -LiteralPath $qaTool -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($qaHash -ne ([string]$manifest.betaManualQaEntryPointSha256).ToLowerInvariant()) {
        throw "Package beta manual-QA tool SHA-256 does not match the manifest."
    }

    $requiredRuntimeFiles = @(
        "hostfxr.dll",
        "hostpolicy.dll",
        "coreclr.dll",
        "clrjit.dll",
        "vcruntime140.dll",
        "msvcp140.dll",
        "Microsoft.WindowsAppRuntime.dll"
    )
    foreach ($runtimeFile in $requiredRuntimeFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $ExtractRoot $runtimeFile) -PathType Leaf)) {
            throw "Package is not runtime-complete; missing '$runtimeFile'."
        }
    }

    $entryMatches = @(Get-ChildItem -LiteralPath $ExtractRoot -Recurse -File -Filter "DragonDiskForge.App.exe")
    if ($entryMatches.Count -ne 1) {
        throw "Expected exactly one DragonDiskForge.App.exe in the package; found $($entryMatches.Count)."
    }
    $pdbFiles = @(Get-ChildItem -LiteralPath $ExtractRoot -Recurse -File -Filter "*.pdb")
    if ($pdbFiles.Count -ne 0) {
        throw "Candidate package unexpectedly contains $($pdbFiles.Count) PDB file(s)."
    }

    return [pscustomobject]@{
        packagePath = $checksum.zipPath
        checksumFile = $checksum.sidecarPath
        packageSha256 = $checksum.sha256
        version = [string]$manifest.version
        architecture = [string]$manifest.architecture
        manifestSchemaVersion = [int]$manifest.schemaVersion
        entryPoint = $entryPoint
        entryPointRelative = $entryPointRelative
        entryPointSha256 = $entryPointHash
        qaTool = $qaTool
        qaToolRelative = $qaRelative
        qaToolSha256 = $qaHash
        extractRoot = [System.IO.Path]::GetFullPath($ExtractRoot)
    }
}

function Get-DefaultWorkspacePath {
    param([Parameter(Mandatory = $true)][string]$PackageSha256)

    $prefix = $PackageSha256.Substring(0, 12)
    return [System.IO.Path]::GetFullPath((Join-Path "artifacts/manual-qa" "session-$prefix"))
}

function Get-StartupFailureLog {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        return ""
    }

    try {
        $path = Join-Path $env:LOCALAPPDATA "DragonDiskForge\Logs\startup-failure.log"
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return (Get-Content -LiteralPath $path -Raw)
        }
    }
    catch {
    }
    return ""
}

function Start-CandidateApp {
    param(
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $true)][int]$ProbeSeconds
    )

    if ($ProbeSeconds -lt 1 -or $ProbeSeconds -gt 30) {
        throw "Launch probe duration must be between 1 and 30 seconds."
    }

    $process = Start-Process -FilePath $Identity.entryPoint -WorkingDirectory $Identity.extractRoot -PassThru
    Start-Sleep -Seconds $ProbeSeconds
    $process.Refresh()
    if ($process.HasExited) {
        $log = Get-StartupFailureLog
        if (-not [string]::IsNullOrWhiteSpace($log)) {
            Write-Host "--- startup-failure.log ---"
            Write-Host $log
        }
        throw "DragonDiskForge.App.exe exited during the local interactive launch preflight with code $($process.ExitCode)."
    }

    return $process
}

function Invoke-ManualEvidenceInitialization {
    param(
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $true)][string]$EvidencePath
    )

    & $Identity.qaTool -Mode new `
        -PackagePath $Identity.packagePath `
        -ChecksumFile $Identity.checksumFile `
        -EvidencePath $EvidencePath `
        -ExpectedVersion $Identity.version
    if ($LASTEXITCODE -ne 0) {
        throw "Packaged beta manual-QA tool failed to initialize evidence (exit code $LASTEXITCODE)."
    }
}

function Write-NextSteps {
    param(
        [Parameter(Mandatory = $true)]$Identity,
        [Parameter(Mandatory = $true)][string]$EvidencePath,
        [Parameter(Mandatory = $true)][string]$DropTarget
    )

    Write-Host ""
    Write-Host "Prepared exact-candidate interactive QA session."
    Write-Host "Candidate: $($Identity.version) / $($Identity.architecture)"
    Write-Host "Package SHA-256: $($Identity.packageSha256)"
    Write-Host "Evidence: $EvidencePath"
    Write-Host "Explorer drop target: $DropTarget"
    Write-Host ""
    Write-Host "IMPORTANT: the launch probe only proves that the process remained alive locally."
    Write-Host "It does NOT mark desktop.clean-launch or any UAC/Explorer gate as passed."
    Write-Host "After physically performing each checklist observation, record it with the packaged tool and -HumanConfirmed."
    Write-Host ""
    Write-Host "List checks:"
    Write-Host "  & '$($Identity.qaTool)' -Mode list -EvidencePath '$EvidencePath'"
    Write-Host ""
    Write-Host "Example after visibly confirming the clean desktop launch:"
    Write-Host "  & '$($Identity.qaTool)' -Mode record -EvidencePath '$EvidencePath' -Check desktop.clean-launch -Result pass -HumanConfirmed -Note 'Visual clean-desktop launch confirmed.'"
    Write-Host ""
    Write-Host "Final verification:"
    Write-Host "  & '$($Identity.qaTool)' -Mode verify -PackagePath '$($Identity.packagePath)' -ChecksumFile '$($Identity.checksumFile)' -EvidencePath '$EvidencePath'"
}

function Invoke-Prepare {
    if ([string]::IsNullOrWhiteSpace($PackagePath)) {
        throw "-PackagePath is required for -Mode prepare."
    }

    $environment = Get-SessionEnvironment
    Assert-InteractiveUnelevated -EnvironmentInfo $environment -Context "Beta QA session preparation"

    $checksum = Resolve-ChecksumSidecar -ZipPath $PackagePath -SidecarPath $ChecksumFile
    $workspace = $WorkspacePath
    if ([string]::IsNullOrWhiteSpace($workspace)) {
        $workspace = Get-DefaultWorkspacePath -PackageSha256 $checksum.sha256
    }
    $workspace = [System.IO.Path]::GetFullPath($workspace)

    if (Test-Path -LiteralPath $workspace) {
        if (-not $Force) {
            throw "QA workspace already exists: $workspace. Use -Force to replace it or -Mode status to inspect it."
        }
        Remove-Item -LiteralPath $workspace -Recurse -Force
    }

    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    $extractRoot = Join-Path $workspace "package"
    $dropTarget = Join-Path $workspace "explorer-drop-target"
    New-Item -ItemType Directory -Path $dropTarget -Force | Out-Null

    $identity = Get-CandidateIdentity -ZipPath $checksum.zipPath -SidecarPath $checksum.sidecarPath -ExtractRoot $extractRoot -Version $ExpectedVersion
    $evidencePath = Join-Path $workspace $Script:EvidenceFileName

    Invoke-ManualEvidenceInitialization -Identity $identity -EvidencePath $evidencePath
    $process = Start-CandidateApp -Identity $identity -ProbeSeconds $LaunchProbeSeconds

    try {
        Start-Process -FilePath "explorer.exe" -ArgumentList @($dropTarget) | Out-Null
    }
    catch {
        Write-Warning "Could not open the Explorer drop-target folder automatically: $($_.Exception.Message)"
    }

    $session = [pscustomobject]@{
        schemaVersion = $Script:SessionSchemaVersion
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
        package = [pscustomobject]@{
            path = $identity.packagePath
            checksumFile = $identity.checksumFile
            sha256 = $identity.packageSha256
            version = $identity.version
            architecture = $identity.architecture
            entryPointSha256 = $identity.entryPointSha256
            qaToolSha256 = $identity.qaToolSha256
        }
        environment = $environment
        workspace = $workspace
        extractRoot = $identity.extractRoot
        evidencePath = [System.IO.Path]::GetFullPath($evidencePath)
        dropTarget = [System.IO.Path]::GetFullPath($dropTarget)
        appProcessId = $process.Id
        appPath = $identity.entryPoint
        launchProbeSeconds = $LaunchProbeSeconds
        launchProbePassed = $true
        manualGatePassed = $false
        note = "Local liveness preflight only; human-confirmed package-bound observations remain required."
    }

    $sessionPath = Save-JsonAtomic -Value $session -Path (Join-Path $workspace $Script:SessionFileName)
    Write-Host "Session metadata: $sessionPath"
    Write-NextSteps -Identity $identity -EvidencePath $evidencePath -DropTarget $dropTarget
}

function Load-Session {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sessionPath = $Path
    if (Test-Path -LiteralPath $Path -PathType Container) {
        $sessionPath = Join-Path $Path $Script:SessionFileName
    }
    $sessionPath = (Resolve-Path -LiteralPath $sessionPath).Path
    $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    if ([int]$session.schemaVersion -ne $Script:SessionSchemaVersion) {
        throw "Unsupported beta QA session schema '$($session.schemaVersion)'."
    }
    return [pscustomobject]@{ path = $sessionPath; value = $session }
}

function Resolve-StatusWorkspace {
    if (-not [string]::IsNullOrWhiteSpace($WorkspacePath)) {
        return [System.IO.Path]::GetFullPath($WorkspacePath)
    }
    throw "-WorkspacePath is required for -Mode status or cleanup."
}

function Invoke-Status {
    $workspace = Resolve-StatusWorkspace
    $loaded = Load-Session -Path $workspace
    $session = $loaded.value

    Write-Host "Session: $($loaded.path)"
    Write-Host "Candidate: $($session.package.version) / $($session.package.architecture)"
    Write-Host "Package SHA-256: $($session.package.sha256)"
    Write-Host "Launch preflight: $($session.launchProbePassed) (manual gate remains false until package-bound human evidence verifies)"
    Write-Host "Evidence: $($session.evidencePath)"
    Write-Host "Drop target: $($session.dropTarget)"

    $qaTool = Join-Path ([string]$session.extractRoot) "tools\beta-manual-qa.ps1"
    if (-not (Test-Path -LiteralPath $qaTool -PathType Leaf)) {
        throw "Extracted packaged beta manual-QA tool is missing: $qaTool"
    }
    if (-not (Test-Path -LiteralPath ([string]$session.evidencePath) -PathType Leaf)) {
        throw "Manual-QA evidence is missing: $($session.evidencePath)"
    }

    & $qaTool -Mode list -EvidencePath ([string]$session.evidencePath)
    if ($LASTEXITCODE -ne 0) {
        throw "Packaged beta manual-QA tool failed to list evidence (exit code $LASTEXITCODE)."
    }
}

function Invoke-Cleanup {
    $workspace = Resolve-StatusWorkspace
    $loaded = Load-Session -Path $workspace
    $session = $loaded.value

    $processId = [int]$session.appProcessId
    if ($processId -gt 0) {
        try {
            $process = Get-Process -Id $processId -ErrorAction Stop
            $actualPath = ""
            try { $actualPath = [string]$process.Path } catch { $actualPath = "" }
            if (-not [string]::IsNullOrWhiteSpace($actualPath) -and
                [string]::Equals([System.IO.Path]::GetFullPath($actualPath), [System.IO.Path]::GetFullPath([string]$session.appPath), [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $processId -Force
            }
            else {
                Write-Warning "Process id $processId no longer resolves to the recorded Dragon DiskForge executable; it was not stopped."
            }
        }
        catch {
        }
    }

    Remove-Item -LiteralPath $workspace -Recurse -Force
    Write-Host "Removed beta QA workspace: $workspace"
}

function New-SelfTestCandidate {
    param([Parameter(Mandatory = $true)][string]$Root)

    $packageRoot = Join-Path $Root "package-source"
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $packageRoot "tools") -Force | Out-Null

    $entryPoint = Join-Path $packageRoot "DragonDiskForge.App.exe"
    $qaTool = Join-Path $packageRoot "tools\beta-manual-qa.ps1"
    Write-Utf8NoBom -Path $entryPoint -Text "self-test-app"
    Write-Utf8NoBom -Path $qaTool -Text "Write-Host 'self-test-qa'"

    foreach ($runtimeFile in @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll", "vcruntime140.dll", "msvcp140.dll", "Microsoft.WindowsAppRuntime.dll")) {
        Write-Utf8NoBom -Path (Join-Path $packageRoot $runtimeFile) -Text "self-test-runtime-$runtimeFile"
    }

    $entryHash = (Get-FileHash -LiteralPath $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    $qaHash = (Get-FileHash -LiteralPath $qaTool -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{
        schemaVersion = 5
        product = "Dragon DiskForge"
        version = "0.5.0-beta.1"
        architecture = "x64"
        entryPoint = "DragonDiskForge.App.exe"
        entryPointSha256 = $entryHash
        betaManualQaEntryPoint = "tools/beta-manual-qa.ps1"
        betaManualQaEntryPointSha256 = $qaHash
        runtimeDeployment = [ordered]@{
            dotNet = "self-contained"
            windowsAppSdk = "self-contained"
            visualCpp = "app-local"
        }
    }
    Write-Utf8NoBom -Path (Join-Path $packageRoot "package-manifest.json") -Text (($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine)

    $zip = Join-Path $Root "DragonDiskForge-win-x64.zip"
    Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $sidecar = "$zip.sha256"
    Write-Utf8NoBom -Path $sidecar -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($zip), [Environment]::NewLine)
    return [pscustomobject]@{ zip = $zip; sidecar = $sidecar; sha256 = $hash }
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-session-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $candidate = New-SelfTestCandidate -Root $tempRoot
        $extract = Join-Path $tempRoot "extract"
        $identity = Get-CandidateIdentity -ZipPath $candidate.zip -SidecarPath $candidate.sidecar -ExtractRoot $extract -Version "0.5.0-beta.1"
        if ($identity.packageSha256 -ne $candidate.sha256) { throw "Self-test failed: package hash mismatch." }
        if ($identity.version -ne "0.5.0-beta.1" -or $identity.architecture -ne "x64") { throw "Self-test failed: candidate identity mismatch." }
        if (-not (Test-Path -LiteralPath $identity.entryPoint -PathType Leaf)) { throw "Self-test failed: entry point was not extracted." }
        if (-not (Test-Path -LiteralPath $identity.qaTool -PathType Leaf)) { throw "Self-test failed: QA tool was not extracted." }

        $badSidecar = Join-Path $tempRoot "bad.sha256"
        Write-Utf8NoBom -Path $badSidecar -Text (("0" * 64) + "  DragonDiskForge-win-x64.zip" + [Environment]::NewLine)
        $failedClosed = $false
        try {
            Resolve-ChecksumSidecar -ZipPath $candidate.zip -SidecarPath $badSidecar | Out-Null
        }
        catch {
            $failedClosed = $true
        }
        if (-not $failedClosed) { throw "Self-test failed: checksum mismatch did not fail closed." }

        $sessionPath = Join-Path $tempRoot "session.json"
        $saved = Save-JsonAtomic -Value ([pscustomobject]@{ schemaVersion = 1; packageSha256 = $candidate.sha256 }) -Path $sessionPath
        $roundTrip = Get-Content -LiteralPath $saved -Raw | ConvertFrom-Json
        if ([int]$roundTrip.schemaVersion -ne 1 -or [string]$roundTrip.packageSha256 -ne $candidate.sha256) {
            throw "Self-test failed: session metadata round-trip mismatch."
        }

        $fakeEnvironment = [pscustomobject]@{
            userInteractive = $false
            sessionId = 0
            processElevated = $false
            uacEnabled = $true
        }
        $environmentRejected = $false
        try { Assert-InteractiveUnelevated -EnvironmentInfo $fakeEnvironment -Context "Self-test" } catch { $environmentRejected = $true }
        if (-not $environmentRejected) { throw "Self-test failed: non-interactive environment did not fail closed." }

        Write-Host "Dragon DiskForge beta QA session helper self-test passed."
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    "prepare" { Invoke-Prepare; break }
    "status" { Invoke-Status; break }
    "cleanup" { Invoke-Cleanup; break }
    "self-test" { Invoke-SelfTest; break }
    default { throw "Unsupported mode '$Mode'." }
}
