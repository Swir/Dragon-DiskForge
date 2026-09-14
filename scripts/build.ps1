$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
dotnet restore "$root\DragonDiskForge.sln"
dotnet build "$root\DragonDiskForge.sln" -c Release
