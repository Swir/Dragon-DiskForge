[CmdletBinding()]
param(
    [ValidateSet("verify-ci", "publish", "self-test")]
    [string]$Mode = "self-test",

    [string]$CandidateMetadataPath = "artifacts/windows/beta-candidate.json",
    [string]$CandidateMetadataChecksumFile = "",
    [string]$QaKitManifestPath = "artifacts/windows/beta-qa-kit.json",
    [string]$QaKitManifestChecksumFile = "",
    [string]$PackagePath = "artifacts/windows/DragonDiskForge-win-x64.zip",
    [string]$PackageChecksumFile = "",
    [string]$EvidencePath = "artifacts/manual-qa/beta-manual-qa.json",
    [string]$EvidenceChecksumFile = "",
    [string]$ExpectedVersion = "0.5.0-beta.1",
    [string]$ExpectedSourceCommit = "",
    [string]$ReleaseNotesTemplatePath = "docs/release-notes/0.5.0-beta.1.md.tmpl",
    [string]$CapabilityMatrixPath = "docs/SUPPORTED-CAPABILITIES.md",
    [string]$OutputDirectory = "artifacts/release",
    [string]$Repository = "Swir/Dragon-DiskForge",
    [string]$TagName = "0.5.0-beta.1",
    [string]$ProofScriptPath = "scripts/beta-release-proof.ps1",
    [string]$PublisherScriptPath = "scripts/beta-release-publish.ps1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RequiredReleaseWorkflowNames = @(
    'Dragon DiskForge Build',
    'Dragon DiskForge Security Boundary',
    'Dragon DiskForge Beta Release Proof Contract',
    'Dragon DiskForge Beta Release Publish Contract'
)

function Assert-ExactCommit {
    param(
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "$Label must be an exact 40-character Git commit SHA."
    }
    return $Commit.ToLowerInvariant()
}

function Assert-RepositoryName {
    param([Parameter(Mandatory = $true)][string]$Name)

    if ($Name -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw "Repository '$Name' must be in owner/name form."
    }
    return $Name
}

function Resolve-File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-CurrentPowerShellExecutable {
    try {
        $process = Get-Process -Id $PID -ErrorAction Stop
        if (-not [string]::IsNullOrWhiteSpace([string]$process.Path) -and (Test-Path -LiteralPath $process.Path -PathType Leaf)) {
            return $process.Path
        }
    }
    catch {
    }

    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -ne $pwsh) { return $pwsh.Source }
    $powershell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($null -ne $powershell) { return $powershell.Source }
    throw "Cannot locate a PowerShell executable for the gated publisher."
}

function Assert-ExactHeadCiSnapshot {
    param(
        [Parameter(Mandatory = $true)][object[]]$Runs,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][int]$ExpectedTotal,
        [Parameter(Mandatory = $true)][string[]]$RequiredWorkflowNames
    )

    $commit = Assert-ExactCommit -Commit $SourceCommit -Label 'ExpectedSourceCommit'
    if ($ExpectedTotal -le 0) {
        throw "Exact-head GitHub Actions gate has no workflow runs for '$commit'."
    }

    $exactRuns = @($Runs | Where-Object { [string]$_.head_sha -eq $commit })
    if ($exactRuns.Count -ne $ExpectedTotal) {
        throw "Exact-head GitHub Actions run set is incomplete: expected $ExpectedTotal run(s), received $($exactRuns.Count)."
    }

    foreach ($requiredName in $RequiredWorkflowNames) {
        if (@($exactRuns | Where-Object { [string]$_.name -eq $requiredName }).Count -eq 0) {
            throw "Exact-head GitHub Actions gate is missing required workflow '$requiredName'."
        }
    }

    $notGreen = @($exactRuns | Where-Object {
        [string]$_.status -ne 'completed' -or [string]$_.conclusion -ne 'success'
    })
    if ($notGreen.Count -gt 0) {
        $details = ($notGreen | ForEach-Object {
            $runNumber = if ($null -ne $_.run_number) { [string]$_.run_number } else { '?' }
            "{0}#{1}:{2}/{3}" -f [string]$_.name, $runNumber, [string]$_.status, [string]$_.conclusion
        }) -join ', '
        throw "Exact-head GitHub Actions gate is not green for '$commit': $details"
    }

    return [pscustomobject]@{
        sourceCommit = $commit
        runCount = $exactRuns.Count
        requiredWorkflowCount = $RequiredWorkflowNames.Count
        state = 'green'
    }
}

