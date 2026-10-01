[CmdletBinding()]
param(
    [ValidateSet('verify','self-test')]
    [string]$Mode = 'self-test',

    [string]$InstallerPath = '',
    [string]$InstallerVerifierPath = 'scripts/verify-installer.ps1',
    [string]$ExpectedVersion = '0.5.0-beta.1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PublicInstallerSmokeArguments {
    param(
        [Parameter(Mandatory = $true)][string]$VerifierPath,
        [Parameter(Mandatory = $true)][string]$PublicInstallerPath,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ($Version -notmatch '^0\\.[0-9]+\\.[0-9]+-beta\\.[0-9]+$') {
        throw "ExpectedVersion '$Version' is invalid."
    }
    if ([System.IO.Path]::GetFileName($VerifierPath) -ne 'verify-installer.ps1') {
        throw 'Installer verifier path must target verify-installer.ps1.'
    }
    if ([System.IO.Path]::GetFileName($PublicInstallerPath) -ne "DragonDiskForge-$Version-win-x64-setup.exe") {
        throw 'Public installer file name does not match the expected beta version.'
    }

    return @(
        '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', $VerifierPath,
        '-Mode', 'verify',
        '-InstallerPath', $PublicInstallerPath,
        '-ExpectedVersion', $Version,
        '-SmokeInstall'
    )
}

function Invoke-SelfTest {
    $version = '0.5.0-beta.1'
    $valid = @{
        VerifierPath = 'scripts/verify-installer.ps1'
        PublicInstallerPath = "artifacts/DragonDiskForge-$version-win-x64-setup.exe"
        Version = $version
    }
    $args = Get-PublicInstallerSmokeArguments @valid

    if ($args -notcontains '-SmokeInstall') {
        throw 'Self-test failed: public installer verification did not require -SmokeInstall.'
    }
    if ($args -notcontains 'verify') {
        throw 'Self-test failed: installer verifier mode is not verify.'
    }

    $invalid = @{
        VerifierPath = 'scripts/verify-installer.ps1'
        PublicInstallerPath = 'wrong.exe'
        Version = $version
    }
    $rejected = $false
    try { Get-PublicInstallerSmokeArguments @invalid | Out-Null } catch { $rejected = $true }
    if (-not $rejected) {
        throw 'Self-test failed: unexpected public installer name was accepted.'
    }

    Write-Host 'Dragon DiskForge public installer smoke contract self-test passed.'
}

if ($Mode -eq 'self-test') {
    Invoke-SelfTest
    exit 0
}

if ($env:OS -ne 'Windows_NT') {
    throw 'Public installer smoke verification must run on Windows.'
}
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    throw 'InstallerPath is required in verify mode.'
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Public installer is missing: $InstallerPath"
}
if (-not (Test-Path -LiteralPath $InstallerVerifierPath -PathType Leaf)) {
    throw "Installer verifier is missing: $InstallerVerifierPath"
}

$powershell = Get-Command powershell.exe -ErrorAction SilentlyContinue
if ($null -eq $powershell) {
    throw 'Windows PowerShell is required for installer smoke verification.'
}

$invokeArgs = @{
    VerifierPath = $InstallerVerifierPath
    PublicInstallerPath = $InstallerPath
    Version = $ExpectedVersion
}
$arguments = Get-PublicInstallerSmokeArguments @invokeArgs
& $powershell.Source @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Public installer smoke verification failed with exit code $LASTEXITCODE."
}

Write-Host 'Dragon DiskForge public installer smoke verification passed.'
