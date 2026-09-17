[CmdletBinding()]
param(
    [string]$AppProject = "src/DragonDiskForge.App/DragonDiskForge.App.csproj",
    [string]$CliProject = "src/DragonDiskForge.Cli/DragonDiskForge.Cli.csproj",
    [string]$ShellProject = "src/DragonDiskForge.Shell/DragonDiskForge.Shell.csproj",
    [string]$BetaManualQaScript = "scripts/beta-manual-qa.ps1",
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

function Add-VcRuntimeCandidatesFromRoot {
    param(
        [System.Collections.Generic.List[string]]$Candidates,
        [string]$Root
    )

    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path $Root -PathType Container)) { return }

    $direct = Join-Path $Root "x64\Microsoft.VC143.CRT"
    if (Test-Path $direct -PathType Container) { $Candidates.Add($direct) }

    foreach ($versionDirectory in @(Get-ChildItem -Path $Root -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending)) {
        $candidate = Join-Path $versionDirectory.FullName "x64\Microsoft.VC143.CRT"
        if (Test-Path $candidate -PathType Container) { $Candidates.Add($candidate) }
    }
}

function Find-AppLocalVcRuntimeDirectory {
    $candidates = [System.Collections.Generic.List[string]]::new()

    if (-not [string]::IsNullOrWhiteSpace($env:VCToolsRedistDir)) {
        Add-VcRuntimeCandidatesFromRoot -Candidates $candidates -Root $env:VCToolsRedistDir
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere -PathType Leaf) {
        $installations = @(& $vswhere -products * -property installationPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        foreach ($installationPath in $installations) {
            Add-VcRuntimeCandidatesFromRoot -Candidates $candidates -Root (Join-Path $installationPath "VC\Redist\MSVC")
        }
    }

    $visualStudioRoot = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022"
    if (Test-Path $visualStudioRoot -PathType Container) {
        foreach ($edition in @(Get-ChildItem -Path $visualStudioRoot -Directory -ErrorAction SilentlyContinue)) {
            Add-VcRuntimeCandidatesFromRoot -Candidates $candidates -Root (Join-Path $edition.FullName "VC\Redist\MSVC")
        }
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if ((Test-Path (Join-Path $candidate "vcruntime140.dll") -PathType Leaf) -and
            (Test-Path (Join-Path $candidate "msvcp140.dll") -PathType Leaf)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "A redistributable x64 Microsoft.VC143.CRT directory was not found. Install the Visual C++ v14 redistributable build tools or set VCToolsRedistDir before packaging."
}

$expected = Get-RepositoryVersion -Override $ExpectedVersion
$appProjectPath = (Resolve-Path $AppProject).Path
$cliProjectPath = (Resolve-Path $CliProject).Path
$shellProjectPath = (Resolve-Path $ShellProject).Path
$betaManualQaScriptPath = (Resolve-Path $BetaManualQaScript).Path
$output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) "artifacts/publish"))
$appPublishDirectory = Join-Path $publishRoot "DragonDiskForge.App-win-x64"
$stage = Join-Path $output "DragonDiskForge-win-x64"
$zip = Join-Path $output "DragonDiskForge-win-x64.zip"
$checksum = "$zip.sha256"

Write-Host "Preparing clean Windows x64 package for version $expected."

if (Test-Path $appPublishDirectory) { Remove-Item $appPublishDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $appPublishDirectory -Force | Out-Null

Write-Host "Publishing runtime-complete Dragon DiskForge desktop app."
& msbuild $appProjectPath /restore /t:Publish /m "/p:Configuration=Release" "/p:Platform=x64" "/p:RuntimeIdentifier=win-x64" "/p:SelfContained=true" "/p:WindowsAppSDKSelfContained=true" "/p:DebugType=None" "/p:DebugSymbols=false" "/p:PublishDir=$appPublishDirectory\"
if ($LASTEXITCODE -ne 0) { throw "Self-contained desktop publish failed with exit code $LASTEXITCODE." }

$appEntryPoint = Join-Path $appPublishDirectory "DragonDiskForge.App.exe"
if (-not (Test-Path $appEntryPoint -PathType Leaf)) { throw "Published desktop executable was not found: $appEntryPoint" }

foreach ($runtimeFileName in @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll", "Microsoft.WindowsAppRuntime.dll")) {
    $runtimePath = Join-Path $appPublishDirectory $runtimeFileName
    if (-not (Test-Path $runtimePath -PathType Leaf)) {
        throw "Desktop publish is not runtime-complete; missing '$runtimeFileName' from '$appPublishDirectory'."
    }
}

Write-Host "Publishing self-contained Dragon DiskForge CLI."
& dotnet publish $cliProjectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Self-contained CLI publish failed with exit code $LASTEXITCODE." }
$cliProjectDirectory = Split-Path $cliProjectPath -Parent
$cliExecutable = Join-Path $cliProjectDirectory "bin/Release/net10.0/win-x64/publish/dragon-disk-forge.exe"
if (-not (Test-Path $cliExecutable -PathType Leaf)) { throw "Published CLI executable was not found: $cliExecutable" }

Write-Host "Publishing self-contained Dragon DiskForge shell helper."
& dotnet publish $shellProjectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Self-contained shell-helper publish failed with exit code $LASTEXITCODE." }
$shellProjectDirectory = Split-Path $shellProjectPath -Parent
$shellExecutable = Join-Path $shellProjectDirectory "bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/dragon-disk-forge-shell.exe"
if (-not (Test-Path $shellExecutable -PathType Leaf)) { throw "Published shell helper was not found: $shellExecutable" }

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$files = @(Get-ChildItem -Path $appPublishDirectory -Recurse -File | Where-Object { $_.Extension -ine ".pdb" })
if ($files.Count -eq 0) { throw "No published desktop application files were found for packaging." }
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($appPublishDirectory, $file.FullName)
    $destination = Join-Path $stage $relative
    $destinationDirectory = Split-Path $destination -Parent
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item $file.FullName $destination -Force
}

