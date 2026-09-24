[CmdletBinding()]
param(
    [ValidateSet("status", "next", "verify-group", "record-command", "self-test")]
    [string]$Mode = "next",

    [string]$WorkspacePath = "",

    [ValidateSet("desktop", "uac", "drag")]
    [string]$Group = "desktop",

    [string]$Check = "",

    [ValidateSet("pass", "fail")]
    [string]$Result = "pass",

    [switch]$HumanConfirmed,

    [string]$Note = "",

    [string]$GateStatusScript = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:SessionSchemaVersion = 1
$Script:SessionFileName = "beta-qa-session.json"
$Script:MinObservationNoteLength = 12
$Script:MaxObservationNoteLength = 1000

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

function Get-RequiredCheckIds {
    return @(
        "desktop.clean-launch",
        "desktop.basic-regression",
        "uac.iso-no-prompt",
        "uac.vhd-cancel",
        "uac.vhd-approve-readonly",
        "uac.vhdx-cancel",
        "uac.vhdx-approve-readonly",
        "uac.unmount-refresh",
        "drag.file-explorer-copy",
        "drag.folder-explorer-copy",
        "drag.desktop-copy",
        "drag.cancel-no-mutation",
        "drag.reparse-blocked",
        "drag.stale-blocked"
    )
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
        metadata = $session
    }
}

function Resolve-BoundQaTool {
    param([Parameter(Mandatory = $true)]$Session)

    Assert-Property -Object $Session.metadata -Name "extractRoot" -Context "Beta QA session"
    Assert-Property -Object $Session.metadata.package -Name "qaToolRelative" -Context "Beta QA session package"
    Assert-Property -Object $Session.metadata.package -Name "qaToolSha256" -Context "Beta QA session package"

    $extractRootValue = [string]$Session.metadata.extractRoot
    if ([string]::IsNullOrWhiteSpace($extractRootValue)) {
        throw "Beta QA session extractRoot is empty."
    }
    $extractRoot = [System.IO.Path]::GetFullPath($extractRootValue)
    if (-not (Test-Path -LiteralPath $extractRoot -PathType Container)) {
        throw "Beta QA extracted candidate root is missing: $extractRoot"
    }

    $relative = [string]$Session.metadata.package.qaToolRelative
    if ([string]::IsNullOrWhiteSpace($relative)) {
        throw "Beta QA session packaged QA-tool relative path is empty."
    }
    $normalized = $relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if ([System.IO.Path]::IsPathRooted($normalized)) {
        throw "Beta QA session packaged QA-tool path must remain package-relative."
    }

    $toolPath = [System.IO.Path]::GetFullPath((Join-Path $extractRoot $normalized))
    $rootPrefix = $extractRoot.TrimEnd([char]'\', [char]'/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $toolPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Beta QA session packaged QA-tool path escapes the extracted candidate root."
    }
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw "Beta QA packaged manual-QA tool is missing: $toolPath"
    }

    $expectedHash = ([string]$Session.metadata.package.qaToolSha256).ToLowerInvariant()
    if ($expectedHash -notmatch '^[0-9a-f]{64}$') {
        throw "Beta QA session packaged QA-tool SHA-256 is invalid."
    }
    $actualHash = (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Beta QA packaged manual-QA tool SHA-256 changed after session preparation. Re-prepare from the exact retained candidate."
    }

    return $toolPath
}

