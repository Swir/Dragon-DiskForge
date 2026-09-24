[CmdletBinding()]
param(
    [ValidateSet('verify', 'self-test')]
    [string]$Mode = 'self-test',
    [string]$InstallerPath = 'artifacts/installer/DragonDiskForge-0.5.0-beta.1-win-x64-setup.exe',
    [string]$ChecksumFile = '',
    [string]$ExpectedVersion = '0.5.0-beta.1',
    [switch]$SmokeInstall
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Checksum {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$ExpectedName)
    $line = (Get-Content -LiteralPath $Path -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s{2}(.+)$') { throw 'Installer checksum sidecar has an invalid format.' }
    if ($Matches[2] -ne $ExpectedName) { throw "Checksum sidecar names '$($Matches[2])', expected '$ExpectedName'." }
    return $Matches[1].ToLowerInvariant()
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-installer-contract-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    try {
        $file = Join-Path $root 'setup.exe'
        [System.IO.File]::WriteAllBytes($file, [byte[]](1, 2, 3))
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        $sidecar = "$file.sha256"
        [System.IO.File]::WriteAllText($sidecar, "$hash  setup.exe$([Environment]::NewLine)", [System.Text.Encoding]::ASCII)
        if ((Read-Checksum -Path $sidecar -ExpectedName 'setup.exe') -ne $hash) { throw 'Checksum parser self-test failed.' }
        $rejected = $false
        try { Read-Checksum -Path $sidecar -ExpectedName 'other.exe' | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { throw 'Mismatched installer filename was accepted.' }
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
    Write-Host 'Dragon DiskForge installer verification contract self-test passed.'
}

if ($Mode -eq 'self-test') {
    Invoke-SelfTest
    exit 0
}

$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$expectedName = "DragonDiskForge-$ExpectedVersion-win-x64-setup.exe"
if ([System.IO.Path]::GetFileName($installer) -ne $expectedName) { throw "Unexpected installer filename." }
$checksum = if ([string]::IsNullOrWhiteSpace($ChecksumFile)) { "$installer.sha256" } else { (Resolve-Path -LiteralPath $ChecksumFile).Path }
$declared = Read-Checksum -Path $checksum -ExpectedName $expectedName
$actual = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
if ($declared -ne $actual) { throw 'Installer SHA-256 does not match its sidecar.' }

if ($SmokeInstall) {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-installer-smoke-' + [guid]::NewGuid().ToString('N'))
    try {
        $install = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', "/DIR=`"$root`"") -Wait -PassThru
        if ($install.ExitCode -ne 0) { throw "Silent installer failed with exit code $($install.ExitCode)." }
        foreach ($required in @('DragonDiskForge.App.exe', 'package-manifest.json', 'unins000.exe')) {
            if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) { throw "Installed payload is missing '$required'." }
        }
        $manifest = Get-Content -LiteralPath (Join-Path $root 'package-manifest.json') -Raw | ConvertFrom-Json
        if ([string]$manifest.version -ne $ExpectedVersion -or [string]$manifest.architecture -ne 'x64') {
            throw 'Installed package identity does not match the expected beta.'
        }
        $uninstall = Start-Process -FilePath (Join-Path $root 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) { throw "Silent uninstaller failed with exit code $($uninstall.ExitCode)." }
    }
    finally {
        if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
    }
}

Write-Host 'Dragon DiskForge installer verification passed.'
Write-Host "Installer SHA-256: $actual"
