[CmdletBinding()]
param(
    [string]$SourceDirectory = "src/DragonDiskForge.App/bin/x64/Release",
    [string]$CliProject = "src/DragonDiskForge.Cli/DragonDiskForge.Cli.csproj",
    [string]$ShellProject = "src/DragonDiskForge.Shell/DragonDiskForge.Shell.csproj",
    [string]$OutputDirectory = "artifacts/windows",
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-RepositoryVersion {
    param([string]$Override)

    if (-not [string]::IsNullOrWhiteSpace($Override)) { return $Override.Trim() }
    $propsPath = Join-Path (Get-Location) "Directory.Build.props"
    if (-not (Test-Path $propsPath -PathType Leaf)) { throw "Directory.Build.props was not found; cannot determine the package version." }
    [xml]$props = Get-Content -Path $propsPath -Raw
    $group = @($props.Project.PropertyGroup) | Select-Object -First 1
    $prefix = [string]$group.DragonDiskForgeVersionPrefix
    $suffix = [string]$group.DragonDiskForgeVersionSuffix
    if ([string]::IsNullOrWhiteSpace($prefix)) { throw "DragonDiskForgeVersionPrefix is missing from Directory.Build.props." }
    if ([string]::IsNullOrWhiteSpace($suffix)) { return $prefix.Trim() }
    return "$($prefix.Trim())-$($suffix.Trim())"
}

$expected = Get-RepositoryVersion -Override $ExpectedVersion
$source = (Resolve-Path $SourceDirectory).Path
$cliProjectPath = (Resolve-Path $CliProject).Path
$shellProjectPath = (Resolve-Path $ShellProject).Path
$output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
$stage = Join-Path $output "DragonDiskForge-win-x64"
$zip = Join-Path $output "DragonDiskForge-win-x64.zip"
$checksum = "$zip.sha256"

Write-Host "Preparing clean Windows x64 package for version $expected."
$executables = @(Get-ChildItem -Path $source -Recurse -File -Filter "DragonDiskForge.App.exe")
if ($executables.Count -ne 1) { throw "Expected exactly one DragonDiskForge.App.exe below '$source', found $($executables.Count)." }
$appDirectory = $executables[0].Directory.FullName

Write-Host "Publishing self-contained Dragon DiskForge CLI."
& dotnet publish $cliProjectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Self-contained CLI publish failed with exit code $LASTEXITCODE." }
$cliProjectDirectory = Split-Path $cliProjectPath -Parent
$cliExecutable = Join-Path $cliProjectDirectory "bin/Release/net10.0/win-x64/publish/dragon-diskforge.exe"
if (-not (Test-Path $cliExecutable -PathType Leaf)) { throw "Published CLI executable was not found: $cliExecutable" }

Write-Host "Publishing self-contained Dragon DiskForge shell helper."
& dotnet publish $shellProjectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Self-contained shell-helper publish failed with exit code $LASTEXITCODE." }
$shellProjectDirectory = Split-Path $shellProjectPath -Parent
$shellExecutable = Join-Path $shellProjectDirectory "bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/dragon-diskforge-shell.exe"
if (-not (Test-Path $shellExecutable -PathType Leaf)) { throw "Published shell helper was not found: $shellExecutable" }

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$files = @(Get-ChildItem -Path $appDirectory -Recurse -File | Where-Object { $_.Extension -ine ".pdb" })
if ($files.Count -eq 0) { throw "No application files were found for packaging." }
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($appDirectory, $file.FullName)
    $destination = Join-Path $stage $relative
    $destinationDirectory = Split-Path $destination -Parent
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item $file.FullName $destination -Force
}

$entryPoint = Join-Path $stage "DragonDiskForge.App.exe"
if (-not (Test-Path $entryPoint -PathType Leaf)) { throw "Packaged application is missing DragonDiskForge.App.exe at the package root." }

$cliStageDirectory = Join-Path $stage "cli"
New-Item -ItemType Directory -Path $cliStageDirectory -Force | Out-Null
$cliEntryPoint = Join-Path $cliStageDirectory "dragon-diskforge.exe"
Copy-Item $cliExecutable $cliEntryPoint -Force
& $cliEntryPoint --help | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Packaged CLI failed its launch smoke test with exit code $LASTEXITCODE." }

$toolsStageDirectory = Join-Path $stage "tools"
New-Item -ItemType Directory -Path $toolsStageDirectory -Force | Out-Null
$shellEntryPoint = Join-Path $toolsStageDirectory "dragon-diskforge-shell.exe"
Copy-Item $shellExecutable $shellEntryPoint -Force
& $shellEntryPoint --help | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Packaged shell helper failed its launch smoke test with exit code $LASTEXITCODE." }

$icon = Join-Path $stage "DragonDiskForge.ico"
if (-not (Test-Path $icon -PathType Leaf)) {
    $repositoryIcon = Join-Path (Get-Location) "src/DragonDiskForge.App/Assets/DragonDiskForge.ico"
    if (-not (Test-Path $repositoryIcon -PathType Leaf)) { throw "DragonDiskForge.ico is absent from both the application output and the canonical repository asset path." }
    Copy-Item $repositoryIcon $icon -Force
}

$debugFiles = @(Get-ChildItem -Path $stage -Recurse -File -Filter "*.pdb")
if ($debugFiles.Count -ne 0) { throw "Public-package staging contains debug symbol files." }
$testFiles = @(Get-ChildItem -Path $stage -Recurse -File | Where-Object { $_.FullName -match "[\\/]tests?[\\/]" -or $_.Name -match "(?i)(SmokeTests|IntegrationTests)" })
if ($testFiles.Count -ne 0) { throw "Public-package staging contains test-only files." }

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($entryPoint)
$productVersion = [string]$versionInfo.ProductVersion
$fileVersion = [string]$versionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($productVersion) -or -not $productVersion.StartsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Packaged application ProductVersion '$productVersion' does not match expected version '$expected'."
}

$entryPointSha256 = (Get-FileHash -Path $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
$cliEntryPointSha256 = (Get-FileHash -Path $cliEntryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
$shellEntryPointSha256 = (Get-FileHash -Path $shellEntryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
$packageFilesBeforeManifest = @(Get-ChildItem -Path $stage -Recurse -File)
$manifest = [ordered]@{
    schemaVersion = 4
    product = "Dragon DiskForge"
    version = $expected
    productVersion = $productVersion
    fileVersion = $fileVersion
    architecture = "x64"
    entryPoint = "DragonDiskForge.App.exe"
    cliEntryPoint = "cli/dragon-diskforge.exe"
    shellIntegrationEntryPoint = "tools/dragon-diskforge-shell.exe"
    icon = "DragonDiskForge.ico"
    entryPointSha256 = $entryPointSha256
    cliEntryPointSha256 = $cliEntryPointSha256
    shellIntegrationEntryPointSha256 = $shellEntryPointSha256
    debugSymbolsIncluded = $false
    fileCount = $packageFilesBeforeManifest.Count + 1
}
$manifest | ConvertTo-Json | Set-Content -Path (Join-Path $stage "package-manifest.json") -Encoding utf8NoBOM

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $checksum -Value "$hash  $([System.IO.Path]::GetFileName($zip))" -Encoding ascii

Write-Host "Packaged $((Get-ChildItem -Path $stage -Recurse -File).Count) files."
Write-Host "Version: $expected"
Write-Host "ZIP: $zip"
Write-Host "SHA-256: $hash"
