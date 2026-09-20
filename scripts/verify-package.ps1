[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/windows",
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepositoryVersion {
    param([string]$Override)
    if (-not [string]::IsNullOrWhiteSpace($Override)) { return $Override.Trim() }
    [xml]$props = Get-Content -Path (Join-Path (Get-Location) "Directory.Build.props") -Raw
    $group = @($props.Project.PropertyGroup) | Select-Object -First 1
    $prefix = [string]$group.DragonDiskForgeVersionPrefix
    $suffix = [string]$group.DragonDiskForgeVersionSuffix
    if ([string]::IsNullOrWhiteSpace($prefix)) { throw "DragonDiskForgeVersionPrefix is missing from Directory.Build.props." }
    if ([string]::IsNullOrWhiteSpace($suffix)) { return $prefix.Trim() }
    return "$($prefix.Trim())-$($suffix.Trim())"
}

$expected = Get-RepositoryVersion -Override $ExpectedVersion
$output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
$zip = Join-Path $output "DragonDiskForge-win-x64.zip"
$checksum = "$zip.sha256"
if (-not (Test-Path $zip -PathType Leaf)) { throw "Package ZIP was not found: $zip" }
if (-not (Test-Path $checksum -PathType Leaf)) { throw "Package checksum was not found: $checksum" }

$checksumLine = (Get-Content -Path $checksum -Raw).Trim()
$declaredHash = ($checksumLine -split '\s+')[0].ToLowerInvariant()
$actualHash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($declaredHash -ne $actualHash) { throw "Package SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'." }

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-package-verify-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    Expand-Archive -Path $zip -DestinationPath $tempRoot -Force
    $manifestPath = Join-Path $tempRoot "package-manifest.json"
    if (-not (Test-Path $manifestPath -PathType Leaf)) { throw "Package manifest is missing." }
    $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -lt 6) { throw "Package manifest schema is too old for the packaged UAC-witness-enabled beta contract: $($manifest.schemaVersion)." }
    if ([string]$manifest.product -ne "Dragon DiskForge") { throw "Unexpected package product '$($manifest.product)'." }
    if ([string]$manifest.version -ne $expected) { throw "Package manifest version '$($manifest.version)' does not match expected '$expected'." }
    if ([string]$manifest.architecture -ne "x64") { throw "Unexpected package architecture '$($manifest.architecture)'." }
    if ([bool]$manifest.debugSymbolsIncluded) { throw "Package manifest claims debug symbols are included." }

    if ([string]$manifest.runtimeDeployment.dotNet -ne "self-contained") { throw "Package manifest does not declare a self-contained .NET runtime." }
    if ([string]$manifest.runtimeDeployment.windowsAppSdk -ne "self-contained") { throw "Package manifest does not declare a self-contained Windows App SDK runtime." }
    if ([string]$manifest.runtimeDeployment.visualCpp -ne "app-local") { throw "Package manifest does not declare an app-local Visual C++ runtime." }

    $entryPoint = Join-Path $tempRoot ([string]$manifest.entryPoint)
    $cliEntryPoint = Join-Path $tempRoot ([string]$manifest.cliEntryPoint)
    $shellEntryPoint = Join-Path $tempRoot ([string]$manifest.shellIntegrationEntryPoint)
    $betaManualQaEntryPoint = Join-Path $tempRoot ([string]$manifest.betaManualQaEntryPoint)
    $betaUacWitnessEntryPoint = Join-Path $tempRoot ([string]$manifest.betaUacWitnessEntryPoint)
    $betaUacPairVerifierEntryPoint = Join-Path $tempRoot ([string]$manifest.betaUacPairVerifierEntryPoint)
    foreach ($required in @($entryPoint, $cliEntryPoint, $shellEntryPoint, $betaManualQaEntryPoint, $betaUacWitnessEntryPoint, $betaUacPairVerifierEntryPoint)) {
        if (-not (Test-Path $required -PathType Leaf)) { throw "Manifest entry point is missing: $required" }
    }

    foreach ($runtimeFileName in @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll", "vcruntime140.dll", "msvcp140.dll")) {
        $runtimePath = Join-Path $tempRoot $runtimeFileName
        if (-not (Test-Path $runtimePath -PathType Leaf)) {
            throw "Verified package is not runtime-complete; missing '$runtimeFileName'."
        }
    }

    if (@(Get-ChildItem -Path $tempRoot -Recurse -File -Filter "*.pdb").Count -ne 0) { throw "Verified package contains debug symbol files." }
    if (@(Get-ChildItem -Path $tempRoot -Recurse -File | Where-Object { $_.FullName -match "[\\/]tests?[\\/]" -or $_.Name -match "(?i)(SmokeTests|IntegrationTests)" }).Count -ne 0) {
        throw "Verified package contains test-only files."
    }

    $iconPath = Join-Path $tempRoot ([string]$manifest.icon)
    if (-not (Test-Path $iconPath -PathType Leaf)) { throw "Manifest icon is missing: $($manifest.icon)" }

    $entryHash = (Get-FileHash -Path $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    $cliHash = (Get-FileHash -Path $cliEntryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    $shellHash = (Get-FileHash -Path $shellEntryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    $betaManualQaHash = (Get-FileHash -Path $betaManualQaEntryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    $betaUacWitnessHash = (Get-FileHash -Path $betaUacWitnessEntryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    $betaUacPairVerifierHash = (Get-FileHash -Path $betaUacPairVerifierEntryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($entryHash -ne ([string]$manifest.entryPointSha256).ToLowerInvariant()) { throw "Entry-point SHA-256 does not match the package manifest." }
    if ($cliHash -ne ([string]$manifest.cliEntryPointSha256).ToLowerInvariant()) { throw "CLI SHA-256 does not match the package manifest." }
    if ($shellHash -ne ([string]$manifest.shellIntegrationEntryPointSha256).ToLowerInvariant()) { throw "Shell-helper SHA-256 does not match the package manifest." }
    if ($betaManualQaHash -ne ([string]$manifest.betaManualQaEntryPointSha256).ToLowerInvariant()) { throw "Beta manual-QA tool SHA-256 does not match the package manifest." }
    if ($betaUacWitnessHash -ne ([string]$manifest.betaUacWitnessEntryPointSha256).ToLowerInvariant()) { throw "Beta UAC witness tool SHA-256 does not match the package manifest." }
    if ($betaUacPairVerifierHash -ne ([string]$manifest.betaUacPairVerifierEntryPointSha256).ToLowerInvariant()) { throw "Beta UAC pair verifier SHA-256 does not match the package manifest." }

    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $betaManualQaEntryPoint -Mode self-test
    if ($LASTEXITCODE -ne 0) { throw "Packaged beta manual-QA evidence tool failed its Windows PowerShell self-test with exit code $LASTEXITCODE." }

    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $betaUacWitnessEntryPoint -Mode self-test
    if ($LASTEXITCODE -ne 0) { throw "Packaged beta UAC witness tool failed its Windows PowerShell self-test with exit code $LASTEXITCODE." }

    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $betaUacPairVerifierEntryPoint -Mode self-test
    if ($LASTEXITCODE -ne 0) { throw "Packaged beta UAC pair verifier failed its Windows PowerShell self-test with exit code $LASTEXITCODE." }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($entryPoint)
    $productVersion = [string]$versionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($productVersion) -or -not $productVersion.StartsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Verified application ProductVersion '$productVersion' does not match expected '$expected'."
    }
    if ([string]$manifest.productVersion -ne $productVersion) { throw "Manifest ProductVersion '$($manifest.productVersion)' differs from executable ProductVersion '$productVersion'." }

    $providerJson = (& $cliEntryPoint formats --format json) | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Packaged CLI formats smoke test failed with exit code $LASTEXITCODE." }
    $providers = @($providerJson | ConvertFrom-Json)
    if ($providers.Count -lt 12) { throw "Packaged CLI returned only $($providers.Count) built-in providers; expected at least 12." }

    & $shellEntryPoint --help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Packaged shell helper failed its launch smoke test with exit code $LASTEXITCODE." }

    Write-Host "Verified runtime-complete Windows x64 package."
    Write-Host "Version: $expected"
    Write-Host "Files: $((Get-ChildItem -Path $tempRoot -Recurse -File).Count)"
    Write-Host "ZIP SHA-256: $actualHash"
    Write-Host "EXE SHA-256: $entryHash"
    Write-Host "CLI SHA-256: $cliHash"
    Write-Host "Shell helper SHA-256: $shellHash"
    Write-Host "Beta manual-QA tool SHA-256: $betaManualQaHash"
    Write-Host "Beta UAC witness tool SHA-256: $betaUacWitnessHash"
    Write-Host "Beta UAC pair verifier SHA-256: $betaUacPairVerifierHash"
    Write-Host "CLI providers: $($providers.Count)"
}
finally {
    if (Test-Path $tempRoot) { Remove-Item $tempRoot -Recurse -Force }
}
