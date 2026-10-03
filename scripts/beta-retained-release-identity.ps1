[CmdletBinding()]
param(
    [ValidateSet('verify','self-test')]
    [string]$Mode = 'self-test',

    [string]$EvidencePath = 'docs/retained-beta-candidate.json',
    [string]$ExpectedVersion = '0.5.0-beta.1',
    [string]$ExpectedSourceCommit = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Hex {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][int]$Length,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -notmatch ("^[0-9a-fA-F]{" + $Length + "}$")) {
        throw "$Label must be exactly $Length hexadecimal characters."
    }

    return $Value.ToLowerInvariant()
}

function Assert-RetainedReleaseIdentity {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$SourceCommit = ''
    )

    if ([int]$Evidence.schemaVersion -ne 2) {
        throw 'Retained beta candidate evidence must use schemaVersion 2.'
    }
    if ([string]$Evidence.kind -ne 'DragonDiskForgeRetainedBetaCandidateEvidence') {
        throw 'Retained beta candidate evidence kind is invalid.'
    }
    if ([string]$Evidence.product -ne 'Dragon DiskForge') {
        throw 'Retained beta candidate product identity is invalid.'
    }
    if ([string]$Evidence.version -ne $Version) {
        throw "Retained beta candidate version '$($Evidence.version)' does not match '$Version'."
    }
    if ([string]$Evidence.architecture -ne 'x64') {
        throw 'Retained beta candidate architecture must be x64.'
    }
    if ([string]$Evidence.installerScope -ne 'per-user') {
        throw 'Retained beta candidate installerScope must be per-user.'
    }
    if ([bool]$Evidence.publicRelease) {
        throw 'Retained beta candidate evidence must describe a non-public engineering candidate.'
    }

    $source = Assert-Hex -Value ([string]$Evidence.sourceCommit) -Length 40 -Label 'Retained sourceCommit'
    if (-not [string]::IsNullOrWhiteSpace($SourceCommit)) {
        $expectedSource = Assert-Hex -Value $SourceCommit -Length 40 -Label 'ExpectedSourceCommit'
        if ($source -ne $expectedSource) {
            throw "Retained sourceCommit '$source' does not match '$expectedSource'."
        }
    }

    [int64]$runId = 0
    if (-not [int64]::TryParse([string]$Evidence.workflowRunId, [ref]$runId) -or $runId -le 0) {
        throw 'Retained workflowRunId must be a positive integer.'
    }

    $packageSha = Assert-Hex -Value ([string]$Evidence.packageSha256) -Length 64 -Label 'Retained packageSha256'
    $installerSha = Assert-Hex -Value ([string]$Evidence.installerSha256) -Length 64 -Label 'Retained installerSha256'

    $expectedPackageName = "DragonDiskForge-win-x64.zip"
    $expectedInstallerName = "DragonDiskForge-$Version-win-x64-setup.exe"
    if ([string]$Evidence.packageFile -ne $expectedPackageName) {
        throw "Retained packageFile '$($Evidence.packageFile)' does not match '$expectedPackageName'."
    }
    if ([string]$Evidence.installerFile -ne $expectedInstallerName) {
        throw "Retained installerFile '$($Evidence.installerFile)' does not match '$expectedInstallerName'."
    }

    return [pscustomobject]@{
        version = $Version
        sourceCommit = $source
        workflowRunId = $runId
        packageFile = $expectedPackageName
        packageSha256 = $packageSha
        installerFile = $expectedInstallerName
        installerSha256 = $installerSha
        installerScope = 'per-user'
    }
}

function Read-RetainedReleaseIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Version,
        [string]$SourceCommit = ''
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Retained beta candidate evidence is missing: $Path"
    }

    try {
        $evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "Retained beta candidate evidence is not valid JSON: $Path"
    }

    $identity = Assert-RetainedReleaseIdentity -Evidence $evidence -Version $Version -SourceCommit $SourceCommit
    $evidenceSha = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Hex -Value $evidenceSha -Length 64 -Label 'Retained evidence SHA-256' | Out-Null

    return [pscustomobject]@{
        identity = $identity
        retainedEvidenceSha256 = $evidenceSha
    }
}

function Invoke-SelfTest {
    $source = 'a' * 40
    $packageSha = 'b' * 64
    $installerSha = 'c' * 64
    $version = '0.5.0-beta.1'

    $valid = [pscustomobject]@{
        schemaVersion = 2
        kind = 'DragonDiskForgeRetainedBetaCandidateEvidence'
        product = 'Dragon DiskForge'
        version = $version
        architecture = 'x64'
        sourceCommit = $source
        workflowRunId = '278'
        packageFile = 'DragonDiskForge-win-x64.zip'
        packageSha256 = $packageSha
        installerFile = "DragonDiskForge-$version-win-x64-setup.exe"
        installerSha256 = $installerSha
        installerScope = 'per-user'
        publicRelease = $false
    }

    $proof = Assert-RetainedReleaseIdentity -Evidence $valid -Version $version -SourceCommit $source
    if ($proof.packageSha256 -ne $packageSha -or $proof.installerSha256 -ne $installerSha -or $proof.workflowRunId -ne 278) {
        throw 'Self-test failed: valid retained identity was not preserved.'
    }

    $wrongSource = $valid.PSObject.Copy()
    $wrongSource.sourceCommit = 'd' * 40
    $rejected = $false
    try { Assert-RetainedReleaseIdentity -Evidence $wrongSource -Version $version -SourceCommit $source | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: wrong retained sourceCommit was accepted.' }

    $wrongInstaller = $valid.PSObject.Copy()
    $wrongInstaller.installerFile = 'unexpected.exe'
    $rejected = $false
    try { Assert-RetainedReleaseIdentity -Evidence $wrongInstaller -Version $version -SourceCommit $source | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: wrong installer file was accepted.' }

    $public = $valid.PSObject.Copy()
    $public.publicRelease = $true
    $rejected = $false
    try { Assert-RetainedReleaseIdentity -Evidence $public -Version $version -SourceCommit $source | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: publicRelease=true retained evidence was accepted.' }

    Write-Host 'Dragon DiskForge retained release identity self-test passed.'
}

switch ($Mode) {
    'self-test' {
        Invoke-SelfTest
        exit 0
    }

    'verify' {
        $result = Read-RetainedReleaseIdentity -Path $EvidencePath -Version $ExpectedVersion -SourceCommit $ExpectedSourceCommit
        Write-Host 'Dragon DiskForge retained release identity verified.'
        Write-Host "Source commit: $($result.identity.sourceCommit)"
        Write-Host "Candidate workflow run: $($result.identity.workflowRunId)"
        Write-Host "Package SHA-256: $($result.identity.packageSha256)"
        Write-Host "Installer SHA-256: $($result.identity.installerSha256)"
        Write-Host "Retained evidence SHA-256: $($result.retainedEvidenceSha256)"
        exit 0
    }
}
