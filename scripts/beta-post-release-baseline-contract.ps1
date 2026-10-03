[CmdletBinding()]
param(
    [ValidateSet('library', 'self-test')]
    [string]$Mode = 'self-test'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ExactHex {
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

function Assert-PostReleaseBaselineContract {
    param([Parameter(Mandatory = $true)]$Baseline)

    if ([int]$Baseline.schemaVersion -ne 3) { throw 'Post-release baseline schemaVersion must be 3.' }
    if ([string]$Baseline.kind -ne 'DragonDiskForgePostReleaseAcceptanceBaseline') { throw 'Unexpected post-release baseline kind.' }
    if ([string]$Baseline.product -ne 'Dragon DiskForge') { throw 'Unexpected post-release baseline product.' }
    if ([string]$Baseline.version -ne '0.5.0-beta.1') { throw 'Unexpected post-release baseline version.' }

    $candidate = $Baseline.retainedCandidate
    if ([int]$candidate.workflowRunNumber -ne 278) { throw 'Retained Candidate workflowRunNumber must be 278.' }
    if ([string]$candidate.workflowRunId -ne '36045422063') { throw 'Retained Candidate workflowRunId mismatch.' }
    $null = Assert-ExactHex -Value ([string]$candidate.sourceCommit) -Length 40 -Label 'Retained sourceCommit'
    $null = Assert-ExactHex -Value ([string]$candidate.packageSha256) -Length 64 -Label 'Retained packageSha256'
    $null = Assert-ExactHex -Value ([string]$candidate.installerSha256) -Length 64 -Label 'Retained installerSha256'
    $null = Assert-ExactHex -Value ([string]$candidate.retainedEvidenceSha256) -Length 64 -Label 'Retained evidence SHA-256'
    if ([string]$candidate.evidencePath -ne 'docs/retained-beta-candidate.json') { throw 'Canonical retained evidence path mismatch.' }

    $policy = $Baseline.toolingPinPolicy
    if ([string]$policy.mode -ne 'exact-commit-plus-blob') { throw 'toolingPinPolicy.mode must be exact-commit-plus-blob.' }
    if (-not [bool]$policy.failClosed) { throw 'toolingPinPolicy.failClosed must be true.' }
    if (-not [bool]$policy.requirePublicReadBack) { throw 'toolingPinPolicy.requirePublicReadBack must be true.' }

    $requiredPaths = @($Baseline.requiredToolingPaths)
    if ($requiredPaths.Count -ne 6) { throw 'Exactly six post-release tooling paths are required.' }
    foreach ($path in $requiredPaths) {
        $property = $Baseline.requiredToolingBlobs.PSObject.Properties[[string]$path]
        if ($null -eq $property) { throw "Missing tooling blob pin for '$path'." }
        $null = Assert-ExactHex -Value ([string]$property.Value) -Length 40 -Label "Tooling blob pin '$path'"
    }

    foreach ($flag in @('retainedIdentityMatched', 'runtimeInstallerVerified')) {
        if (@($Baseline.requiredResultFlags) -notcontains $flag) { throw "Missing required result flag '$flag'." }
    }
    foreach ($step in @('retainedCandidateIdentity', 'publicPackageAndInstallerHashes', 'exactCommitToolingReadBack', 'publicInstallerSmokeInstallUninstall', 'postUninstallProductResidue')) {
        if (@($Baseline.verificationSequence) -notcontains $step) { throw "Missing verification step '$step'." }
    }

    return [pscustomobject]@{
        schemaVersion = 3
        candidateWorkflowRunNumber = 278
        candidateWorkflowRunId = '36045422063'
        toolingPinsValidated = $true
        retainedIdentityRequired = $true
        runtimeInstallerRequired = $true
        failClosed = $true
    }
}

function Invoke-SelfTest {
    $baseline = [pscustomobject]@{
        schemaVersion = 3
        kind = 'DragonDiskForgePostReleaseAcceptanceBaseline'
        product = 'Dragon DiskForge'
        version = '0.5.0-beta.1'
        retainedCandidate = [pscustomobject]@{
            workflowRunNumber = 278
            workflowRunId = '36045422063'
            sourceCommit = ('a' * 40)
            packageSha256 = ('b' * 64)
            installerSha256 = ('c' * 64)
            evidencePath = 'docs/retained-beta-candidate.json'
            retainedEvidenceSha256 = ('d' * 64)
        }
        requiredToolingPaths = @(
            'scripts/beta-post-release-verify.ps1',
            'scripts/verify-package.ps1',
            'scripts/verify-installer.ps1',
            'scripts/beta-retained-release-identity.ps1',
            'scripts/beta-retained-public-binding.ps1',
            'scripts/beta-public-installer-smoke-contract.ps1'
        )
        requiredToolingBlobs = [pscustomobject]@{
            'scripts/beta-post-release-verify.ps1' = ('1' * 40)
            'scripts/verify-package.ps1' = ('2' * 40)
            'scripts/verify-installer.ps1' = ('3' * 40)
            'scripts/beta-retained-release-identity.ps1' = ('4' * 40)
            'scripts/beta-retained-public-binding.ps1' = ('5' * 40)
            'scripts/beta-public-installer-smoke-contract.ps1' = ('6' * 40)
        }
        toolingPinPolicy = [pscustomobject]@{
            mode = 'exact-commit-plus-blob'
            failClosed = $true
            requirePublicReadBack = $true
        }
        requiredResultFlags = @('retainedIdentityMatched', 'runtimeInstallerVerified')
        verificationSequence = @(
            'retainedCandidateIdentity',
            'publicPackageAndInstallerHashes',
            'exactCommitToolingReadBack',
            'publicInstallerSmokeInstallUninstall',
            'postUninstallProductResidue'
        )
    }

    $valid = Assert-PostReleaseBaselineContract -Baseline $baseline
    if (-not [bool]$valid.failClosed) { throw 'Self-test failed: valid baseline was rejected.' }

    $bad = $baseline.PSObject.Copy()
    $bad.toolingPinPolicy = [pscustomobject]@{
        mode = 'exact-commit-plus-blob'
        failClosed = $false
        requirePublicReadBack = $true
    }
    $rejected = $false
    try { Assert-PostReleaseBaselineContract -Baseline $bad | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: failClosed=false was accepted.' }

    Write-Host 'Dragon DiskForge post-release baseline contract self-test passed.'
}

if ($Mode -eq 'self-test') {
    Invoke-SelfTest
}
