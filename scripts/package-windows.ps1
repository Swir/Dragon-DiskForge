[CmdletBinding()]
param(
    [string]$SourceDirectory = "src/DragonDiskForge.App/bin/x64/Release",
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

    $propsPath = Join-Path (Get-Location) "Directory.Build.props"
    if (-not (Test-Path $propsPath -PathType Leaf)) {
        throw "Directory.Build.props was not found; cannot determine the package version."
    }

    [xml]$props = Get-Content -Path $propsPath -Raw
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
$source = (Resolve-Path $SourceDirectory).Path
$output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
$stage = Join-Path $output "DragonDiskForge-win-x64"
$zip = Join-Path $output "DragonDiskForge-win-x64.zip"
$checksum = "$zip.sha256"

Write-Host "Preparing clean Windows x64 package for version $expected."
Write-Host "Build search root: $source"

$executables = @(Get-ChildItem -Path $source -Recurse -File -Filter "DragonDiskForge.App.exe")
if ($executables.Count -ne 1) {
    throw "Expected exactly one DragonDiskForge.App.exe below '$source', found $($executables.Count)."
}

$appDirectory = $executables[0].Directory.FullName
Write-Host "Application output directory: $appDirectory"

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$files = @(Get-ChildItem -Path $appDirectory -Recurse -File | Where-Object {
    $_.Extension -ine ".pdb"
})
if ($files.Count -eq 0) {
    throw "No application files were found for packaging."
}

foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($appDirectory, $file.FullName)
    $destination = Join-Path $stage $relative
    $destinationDirectory = Split-Path $destination -Parent
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item $file.FullName $destination -Force
}

$entryPoint = Join-Path $stage "DragonDiskForge.App.exe"
if (-not (Test-Path $entryPoint -PathType Leaf)) {
    throw "Packaged application is missing DragonDiskForge.App.exe at the package root."
}

$icon = Join-Path $stage "DragonDiskForge.ico"
if (-not (Test-Path $icon -PathType Leaf)) {
    $repositoryIcon = Join-Path (Get-Location) "src/DragonDiskForge.App/Assets/DragonDiskForge.ico"
    if (-not (Test-Path $repositoryIcon -PathType Leaf)) {
        throw "DragonDiskForge.ico is absent from both the application output and the canonical repository asset path."
    }

    Copy-Item $repositoryIcon $icon -Force
    Write-Host "Added canonical DragonDiskForge.ico to package root."
}

$debugFiles = @(Get-ChildItem -Path $stage -Recurse -File -Filter "*.pdb")
if ($debugFiles.Count -ne 0) {
    throw "Public-package staging contains debug symbol files."
}

$testFiles = @(Get-ChildItem -Path $stage -Recurse -File | Where-Object {
    $_.FullName -match "[\\/]tests?[\\/]" -or $_.Name -match "(?i)(SmokeTests|IntegrationTests)"
})
if ($testFiles.Count -ne 0) {
    throw "Public-package staging contains test-only files."
}

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($entryPoint)
$productVersion = [string]$versionInfo.ProductVersion
$fileVersion = [string]$versionInfo.FileVersion
Write-Host "Executable ProductVersion: $productVersion"
Write-Host "Executable FileVersion: $fileVersion"
if ([string]::IsNullOrWhiteSpace($productVersion) -or -not $productVersion.StartsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Packaged application ProductVersion '$productVersion' does not match expected version '$expected'."
}

$entryPointSha256 = (Get-FileHash -Path $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
$packageFilesBeforeManifest = @(Get-ChildItem -Path $stage -Recurse -File)
$manifest = [ordered]@{
    schemaVersion = 2
    product = "Dragon DiskForge"
    version = $expected
    productVersion = $productVersion
    fileVersion = $fileVersion
    architecture = "x64"
    entryPoint = "DragonDiskForge.App.exe"
    icon = "DragonDiskForge.ico"
    entryPointSha256 = $entryPointSha256
    debugSymbolsIncluded = $false
    fileCount = $packageFilesBeforeManifest.Count + 1
}
$manifest | ConvertTo-Json | Set-Content -Path (Join-Path $stage "package-manifest.json") -Encoding utf8NoBOM

if (Test-Path $zip) {
    Remove-Item $zip -Force
}
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$line = "$hash  $([System.IO.Path]::GetFileName($zip))"
Set-Content -Path $checksum -Value $line -Encoding ascii

Write-Host "Packaged $((Get-ChildItem -Path $stage -Recurse -File).Count) files."
Write-Host "Version: $expected"
Write-Host "ProductVersion: $productVersion"
Write-Host "ZIP: $zip"
Write-Host "SHA-256: $hash"
