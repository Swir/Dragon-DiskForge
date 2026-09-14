$ErrorActionPreference = 'Stop'
Write-Host 'Dragon DiskForge - development environment check' -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'Missing .NET SDK. Install .NET 10 SDK and the WinUI development workload in Visual Studio 2026.' -ForegroundColor Yellow
    exit 1
}

$version = dotnet --version
Write-Host "dotnet: $version"
if (-not $version.StartsWith('10.')) {
    Write-Host 'Dragon DiskForge currently targets .NET 10.' -ForegroundColor Yellow
}

Write-Host 'Restoring packages...'
dotnet restore "$PSScriptRoot\..\DragonDiskForge.sln"
Write-Host 'Environment looks ready.' -ForegroundColor Green
