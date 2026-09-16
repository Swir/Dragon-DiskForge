[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/windows",
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepositoryVersion {
    param([string]$Override)

    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        return $Override.Trim()
    }

    [xml]$props = Get-Content -Path (Join-Path (Get-Location) "Directory.Build.props") -Raw
    $group = @($props.Project.PropertyGroup) | Select-Object -First 1
    $prefix = [string]$group.DragonDiskForgeVersionPrefix
    $suffix = [string]$group.DragonDiskForgeVersionSuffix
    if ([string]::IsNullOrWhiteSpace($prefix)) {
        throw "DragonDiskForgeVersionPrefix is missing from Directory.Build.props."
    }

    if ([string]::IsNullOrWhiteSpace($suffix)) {
        return $prefix.Trim()
    }

    return "$($prefix.Trim())-$($suffix.Trim())"
}

$expected = Get-RepositoryVersion -Override $ExpectedVersion
$output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
$zip = Join-Path $output "DragonDiskForge-win-x64.zip"
$checksum = "$zip.sha256"

if (-not (Test-Path $zip -PathType Leaf)) {
    throw "Package ZIP was not found: $zip"
}
if (-not (Test-Path $checksum -PathType Leaf)) {
    throw "Package checksum was not found: $checksum"
}

$checksumLine = (Get-Content -Path $checksum -Raw).Trim()
$declaredHash = ($checksumLine -split '\s+')[0].ToLowerInvariant()
$actualHash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($declaredHash -ne $actualHash) {
    throw "Package SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'."
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-package-verify-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    Expand-Archive -Path $zip -DestinationPath $tempRoot -Force

    $manifestPath = Join-Path $tempRoot "package-manifest.json"
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        throw "Package manifest is missing."
    }

    $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -lt 2) {
        throw "Package manifest schema is too old: $($manifest.schemaVersion)."
    }
    if ([string]$manifest.product -ne "Dragon DiskForge") {
        throw "Unexpected package product '$($manifest.product)'."
    }
    if ([string]$manifest.version -ne $expected) {
        throw "Package manifest version '$($manifest.version)' does not match expected '$expected'."
    }
    if ([string]$manifest.architecture -ne "x64") {
        throw "Unexpected package architecture '$($manifest.architecture)'."
    }
    if ([bool]$manifest.debugSymbolsIncluded) {
        throw "Package manifest claims debug symbols are included."
    }

    $entryPoint = Join-Path $tempRoot ([string]$manifest.entryPoint)
    if (-not (Test-Path $entryPoint -PathType Leaf)) {
        throw "Manifest entry point is missing: $($manifest.entryPoint)"
    }

    $executables = @(Get-ChildItem -Path $tempRoot -Recurse -File -Filter "DragonDiskForge.App.exe")
    if ($executables.Count -ne 1) {
        throw "Expected exactly one packaged DragonDiskForge.App.exe, found $($executables.Count)."
    }

    $debugFiles = @(Get-ChildItem -Path $tempRoot -Recurse -File -Filter "*.pdb")
    if ($debugFiles.Count -ne 0) {
        throw "Verified package contains debug symbol files."
    }

    $testFiles = @(Get-ChildItem -Path $tempRoot -Recurse -File | Where-Object {
        $_.FullName -match "[\\/]tests?[\\/]" -or $_.Name -match "(?i)(SmokeTests|IntegrationTests)"
    })
    if ($testFiles.Count -ne 0) {
        throw "Verified package contains test-only files."
    }

    $iconPath = Join-Path $tempRoot ([string]$manifest.icon)
    if (-not (Test-Path $iconPath -PathType Leaf)) {
        throw "Manifest icon is missing: $($manifest.icon)"
    }

    $entryHash = (Get-FileHash -Path $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($entryHash -ne ([string]$manifest.entryPointSha256).ToLowerInvariant()) {
        throw "Entry-point SHA-256 does not match the package manifest."
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($entryPoint)
    $productVersion = [string]$versionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($productVersion) -or -not $productVersion.StartsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Verified application ProductVersion '$productVersion' does not match expected '$expected'."
    }
    if ([string]$manifest.productVersion -ne $productVersion) {
        throw "Manifest ProductVersion '$($manifest.productVersion)' differs from executable ProductVersion '$productVersion'."
    }

    Write-Host "Verified clean Windows x64 package."
    Write-Host "Version: $expected"
    Write-Host "Files: $((Get-ChildItem -Path $tempRoot -Recurse -File).Count)"
    Write-Host "ZIP SHA-256: $actualHash"
    Write-Host "EXE SHA-256: $entryHash"
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force
    }
}
