[CmdletBinding()]
param(
    [ValidateSet('build', 'self-test')]
    [string]$Mode = 'self-test',
    [string]$SourceDirectory = 'artifacts/windows/DragonDiskForge-win-x64',
    [string]$OutputDirectory = 'artifacts/installer',
    [string]$ExpectedVersion = '0.5.0-beta.1',
    [string]$InnoScriptPath = 'installer/DragonDiskForge.iss'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Version {
    param([Parameter(Mandatory = $true)][string]$Version)
    if ($Version -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)-beta\.(?<beta>\d+)$') {
        throw "Installer version '$Version' must use the form major.minor.patch-beta.number."
    }
    return "$($Matches.major).$($Matches.minor).$($Matches.patch).$($Matches.beta)"
}

function Find-InnoCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    foreach ($candidate in @(
        (Join-Path $env:LocalAppData 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw 'Inno Setup 6 compiler (ISCC.exe) was not found.'
}

function Assert-PackageSource {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Version
    )
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Installer source directory is missing: $Directory"
    }
    $manifestPath = Join-Path $Directory 'package-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Installer source is missing package-manifest.json."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([string]$manifest.version -ne $Version) { throw "Package version '$($manifest.version)' does not match '$Version'." }
    if ([string]$manifest.architecture -ne 'x64') { throw "Installer source architecture must be x64." }
    if ([string]$manifest.entryPoint -ne 'DragonDiskForge.App.exe') { throw 'Unexpected desktop entry point.' }
    foreach ($required in @('DragonDiskForge.App.exe', 'DragonDiskForge.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $required) -PathType Leaf)) {
            throw "Installer source is missing '$required'."
        }
    }
}

function Invoke-SelfTest {
    if ((Assert-Version -Version '0.5.0-beta.1') -ne '0.5.0.1') { throw 'Version conversion self-test failed.' }
    $rejected = $false
    try { Assert-Version -Version '0.5.0' | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Stable version was accepted by the beta installer contract.' }
    Write-Host 'Dragon DiskForge installer packaging contract self-test passed.'
}

if ($Mode -eq 'self-test') {
    Invoke-SelfTest
    exit 0
}

$numericVersion = Assert-Version -Version $ExpectedVersion
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$script = (Resolve-Path -LiteralPath $InnoScriptPath).Path
$output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
Assert-PackageSource -Directory $source -Version $ExpectedVersion
New-Item -ItemType Directory -Path $output -Force | Out-Null

$compiler = Find-InnoCompiler
& $compiler "/DMyAppVersion=$ExpectedVersion" "/DMyAppNumericVersion=$numericVersion" "/DSourceDir=$source" "/DOutputDir=$output" $script
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$installer = Join-Path $output "DragonDiskForge-$ExpectedVersion-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer output is missing: $installer" }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$sidecar = "$installer.sha256"
[System.IO.File]::WriteAllText($sidecar, "$hash  $([System.IO.Path]::GetFileName($installer))$([Environment]::NewLine)", [System.Text.Encoding]::ASCII)
Write-Host "Installer: $installer"
Write-Host "SHA-256: $hash"
