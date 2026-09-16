param([string]$OutputDirectory = "$PSScriptRoot/../artifacts")
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$version = ([xml](Get-Content "$repoRoot/Directory.Build.props")).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.-]+)?$') { throw 'Invalid release version.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$packageName = "DragonDiskForge-$version-win-x64"
$stage = Join-Path $output ("stage-" + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $stage $packageName
$appOutput = "$package/app/"
$cliOutput = "$package/cli/"
New-Item -ItemType Directory -Force -Path $appOutput,$cliOutput | Out-Null
& msbuild "$repoRoot/src/DragonDiskForge.App/DragonDiskForge.App.csproj" /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:WindowsAppSDKSelfContained=true /p:PublishSingleFile=false /p:PublishTrimmed=false "/p:PublishDir=$appOutput"
if ($LASTEXITCODE -ne 0) { throw 'Windows app publish failed.' }
& dotnet publish "$repoRoot/src/DragonDiskForge.Cli/DragonDiskForge.Cli.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $cliOutput
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
foreach ($required in @("$appOutput/DragonDiskForge.App.exe", "$appOutput/coreclr.dll", "$cliOutput/ddf.exe", "$cliOutput/coreclr.dll")) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing package dependency: $required" }
}
Copy-Item "$repoRoot/docs/RELEASE-NOTES.md" "$package/README.md"
Copy-Item "$repoRoot/THIRD-PARTY-NOTICES.txt" $package
'@echo off
cd /d "%~dp0app"
start "" "DragonDiskForge.App.exe" %*
' | Set-Content "$package/Start Dragon DiskForge.cmd" -Encoding ascii
& "$cliOutput/ddf.exe" --help
if ($LASTEXITCODE -ne 0) { throw 'Packaged CLI could not start.' }
& "$PSScriptRoot/test-package.ps1" -AppPath "$appOutput/DragonDiskForge.App.exe"
Get-ChildItem -LiteralPath $package -Filter '*.pdb' -Recurse | ForEach-Object { Remove-Item -LiteralPath $_.FullName }
$zipPath = Join-Path $output "$packageName.zip"
if (Test-Path -LiteralPath $zipPath) { throw 'Release archive already exists.' }
Compress-Archive -LiteralPath $package -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
"$hash  $packageName.zip" | Set-Content (Join-Path $output "$packageName.sha256") -Encoding ascii
Write-Host "Validated release archive: $zipPath"
