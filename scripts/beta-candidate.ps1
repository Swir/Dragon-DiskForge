[CmdletBinding()]
param(
    [ValidateSet("prepare", "metadata", "self-test")]
    [string]$Mode = "self-test",

    [string]$PropsPath = "Directory.Build.props",
    [string]$OutputDirectory = "artifacts/windows",
    [string]$ExpectedPrefix = "0.5.0",
    [string]$BetaSuffix = "beta.1",
    [string]$SourceCommit = "",
    [string]$WorkflowRunId = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Assert-VersionToken {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -notmatch '^[0-9A-Za-z][0-9A-Za-z.-]*$') {
        throw "$Name '$Value' is not a safe semantic-version token."
    }
}

function Assert-HexSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Name must be a 64-character SHA-256 value."
    }

    return $Value.ToLowerInvariant()
}

function Get-ExpectedVersion {
    Assert-VersionToken -Value $ExpectedPrefix -Name "ExpectedPrefix"
    Assert-VersionToken -Value $BetaSuffix -Name "BetaSuffix"
    return "$ExpectedPrefix-$BetaSuffix"
}

function Read-VersionProps {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $document = New-Object System.Xml.XmlDocument
    $document.PreserveWhitespace = $true
    $document.Load($resolved)

    $group = $document.SelectSingleNode('/Project/PropertyGroup[DragonDiskForgeVersionPrefix]')
    if ($null -eq $group) {
        throw "Directory.Build.props does not contain the Dragon DiskForge version property group."
    }

    $prefixNode = $group.SelectSingleNode('DragonDiskForgeVersionPrefix')
    $suffixNode = $group.SelectSingleNode('DragonDiskForgeVersionSuffix')
    if ($null -eq $prefixNode -or $null -eq $suffixNode) {
        throw "Directory.Build.props is missing DragonDiskForgeVersionPrefix or DragonDiskForgeVersionSuffix."
    }

    return [pscustomobject]@{
        path = $resolved
        document = $document
        prefixNode = $prefixNode
        suffixNode = $suffixNode
        prefix = [string]$prefixNode.InnerText
        suffix = [string]$suffixNode.InnerText
    }
}

function Set-BetaVersionInWorkspace {
    param([Parameter(Mandatory = $true)][string]$Path)

    $expected = Get-ExpectedVersion
    $props = Read-VersionProps -Path $Path
    if ([string]$props.prefix -ne $ExpectedPrefix) {
        throw "Repository version prefix '$($props.prefix)' does not match beta target '$ExpectedPrefix'. Refusing candidate promotion."
    }

    $props.suffixNode.InnerText = $BetaSuffix
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $false
    $settings.OmitXmlDeclaration = $true
    $writer = [System.Xml.XmlWriter]::Create($props.path, $settings)
    try {
        $props.document.Save($writer)
    }
    finally {
        $writer.Dispose()
    }

    $reloaded = Read-VersionProps -Path $props.path
    if ([string]$reloaded.prefix -ne $ExpectedPrefix -or [string]$reloaded.suffix -ne $BetaSuffix) {
        throw "Workspace beta-version promotion did not persist the expected values."
    }

    Write-Host "Prepared workspace-only beta version: $expected"
    Write-Host "Committed repository metadata is not changed by this script unless the caller explicitly commits the workspace file."
}

function Assert-SourceCommit {
    param([Parameter(Mandatory = $true)][string]$Commit)
    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "SourceCommit must be an exact 40-character Git commit SHA."
    }
}