function Get-ExactHeadCiProof {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][string[]]$RequiredWorkflowNames
    )

    Assert-RepositoryName -Name $Repo | Out-Null
    $commit = Assert-ExactCommit -Commit $SourceCommit -Label 'ExpectedSourceCommit'

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $gh) {
        throw "GitHub CLI (gh) is required to prove exact-head CI before public beta publication."
    }

    & $gh.Source auth status --hostname github.com *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI is not authenticated for github.com."
    }

    $repoIdentity = (& $gh.Source repo view $Repo --json nameWithOwner --jq '.nameWithOwner' 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or [string]$repoIdentity -ne $Repo) {
        throw "GitHub CLI cannot prove repository identity '$Repo'."
    }

    $runs = @()
    $expectedTotal = -1
    $page = 1
    while ($true) {
        if ($page -gt 100) {
            throw "Exact-head GitHub Actions pagination exceeded the fail-closed safety limit."
        }

        $endpoint = "repos/$Repo/actions/runs?head_sha=$commit&per_page=100&page=$page"
        $json = @(& $gh.Source api -H 'Accept: application/vnd.github+json' $endpoint 2>$null)
        $jsonText = ($json -join [Environment]::NewLine).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($jsonText)) {
            throw "Cannot read exact-head GitHub Actions state for '$commit' (page $page)."
        }

        try {
            $response = $jsonText | ConvertFrom-Json
        }
        catch {
            throw "GitHub Actions response for '$commit' is not valid JSON."
        }

        if ($page -eq 1) {
            try { $expectedTotal = [int]$response.total_count } catch { throw "GitHub Actions response is missing total_count." }
            if ($expectedTotal -le 0) {
                throw "Exact-head GitHub Actions gate has no workflow runs for '$commit'."
            }
        }

        $pageRuns = @($response.workflow_runs)
        $runs += $pageRuns
        if ($runs.Count -ge $expectedTotal) { break }
        if ($pageRuns.Count -eq 0) {
            throw "Exact-head GitHub Actions pagination ended before all $expectedTotal run(s) were read."
        }
        $page++
    }

    if ($runs.Count -ne $expectedTotal) {
        throw "Exact-head GitHub Actions pagination returned $($runs.Count) run(s), expected $expectedTotal."
    }

    return Assert-ExactHeadCiSnapshot -Runs $runs -SourceCommit $commit -ExpectedTotal $expectedTotal -RequiredWorkflowNames $RequiredWorkflowNames
}

function Get-PublisherArguments {
    param([Parameter(Mandatory = $true)][string]$Publisher)

    $arguments = @(
        '-NoProfile', '-NonInteractive', '-File', $Publisher,
        '-Mode', 'publish',
        '-CandidateMetadataPath', $CandidateMetadataPath,
        '-QaKitManifestPath', $QaKitManifestPath,
        '-PackagePath', $PackagePath,
        '-EvidencePath', $EvidencePath,
        '-ExpectedVersion', $ExpectedVersion,
        '-ExpectedSourceCommit', $ExpectedSourceCommit,
        '-ReleaseNotesTemplatePath', $ReleaseNotesTemplatePath,
        '-CapabilityMatrixPath', $CapabilityMatrixPath,
        '-OutputDirectory', $OutputDirectory,
        '-Repository', $Repository,
        '-TagName', $TagName,
        '-ProofScriptPath', $ProofScriptPath
    )

    if (-not [string]::IsNullOrWhiteSpace($CandidateMetadataChecksumFile)) {
        $arguments += @('-CandidateMetadataChecksumFile', $CandidateMetadataChecksumFile)
    }
    if (-not [string]::IsNullOrWhiteSpace($QaKitManifestChecksumFile)) {
        $arguments += @('-QaKitManifestChecksumFile', $QaKitManifestChecksumFile)
    }
    if (-not [string]::IsNullOrWhiteSpace($PackageChecksumFile)) {
        $arguments += @('-PackageChecksumFile', $PackageChecksumFile)
    }
    if (-not [string]::IsNullOrWhiteSpace($EvidenceChecksumFile)) {
        $arguments += @('-EvidenceChecksumFile', $EvidenceChecksumFile)
    }

    return $arguments
}

