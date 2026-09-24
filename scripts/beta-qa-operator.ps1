[CmdletBinding()]
param(
    [ValidateSet("status", "next", "verify-group", "self-test")]
    [string]$Mode = "next",

    [string]$WorkspacePath = "",

    [ValidateSet("desktop", "uac", "drag")]
    [string]$Group = "desktop",

    [string]$GateStatusScript = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:SessionSchemaVersion = 1
$Script:SessionFileName = "beta-qa-session.json"

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Assert-Property {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Context
    )

    if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $Name)) {
        throw "$Context is missing required property '$Name'."
    }
}

function Resolve-Session {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "-WorkspacePath is required for manual-gate routing."
    }

    $candidate = [System.IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $candidate -PathType Container) {
        $candidate = Join-Path $candidate $Script:SessionFileName
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Beta QA session metadata is missing: $candidate"
    }

    $sessionPath = (Resolve-Path -LiteralPath $candidate).Path
    $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    Assert-Property -Object $session -Name "schemaVersion" -Context "Beta QA session"
    Assert-Property -Object $session -Name "package" -Context "Beta QA session"
    Assert-Property -Object $session -Name "evidencePath" -Context "Beta QA session"
    Assert-Property -Object $session.package -Name "path" -Context "Beta QA session package"
    Assert-Property -Object $session.package -Name "checksumFile" -Context "Beta QA session package"

    if ([int]$session.schemaVersion -ne $Script:SessionSchemaVersion) {
        throw "Unsupported beta QA session schema '$($session.schemaVersion)'; expected '$Script:SessionSchemaVersion'. Re-prepare from the exact retained candidate."
    }

    foreach ($entry in @(
        @{ Label = "candidate package"; Value = [string]$session.package.path },
        @{ Label = "candidate checksum sidecar"; Value = [string]$session.package.checksumFile },
        @{ Label = "manual-QA evidence"; Value = [string]$session.evidencePath }
    )) {
        if ([string]::IsNullOrWhiteSpace($entry.Value)) {
            throw "Beta QA session $($entry.Label) path is empty."
        }
        if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
            throw "Beta QA session $($entry.Label) is missing: $($entry.Value)"
        }
    }

    return [pscustomobject]@{
        sessionPath = $sessionPath
        packagePath = (Resolve-Path -LiteralPath ([string]$session.package.path)).Path
        checksumFile = (Resolve-Path -LiteralPath ([string]$session.package.checksumFile)).Path
        evidencePath = (Resolve-Path -LiteralPath ([string]$session.evidencePath)).Path
    }
}

function Resolve-GateStatusScript {
    param([string]$Path)

    $candidate = $Path
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = Join-Path $PSScriptRoot "beta-manual-gate-status.ps1"
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Manual-gate status verifier is missing: $candidate"
    }
    return (Resolve-Path -LiteralPath $candidate).Path
}

function Get-GateParameters {
    param(
        [Parameter(Mandatory = $true)]$Session,
        [Parameter(Mandatory = $true)][ValidateSet("status", "next", "verify-group")][string]$Operation,
        [Parameter(Mandatory = $true)][ValidateSet("desktop", "uac", "drag")][string]$SelectedGroup
    )

    $parameters = @{
        Mode = $Operation
        EvidencePath = $Session.evidencePath
        PackagePath = $Session.packagePath
        ChecksumFile = $Session.checksumFile
    }
    if ($Operation -eq "verify-group") {
        $parameters.Group = $SelectedGroup
    }
    return $parameters
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-operator-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $package = Join-Path $root "DragonDiskForge-win-x64.zip"
        $checksum = "$package.sha256"
        $evidence = Join-Path $root "beta-manual-qa.json"
        Write-Utf8NoBom -Path $package -Text "package"
        Write-Utf8NoBom -Path $checksum -Text "checksum"
        Write-Utf8NoBom -Path $evidence -Text "{}"

        $sessionObject = [ordered]@{
            schemaVersion = 1
            package = [ordered]@{
                path = $package
                checksumFile = $checksum
            }
            evidencePath = $evidence
        }
        $sessionFile = Join-Path $root $Script:SessionFileName
        Write-Utf8NoBom -Path $sessionFile -Text (($sessionObject | ConvertTo-Json -Depth 5) + [Environment]::NewLine)

        $resolved = Resolve-Session -Path $root
        if ($resolved.packagePath -ne (Resolve-Path -LiteralPath $package).Path) { throw "Self-test failed: package routing drifted." }
        if ($resolved.checksumFile -ne (Resolve-Path -LiteralPath $checksum).Path) { throw "Self-test failed: checksum routing drifted." }
        if ($resolved.evidencePath -ne (Resolve-Path -LiteralPath $evidence).Path) { throw "Self-test failed: evidence routing drifted." }

        $next = Get-GateParameters -Session $resolved -Operation "next" -SelectedGroup "desktop"
        if ($next.Mode -ne "next" -or $next.ContainsKey("Group")) { throw "Self-test failed: next routing parameters are invalid." }

        $verify = Get-GateParameters -Session $resolved -Operation "verify-group" -SelectedGroup "uac"
        if ($verify.Mode -ne "verify-group" -or $verify.Group -ne "uac") { throw "Self-test failed: verify-group routing parameters are invalid." }

        $sessionObject.schemaVersion = 2
        Write-Utf8NoBom -Path $sessionFile -Text (($sessionObject | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        $failedClosed = $false
        try { Resolve-Session -Path $root | Out-Null } catch { $failedClosed = $true }
        if (-not $failedClosed) { throw "Self-test failed: unknown session schema did not fail closed." }

        Remove-Item -LiteralPath $checksum -Force
        $sessionObject.schemaVersion = 1
        Write-Utf8NoBom -Path $sessionFile -Text (($sessionObject | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        $missingRejected = $false
        try { Resolve-Session -Path $root | Out-Null } catch { $missingRejected = $true }
        if (-not $missingRejected) { throw "Self-test failed: missing checksum sidecar did not fail closed." }

        Write-Host "Beta QA operator self-test passed."
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Mode -eq "self-test") {
    Invoke-SelfTest
    exit 0
}

$session = Resolve-Session -Path $WorkspacePath
$verifier = Resolve-GateStatusScript -Path $GateStatusScript
$parameters = Get-GateParameters -Session $session -Operation $Mode -SelectedGroup $Group

Write-Host "Beta QA workspace router"
Write-Host "Session: $($session.sessionPath)"
Write-Host "Delegating to: $verifier"
Write-Host "No observation is recorded or auto-passed by this router."
Write-Host ""

& $verifier @parameters
