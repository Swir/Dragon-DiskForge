[CmdletBinding()]
param(
    [ValidateSet('library', 'self-test')]
    [string]$Mode = 'self-test'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-BindingHex {
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

function Assert-PublicReleaseRetainedBinding {
    param(
        [Parameter(Mandatory = $true)]$RetainedIdentity,
        [Parameter(Mandatory = $true)][string]$PublicSourceCommit,
        [Parameter(Mandatory = $true)][string]$PublicPackageSha256,
        [Parameter(Mandatory = $true)][string]$PublicInstallerSha256,
        [Parameter(Mandatory = $true)][string]$ManifestCandidateWorkflowRunId,
        [Parameter(Mandatory = $true)][string]$ManifestRetainedEvidenceSha256
    )

    $publicSource = Assert-BindingHex -Value $PublicSourceCommit -Length 40 -Label 'Public sourceCommit'
    $retainedSource = Assert-BindingHex -Value ([string]$RetainedIdentity.sourceCommit) -Length 40 -Label 'Retained sourceCommit'
    if ($publicSource -ne $retainedSource) {
        throw "Public sourceCommit '$publicSource' does not match retained sourceCommit '$retainedSource'."
    }

    $publicPackage = Assert-BindingHex -Value $PublicPackageSha256 -Length 64 -Label 'Public package SHA-256'
    $retainedPackage = Assert-BindingHex -Value ([string]$RetainedIdentity.packageSha256) -Length 64 -Label 'Retained package SHA-256'
    if ($publicPackage -ne $retainedPackage) {
        throw "Public package SHA-256 '$publicPackage' does not match retained package SHA-256 '$retainedPackage'."
    }

    $publicInstaller = Assert-BindingHex -Value $PublicInstallerSha256 -Length 64 -Label 'Public installer SHA-256'
    $retainedInstaller = Assert-BindingHex -Value ([string]$RetainedIdentity.installerSha256) -Length 64 -Label 'Retained installer SHA-256'
    if ($publicInstaller -ne $retainedInstaller) {
        throw "Public installer SHA-256 '$publicInstaller' does not match retained installer SHA-256 '$retainedInstaller'."
    }

    [int64]$manifestRunId = 0
    [int64]$retainedRunId = 0
    if (-not [int64]::TryParse($ManifestCandidateWorkflowRunId, [ref]$manifestRunId) -or $manifestRunId -le 0) {
        throw 'Manifest candidateWorkflowRunId must be a positive integer.'
    }
    if (-not [int64]::TryParse([string]$RetainedIdentity.workflowRunId, [ref]$retainedRunId) -or $retainedRunId -le 0) {
        throw 'Retained workflowRunId must be a positive integer.'
    }
    if ($manifestRunId -ne $retainedRunId) {
        throw "Manifest candidateWorkflowRunId '$manifestRunId' does not match retained workflow run '$retainedRunId'."
    }

    $manifestRetainedHash = Assert-BindingHex -Value $ManifestRetainedEvidenceSha256 -Length 64 -Label 'Manifest retainedEvidenceSha256'
    $retainedEvidenceHash = Assert-BindingHex -Value ([string]$RetainedIdentity.retainedEvidenceSha256) -Length 64 -Label 'Canonical retained evidence SHA-256'
    if ($manifestRetainedHash -ne $retainedEvidenceHash) {
        throw "Manifest retainedEvidenceSha256 '$manifestRetainedHash' does not match canonical retained evidence SHA-256 '$retainedEvidenceHash'."
    }

    return [pscustomobject]@{
        retainedIdentityMatched = $true
        sourceCommit = $publicSource
        candidateWorkflowRunId = $manifestRunId
        packageSha256 = $publicPackage
        installerSha256 = $publicInstaller
        retainedEvidenceSha256 = $retainedEvidenceHash
    }
}

function Invoke-SelfTest {
    $retained = [pscustomobject]@{
        sourceCommit = ('a' * 40)
        workflowRunId = 278
        packageSha256 = ('b' * 64)
        installerSha256 = ('c' * 64)
        retainedEvidenceSha256 = ('d' * 64)
    }

    $proof = Assert-PublicReleaseRetainedBinding \
        -RetainedIdentity $retained \
        -PublicSourceCommit ('a' * 40) \
        -PublicPackageSha256 ('b' * 64) \
        -PublicInstallerSha256 ('c' * 64) \
        -ManifestCandidateWorkflowRunId '278' \
        -ManifestRetainedEvidenceSha256 ('d' * 64)

    if (-not [bool]$proof.retainedIdentityMatched) {
        throw 'Self-test failed: valid retained/public binding was not accepted.'
    }

    foreach ($case in @(
        [pscustomobject]@{ name = 'source'; source = ('e' * 40); package = ('b' * 64); installer = ('c' * 64); run = '278'; retained = ('d' * 64) },
        [pscustomobject]@{ name = 'package'; source = ('a' * 40); package = ('e' * 64); installer = ('c' * 64); run = '278'; retained = ('d' * 64) },
        [pscustomobject]@{ name = 'installer'; source = ('a' * 40); package = ('b' * 64); installer = ('e' * 64); run = '278'; retained = ('d' * 64) },
        [pscustomobject]@{ name = 'workflow'; source = ('a' * 40); package = ('b' * 64); installer = ('c' * 64); run = '279'; retained = ('d' * 64) },
        [pscustomobject]@{ name = 'retained hash'; source = ('a' * 40); package = ('b' * 64); installer = ('c' * 64); run = '278'; retained = ('e' * 64) }
    )) {
        $rejected = $false
        try {
            Assert-PublicReleaseRetainedBinding \
                -RetainedIdentity $retained \
                -PublicSourceCommit $case.source \
                -PublicPackageSha256 $case.package \
                -PublicInstallerSha256 $case.installer \
                -ManifestCandidateWorkflowRunId $case.run \
                -ManifestRetainedEvidenceSha256 $case.retained | Out-Null
        }
        catch {
            $rejected = $true
        }
        if (-not $rejected) {
            throw "Self-test failed: mismatched $($case.name) binding was accepted."
        }
    }

    Write-Host 'Dragon DiskForge retained/public release binding self-test passed.'
}

if ($Mode -eq 'self-test') {
    Invoke-SelfTest
}