function Write-CandidateMetadata {
    $expected = Get-ExpectedVersion
    Assert-SourceCommit -Commit $SourceCommit
    if (-not [string]::IsNullOrWhiteSpace($WorkflowRunId) -and $WorkflowRunId -notmatch '^[0-9]+$') {
        throw "WorkflowRunId must contain digits only when supplied."
    }

    $output = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
    $zip = Join-Path $output "DragonDiskForge-win-x64.zip"
    $checksum = "$zip.sha256"
    if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) { throw "Beta candidate ZIP was not found: $zip" }
    if (-not (Test-Path -LiteralPath $checksum -PathType Leaf)) { throw "Beta candidate checksum was not found: $checksum" }

    $verifyPackage = Join-Path $PSScriptRoot "verify-package.ps1"
    if (-not (Test-Path -LiteralPath $verifyPackage -PathType Leaf)) { throw "verify-package.ps1 was not found next to beta-candidate.ps1." }
    & $verifyPackage -OutputDirectory $OutputDirectory -ExpectedVersion $expected

    $checksumLine = (Get-Content -LiteralPath $checksum -Raw).Trim()
    if ($checksumLine -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "Beta candidate checksum sidecar has an invalid format."
    }
    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    if ($declaredName -ne [System.IO.Path]::GetFileName($zip)) {
        throw "Beta candidate checksum sidecar targets '$declaredName' instead of '$([System.IO.Path]::GetFileName($zip))'."
    }
    $actualHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "Beta candidate SHA-256 mismatch."
    }

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-candidate-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $zip -DestinationPath $tempRoot -Force
        $manifestPath = Join-Path $tempRoot "package-manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Candidate package manifest is missing." }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([string]$manifest.version -ne $expected) { throw "Candidate manifest version '$($manifest.version)' does not match '$expected'." }
        if ([string]$manifest.architecture -ne "x64") { throw "Candidate architecture '$($manifest.architecture)' is not x64." }
        $packageManifestSchema = [int]$manifest.schemaVersion
        if ($packageManifestSchema -lt 5) { throw "Candidate package manifest predates the beta QA contract." }

        $entryPointSha256 = Assert-HexSha256 -Value ([string]$manifest.entryPointSha256) -Name "entryPointSha256"
        $betaManualQaEntryPointSha256 = Assert-HexSha256 -Value ([string]$manifest.betaManualQaEntryPointSha256) -Name "betaManualQaEntryPointSha256"
        $betaUacWitnessEntryPointSha256 = $null
        $betaUacPairVerifierEntryPointSha256 = $null
        if ($packageManifestSchema -ge 6) {
            if (-not ($manifest.PSObject.Properties.Name -contains 'betaUacWitnessEntryPointSha256')) {
                throw "Package manifest schema $packageManifestSchema is missing betaUacWitnessEntryPointSha256."
            }
            if (-not ($manifest.PSObject.Properties.Name -contains 'betaUacPairVerifierEntryPointSha256')) {
                throw "Package manifest schema $packageManifestSchema is missing betaUacPairVerifierEntryPointSha256."
            }
            $betaUacWitnessEntryPointSha256 = Assert-HexSha256 -Value ([string]$manifest.betaUacWitnessEntryPointSha256) -Name "betaUacWitnessEntryPointSha256"
            $betaUacPairVerifierEntryPointSha256 = Assert-HexSha256 -Value ([string]$manifest.betaUacPairVerifierEntryPointSha256) -Name "betaUacPairVerifierEntryPointSha256"
        }

        $metadata = [ordered]@{
            schemaVersion = 1
            kind = "DragonDiskForgeBetaCandidate"
            product = "Dragon DiskForge"
            version = $expected
            architecture = "x64"
            sourceCommit = $SourceCommit.ToLowerInvariant()
            workflowRunId = $WorkflowRunId
            packageFile = [System.IO.Path]::GetFileName($zip)
            packageSha256 = $actualHash
            packageManifestSchema = $packageManifestSchema
            entryPointSha256 = $entryPointSha256
            betaManualQaEntryPointSha256 = $betaManualQaEntryPointSha256
            createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
            publicRelease = $false
        }
        if ($packageManifestSchema -ge 6) {
            $metadata.betaUacWitnessEntryPointSha256 = $betaUacWitnessEntryPointSha256
            $metadata.betaUacPairVerifierEntryPointSha256 = $betaUacPairVerifierEntryPointSha256
        }

        $metadataPath = Join-Path $output "beta-candidate.json"
        Write-Utf8NoBom -Path $metadataPath -Text (($metadata | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
        $metadataHash = (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $metadataSidecar = "$metadataPath.sha256"
        Write-Utf8NoBom -Path $metadataSidecar -Text ("{0}  {1}{2}" -f $metadataHash, [System.IO.Path]::GetFileName($metadataPath), [Environment]::NewLine)

        Write-Host "Verified exact beta candidate metadata."
        Write-Host "Version: $expected"
        Write-Host "Source commit: $($SourceCommit.ToLowerInvariant())"
        Write-Host "Package SHA-256: $actualHash"
        if ($packageManifestSchema -ge 6) {
            Write-Host "UAC witness SHA-256: $betaUacWitnessEntryPointSha256"
            Write-Host "UAC pair verifier SHA-256: $betaUacPairVerifierEntryPointSha256"
        }
        Write-Host "Metadata: $metadataPath"
    }
    finally {
        if (Test-Path $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-beta-candidate-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $props = Join-Path $tempRoot "Directory.Build.props"
        Write-Utf8NoBom -Path $props -Text @"
<Project>
  <PropertyGroup>
    <DragonDiskForgeVersionPrefix>0.5.0</DragonDiskForgeVersionPrefix>
    <DragonDiskForgeVersionSuffix>alpha.1</DragonDiskForgeVersionSuffix>
    <Version>`$(DragonDiskForgeVersionPrefix)-`$(DragonDiskForgeVersionSuffix)</Version>
  </PropertyGroup>
</Project>
"@

        Set-BetaVersionInWorkspace -Path $props
        $reloaded = Read-VersionProps -Path $props
        if ([string]$reloaded.suffix -ne "beta.1") {
            throw "Self-test failed: beta suffix was not written."
        }

        $wrongProps = Join-Path $tempRoot "wrong.props"
        Write-Utf8NoBom -Path $wrongProps -Text @"
<Project>
  <PropertyGroup>
    <DragonDiskForgeVersionPrefix>9.9.9</DragonDiskForgeVersionPrefix>
    <DragonDiskForgeVersionSuffix>alpha.1</DragonDiskForgeVersionSuffix>
  </PropertyGroup>
</Project>
"@
        $failedClosed = $false
        try {
            Set-BetaVersionInWorkspace -Path $wrongProps
        }
        catch {
            $failedClosed = $true
        }
        if (-not $failedClosed) {
            throw "Self-test failed: unexpected version prefix did not fail closed."
        }

        Assert-SourceCommit -Commit ("a" * 40)
        $invalidCommitRejected = $false
        try {
            Assert-SourceCommit -Commit "abc123"
        }
        catch {
            $invalidCommitRejected = $true
        }
        if (-not $invalidCommitRejected) {
            throw "Self-test failed: invalid source commit did not fail closed."
        }

        $validHash = Assert-HexSha256 -Value ("a" * 64) -Name "selfTestSha256"
        if ($validHash -ne ("a" * 64)) { throw "Self-test failed: valid SHA-256 changed unexpectedly." }
        $invalidHashRejected = $false
        try { $null = Assert-HexSha256 -Value "00" -Name "selfTestSha256" } catch { $invalidHashRejected = $true }
        if (-not $invalidHashRejected) { throw "Self-test failed: invalid SHA-256 did not fail closed." }

        Write-Host "Dragon DiskForge beta candidate contract self-test passed, including schema-6 UAC provenance hash validation."
    }
    finally {
        if (Test-Path $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    "prepare" {
        Set-BetaVersionInWorkspace -Path $PropsPath
        exit 0
    }
    "metadata" {
        if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
            throw "-SourceCommit is required for -Mode metadata."
        }
        Write-CandidateMetadata
        exit 0
    }
    "self-test" {
        Invoke-SelfTest
        exit 0
    }
}