function Invoke-SelfTest {
    $commit = 'a' * 40
    $otherCommit = 'b' * 40
    $required = @(
        'Dragon DiskForge Build',
        'Dragon DiskForge Security Boundary',
        'Dragon DiskForge Beta Release Proof Contract',
        'Dragon DiskForge Beta Release Publish Contract'
    )

    $greenRuns = @(
        [pscustomobject]@{ name = $required[0]; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 1 },
        [pscustomobject]@{ name = $required[1]; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 2 },
        [pscustomobject]@{ name = $required[2]; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 3 },
        [pscustomobject]@{ name = $required[3]; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 4 },
        [pscustomobject]@{ name = 'Dragon DiskForge Extra Contract'; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 5 }
    )

    $proof = Assert-ExactHeadCiSnapshot -Runs $greenRuns -SourceCommit $commit -ExpectedTotal 5 -RequiredWorkflowNames $required
    if ([string]$proof.state -ne 'green' -or [int]$proof.runCount -ne 5) {
        throw 'Self-test failed: a complete green exact-head workflow set was not accepted.'
    }

    $pending = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $pending[4].status = 'in_progress'
    $pending[4].conclusion = $null
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $pending -SourceCommit $commit -ExpectedTotal 5 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: pending exact-head workflow was accepted.' }

    $failed = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $failed[1].conclusion = 'failure'
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $failed -SourceCommit $commit -ExpectedTotal 5 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: failed exact-head workflow was accepted.' }

    $missingRequired = @($greenRuns | Where-Object { [string]$_.name -ne $required[2] })
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $missingRequired -SourceCommit $commit -ExpectedTotal 4 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: missing required release-proof workflow was accepted.' }

    $foreignHead = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $foreignHead[4].head_sha = $otherCommit
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $foreignHead -SourceCommit $commit -ExpectedTotal 5 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: mixed-head workflow snapshot was accepted.' }

    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs @() -SourceCommit $commit -ExpectedTotal 0 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: empty exact-head workflow snapshot was accepted.' }

    Write-Host 'Dragon DiskForge exact-head beta publish gate self-test passed.'
}

switch ($Mode) {
    'self-test' {
        Invoke-SelfTest
        exit 0
    }

    'verify-ci' {
        $proof = Get-ExactHeadCiProof -Repo $Repository -SourceCommit $ExpectedSourceCommit -RequiredWorkflowNames $RequiredReleaseWorkflowNames
        Write-Host "Dragon DiskForge exact-head CI gate is green."
        Write-Host "Source commit: $($proof.sourceCommit)"
        Write-Host "Workflow runs: $($proof.runCount)"
        Write-Host "Required release workflows: $($proof.requiredWorkflowCount)"
        exit 0
    }

    'publish' {
        $proof = Get-ExactHeadCiProof -Repo $Repository -SourceCommit $ExpectedSourceCommit -RequiredWorkflowNames $RequiredReleaseWorkflowNames
        Write-Host "Exact-head CI verified green for $($proof.sourceCommit) across $($proof.runCount) workflow run(s)."

        $publisher = Resolve-File -Path $PublisherScriptPath -Label 'Beta release publisher script'
        $hostExecutable = Get-CurrentPowerShellExecutable
        $arguments = Get-PublisherArguments -Publisher $publisher
        & $hostExecutable @arguments
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "Verified beta publisher failed with exit code $exitCode."
        }
        exit 0
    }
}