function ConvertTo-SingleQuotedPowerShellLiteral {
    param([Parameter(Mandatory = $true)][string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function New-RecordCommand {
    param(
        [Parameter(Mandatory = $true)]$Session,
        [Parameter(Mandatory = $true)][string]$QaToolPath,
        [Parameter(Mandatory = $true)][string]$CheckId,
        [Parameter(Mandatory = $true)][ValidateSet("pass", "fail")][string]$ObservationResult,
        [bool]$Confirmed,
        [Parameter(Mandatory = $true)][string]$ObservationNote
    )

    if ($CheckId -notin @(Get-RequiredCheckIds)) {
        throw "Unknown manual QA check '$CheckId'. Run -Mode next or -Mode status to see the current checklist."
    }
    if ($ObservationResult -eq "pass" -and -not $Confirmed) {
        throw "A passing record command requires -HumanConfirmed."
    }

    $note = $ObservationNote.Trim()
    if ($note.Length -lt $Script:MinObservationNoteLength -or $note.Length -gt $Script:MaxObservationNoteLength) {
        throw "-Note must contain $($Script:MinObservationNoteLength)-$($Script:MaxObservationNoteLength) non-whitespace characters."
    }

    $human = if ($Confirmed) { " -HumanConfirmed" } else { "" }
    return "& $(ConvertTo-SingleQuotedPowerShellLiteral -Value $QaToolPath) -Mode record -EvidencePath $(ConvertTo-SingleQuotedPowerShellLiteral -Value $Session.evidencePath) -PackagePath $(ConvertTo-SingleQuotedPowerShellLiteral -Value $Session.packagePath) -ChecksumFile $(ConvertTo-SingleQuotedPowerShellLiteral -Value $Session.checksumFile) -Check $(ConvertTo-SingleQuotedPowerShellLiteral -Value $CheckId) -Result $ObservationResult$human -Note $(ConvertTo-SingleQuotedPowerShellLiteral -Value $note)"
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

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $threw = $false
    try { & $Action } catch { $threw = $true }
    if (-not $threw) {
        throw "Self-test failed: '$Name' did not fail closed."
    }
}

function Invoke-SelfTest {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-qa-operator-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $package = Join-Path $root "DragonDiskForge-win-x64.zip"
        $checksum = "$package.sha256"
        $evidence = Join-Path $root "beta-manual-qa.json"
        $extractRoot = Join-Path $root "package"
        $toolsRoot = Join-Path $extractRoot "tools"
        $qaTool = Join-Path $toolsRoot "beta-manual-qa.ps1"
        New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
        Write-Utf8NoBom -Path $package -Text "package"
        Write-Utf8NoBom -Path $checksum -Text "checksum"
        Write-Utf8NoBom -Path $evidence -Text "{}"
        Write-Utf8NoBom -Path $qaTool -Text "Write-Host 'qa'"
        $qaToolHash = (Get-FileHash -LiteralPath $qaTool -Algorithm SHA256).Hash.ToLowerInvariant()

        $sessionObject = [ordered]@{
            schemaVersion = 1
            package = [ordered]@{
                path = $package
                checksumFile = $checksum
                qaToolRelative = "tools/beta-manual-qa.ps1"
                qaToolSha256 = $qaToolHash
            }
            extractRoot = $extractRoot
            evidencePath = $evidence
        }
        $sessionFile = Join-Path $root $Script:SessionFileName
        Write-Utf8NoBom -Path $sessionFile -Text (($sessionObject | ConvertTo-Json -Depth 5) + [Environment]::NewLine)

        $resolved = Resolve-Session -Path $root
        if ($resolved.packagePath -ne (Resolve-Path -LiteralPath $package).Path) { throw "Self-test failed: package routing drifted." }
        if ($resolved.checksumFile -ne (Resolve-Path -LiteralPath $checksum).Path) { throw "Self-test failed: checksum routing drifted." }
        if ($resolved.evidencePath -ne (Resolve-Path -LiteralPath $evidence).Path) { throw "Self-test failed: evidence routing drifted." }

        $resolvedQaTool = Resolve-BoundQaTool -Session $resolved
        if ($resolvedQaTool -ne (Resolve-Path -LiteralPath $qaTool).Path) { throw "Self-test failed: packaged QA-tool routing drifted." }

        $recordCommand = New-RecordCommand -Session $resolved -QaToolPath $resolvedQaTool -CheckId "uac.vhd-cancel" -ObservationResult "pass" -Confirmed $true -ObservationNote "Observed the cancellation path safely."
        foreach ($requiredFragment in @("-Mode record", "-Check 'uac.vhd-cancel'", "-Result pass", "-HumanConfirmed", "Observed the cancellation path safely.")) {
            if (-not $recordCommand.Contains($requiredFragment)) {
                throw "Self-test failed: exact record command is missing '$requiredFragment'."
            }
        }

        Assert-Throws -Name "pass command without human confirmation" -Action {
            New-RecordCommand -Session $resolved -QaToolPath $resolvedQaTool -CheckId "uac.vhd-cancel" -ObservationResult "pass" -Confirmed $false -ObservationNote "Observed the cancellation path safely." | Out-Null
        }
        Assert-Throws -Name "unknown check command" -Action {
            New-RecordCommand -Session $resolved -QaToolPath $resolvedQaTool -CheckId "unknown.check" -ObservationResult "fail" -Confirmed $false -ObservationNote "Observed a concrete failure here." | Out-Null
        }
        Assert-Throws -Name "short observation note" -Action {
            New-RecordCommand -Session $resolved -QaToolPath $resolvedQaTool -CheckId "uac.vhd-cancel" -ObservationResult "fail" -Confirmed $false -ObservationNote "too short" | Out-Null
        }

        $next = Get-GateParameters -Session $resolved -Operation "next" -SelectedGroup "desktop"
        if ($next.Mode -ne "next" -or $next.ContainsKey("Group")) { throw "Self-test failed: next routing parameters are invalid." }

        $verify = Get-GateParameters -Session $resolved -Operation "verify-group" -SelectedGroup "uac"
        if ($verify.Mode -ne "verify-group" -or $verify.Group -ne "uac") { throw "Self-test failed: verify-group routing parameters are invalid." }

        $originalQaHash = $sessionObject.package.qaToolSha256
        $sessionObject.package.qaToolSha256 = ("0" * 64)
        Write-Utf8NoBom -Path $sessionFile -Text (($sessionObject | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        $tamperedResolved = Resolve-Session -Path $root
        Assert-Throws -Name "packaged QA-tool hash mismatch" -Action { Resolve-BoundQaTool -Session $tamperedResolved | Out-Null }
        $sessionObject.package.qaToolSha256 = $originalQaHash

        $sessionObject.schemaVersion = 2
        Write-Utf8NoBom -Path $sessionFile -Text (($sessionObject | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        Assert-Throws -Name "unknown session schema" -Action { Resolve-Session -Path $root | Out-Null }

        Remove-Item -LiteralPath $checksum -Force
        $sessionObject.schemaVersion = 1
        Write-Utf8NoBom -Path $sessionFile -Text (($sessionObject | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        Assert-Throws -Name "missing checksum sidecar" -Action { Resolve-Session -Path $root | Out-Null }

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

if ($Mode -eq "record-command") {
    $qaTool = Resolve-BoundQaTool -Session $session
    $command = New-RecordCommand -Session $session -QaToolPath $qaTool -CheckId $Check -ObservationResult $Result -Confirmed ([bool]$HumanConfirmed) -ObservationNote $Note
    Write-Host "Exact packaged manual-QA record command"
    Write-Host "Session: $($session.sessionPath)"
    Write-Host "This command is generated only; no observation is recorded or auto-passed."
    Write-Host ""
    Write-Host $command
    exit 0
}

$verifier = Resolve-GateStatusScript -Path $GateStatusScript
$parameters = Get-GateParameters -Session $session -Operation $Mode -SelectedGroup $Group

Write-Host "Beta QA workspace router"
Write-Host "Session: $($session.sessionPath)"
Write-Host "Delegating to: $verifier"
Write-Host "No observation is recorded or auto-passed by this router."
Write-Host ""

& $verifier @parameters