# Windows App SDK self-contained deployment does not make an unpackaged app
# independent of the native Visual C++ runtime. Ship Microsoft's redistributable
# CRT app-local so the ZIP has no machine-level VC++ prerequisite.
$vcRuntimeDirectory = Find-AppLocalVcRuntimeDirectory
$vcRuntimeFiles = @(Get-ChildItem -Path $vcRuntimeDirectory -File -Filter "*.dll")
if ($vcRuntimeFiles.Count -eq 0) { throw "No redistributable CRT DLLs were found in '$vcRuntimeDirectory'." }
foreach ($runtimeFile in $vcRuntimeFiles) {
    Copy-Item $runtimeFile.FullName (Join-Path $stage $runtimeFile.Name) -Force
}
Write-Host "Bundled $($vcRuntimeFiles.Count) app-local Visual C++ runtime DLLs from '$vcRuntimeDirectory'."

$entryPoint = Join-Path $stage "DragonDiskForge.App.exe"
if (-not (Test-Path $entryPoint -PathType Leaf)) { throw "Packaged application is missing DragonDiskForge.App.exe at the package root." }

foreach ($runtimeFileName in @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll", "Microsoft.WindowsAppRuntime.dll", "vcruntime140.dll", "msvcp140.dll")) {
    $runtimePath = Join-Path $stage $runtimeFileName
    if (-not (Test-Path $runtimePath -PathType Leaf)) {
        throw "Public-package staging is not runtime-complete; missing runtime file '$runtimeFileName'."
    }
}

$cliStageDirectory = Join-Path $stage "cli"
New-Item -ItemType Directory -Path $cliStageDirectory -Force | Out-Null
$cliEntryPoint = Join-Path $cliStageDirectory "dragon-disk-forge.exe"
Copy-Item $cliExecutable $cliEntryPoint -Force
& $cliEntryPoint --help | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Packaged CLI failed its launch smoke test with exit code $LASTEXITCODE." }

$toolsStageDirectory = Join-Path $stage "tools"
New-Item -ItemType Directory -Path $toolsStageDirectory -Force | Out-Null
$shellEntryPoint = Join-Path $toolsStageDirectory "dragon-disk-forge-shell.exe"
Copy-Item $shellExecutable $shellEntryPoint -Force
& $shellEntryPoint --help | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Packaged shell helper failed its launch smoke test with exit code $LASTEXITCODE." }

$betaManualQaEntryPoint = Join-Path $toolsStageDirectory "beta-manual-qa.ps1"
Copy-Item $betaManualQaScriptPath $betaManualQaEntryPoint -Force
& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $betaManualQaEntryPoint -Mode self-test
if ($LASTEXITCODE -ne 0) { throw "Packaged beta manual-QA evidence tool failed its Windows PowerShell self-test with exit code $LASTEXITCODE." }

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
$betaManualQaEntryPointSha256 = (Get-FileHash -Path $betaManualQaEntryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
$packageFilesBeforeManifest = @(Get-ChildItem -Path $stage -Recurse -File)
$manifest = [ordered]@{
    schemaVersion = 5
    product = "Dragon DiskForge"
    version = $expected
    productVersion = $productVersion
    fileVersion = $fileVersion
    architecture = "x64"
    entryPoint = "DragonDiskForge.App.exe"
    cliEntryPoint = "cli/dragon-disk-forge.exe"
    shellIntegrationEntryPoint = "tools/dragon-disk-forge-shell.exe"
    betaManualQaEntryPoint = "tools/beta-manual-qa.ps1"
    icon = "DragonDiskForge.ico"
    entryPointSha256 = $entryPointSha256
    cliEntryPointSha256 = $cliEntryPointSha256
    shellIntegrationEntryPointSha256 = $shellEntryPointSha256
    betaManualQaEntryPointSha256 = $betaManualQaEntryPointSha256
    runtimeDeployment = [ordered]@{
        dotNet = "self-contained"
        windowsAppSdk = "self-contained"
        visualCpp = "app-local"
    }
    debugSymbolsIncluded = $false
    fileCount = $packageFilesBeforeManifest.Count + 1
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $stage "package-manifest.json") -Encoding utf8NoBOM

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $checksum -Value "$hash  $([System.IO.Path]::GetFileName($zip))" -Encoding ascii

Write-Host "Packaged $((Get-ChildItem -Path $stage -Recurse -File).Count) files."
Write-Host "Version: $expected"
Write-Host "ZIP: $zip"
Write-Host "SHA-256: $hash"
