[CmdletBinding()]
param(
    [string]$SourceDirectory = "src/DragonDiskForge.App/bin/x64/Release",
    [string]$OutputDirectory = "artifacts/windows"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$source = (Resolve-Path $SourceDirectory).Path
$output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
$stage = Join-Path $output "DragonDiskForge-win-x64"
$zip = Join-Path $output "DragonDiskForge-win-x64.zip"
$checksum = "$zip.sha256"

$executables = @(Get-ChildItem -Path $source -Recurse -File -Filter "DragonDiskForge.App.exe")
if ($executables.Count -ne 1) {
    throw "Expected exactly one DragonDiskForge.App.exe below '$source', found $($executables.Count)."
}

$appDirectory = $executables[0].Directory.FullName
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

$manifest = [ordered]@{
    schemaVersion = 1
    product = "Dragon DiskForge"
    architecture = "x64"
    entryPoint = "DragonDiskForge.App.exe"
    debugSymbolsIncluded = $false
    fileCount = $files.Count
}
$manifest | ConvertTo-Json | Set-Content -Path (Join-Path $stage "package-manifest.json") -Encoding utf8NoBOM

if (Test-Path $zip) {
    Remove-Item $zip -Force
}
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$line = "$hash  $([System.IO.Path]::GetFileName($zip))"
Set-Content -Path $checksum -Value $line -Encoding ascii

Write-Host "Packaged $($files.Count) application files."
Write-Host "ZIP: $zip"
Write-Host "SHA-256: $hash"
