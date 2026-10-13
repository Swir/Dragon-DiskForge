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
    [string]$InstallerPath = "artifacts/installer/DragonDiskForge-0.5.0-beta.1-win-x64-setup.exe",
    [string]$InstallerChecksumFile = "",
    [string]$RetainedEvidencePath = "docs/retained-beta-candidate.json",
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
    'Dragon DiskForge Beta Release Publish Contract',
    'Dragon DiskForge Installer Contract'
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

    $byWorkflow = @{}
    foreach ($run in $exactRuns) {
        if ($null -eq $run.PSObject.Properties['workflow_id']) {
            throw "Exact-head GitHub Actions run is missing workflow_id."
        }
        if ($null -eq $run.PSObject.Properties['run_number']) {
            throw "Exact-head GitHub Actions run is missing run_number."
        }

        $workflowIdText = [string]$run.workflow_id
        $runNumberText = [string]$run.run_number
        [int64]$workflowId = 0
        [int64]$runNumber = 0
        if ([string]::IsNullOrWhiteSpace($workflowIdText) -or
            -not [int64]::TryParse($workflowIdText, [ref]$workflowId) -or
            $workflowId -le 0) {
            throw "Exact-head GitHub Actions run has invalid workflow_id '$workflowIdText'."
        }
        if ([string]::IsNullOrWhiteSpace($runNumberText) -or
            -not [int64]::TryParse($runNumberText, [ref]$runNumber) -or
            $runNumber -le 0) {
            throw "Exact-head GitHub Actions run has invalid run_number '$runNumberText'."
        }

        $key = [string]$workflowId
        if (-not $byWorkflow.ContainsKey($key)) {
            $byWorkflow[$key] = @()
        }
        $byWorkflow[$key] += [pscustomobject]@{
            workflowId = $workflowId
            runNumber = $runNumber
            run = $run
        }
    }

    $effectiveRuns = @()
    foreach ($key in $byWorkflow.Keys) {
        $group = @($byWorkflow[$key])
        $maxRunNumber = [int64](($group | Measure-Object -Property runNumber -Maximum).Maximum)
        $latest = @($group | Where-Object { [int64]$_.runNumber -eq $maxRunNumber })
        if ($latest.Count -ne 1) {
            throw "Exact-head GitHub Actions gate has ambiguous latest run for workflow_id '$key' at run_number '$maxRunNumber'."
        }
        $effectiveRuns += $latest[0].run
    }

    foreach ($requiredName in $RequiredWorkflowNames) {
        if (@($effectiveRuns | Where-Object { [string]$_.name -eq $requiredName }).Count -eq 0) {
            throw "Exact-head GitHub Actions gate is missing required workflow '$requiredName'."
        }
    }

    $notGreen = @($effectiveRuns | Where-Object {
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
        runCount = $effectiveRuns.Count
        rawRunCount = $exactRuns.Count
        effectiveRunCount = $effectiveRuns.Count
        supersededRunCount = $exactRuns.Count - $effectiveRuns.Count
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
        '-InstallerPath', $InstallerPath,
        '-RetainedEvidencePath', $RetainedEvidencePath,
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
    if (-not [string]::IsNullOrWhiteSpace($InstallerChecksumFile)) {
        $arguments += @('-InstallerChecksumFile', $InstallerChecksumFile)
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
        'Dragon DiskForge Beta Release Publish Contract',
        'Dragon DiskForge Installer Contract'
    )

    $greenRuns = @(
        [pscustomobject]@{ name = $required[0]; workflow_id = 1001; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 10 },
        [pscustomobject]@{ name = $required[1]; workflow_id = 1002; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 20 },
        [pscustomobject]@{ name = $required[2]; workflow_id = 1003; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 30 },
        [pscustomobject]@{ name = $required[3]; workflow_id = 1004; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 40 },
        [pscustomobject]@{ name = $required[4]; workflow_id = 1005; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 50 },
        [pscustomobject]@{ name = 'Dragon DiskForge Extra Contract'; workflow_id = 1006; head_sha = $commit; status = 'completed'; conclusion = 'success'; run_number = 60 }
    )

    $proof = Assert-ExactHeadCiSnapshot -Runs $greenRuns -SourceCommit $commit -ExpectedTotal 6 -RequiredWorkflowNames $required
    if ([string]$proof.state -ne 'green' -or
        [int]$proof.rawRunCount -ne 6 -or
        [int]$proof.effectiveRunCount -ne 6 -or
        [int]$proof.supersededRunCount -ne 0) {
        throw 'Self-test failed: a complete green exact-head workflow set was not accepted.'
    }

    $superseded = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $superseded += [pscustomobject]@{
        name = $required[0]
        workflow_id = 1001
        head_sha = $commit
        status = 'completed'
        conclusion = 'cancelled'
        run_number = 9
    }
    $proof = Assert-ExactHeadCiSnapshot -Runs $superseded -SourceCommit $commit -ExpectedTotal 7 -RequiredWorkflowNames $required
    if ([int]$proof.rawRunCount -ne 7 -or
        [int]$proof.effectiveRunCount -ne 6 -or
        [int]$proof.supersededRunCount -ne 1) {
        throw 'Self-test failed: superseded historical workflow run was not collapsed correctly.'
    }

    $newerFailure = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $newerFailure += [pscustomobject]@{
        name = $required[0]
        workflow_id = 1001
        head_sha = $commit
        status = 'completed'
        conclusion = 'failure'
        run_number = 11
    }
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $newerFailure -SourceCommit $commit -ExpectedTotal 7 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: newer failed workflow run did not override the older green run.' }

    $ambiguousLatest = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $ambiguousLatest += [pscustomobject]@{
        name = $required[0]
        workflow_id = 1001
        head_sha = $commit
        status = 'completed'
        conclusion = 'success'
        run_number = 10
    }
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $ambiguousLatest -SourceCommit $commit -ExpectedTotal 7 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: ambiguous latest workflow run was accepted.' }

    $missingWorkflowId = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $missingWorkflowId[0].PSObject.Properties.Remove('workflow_id')
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $missingWorkflowId -SourceCommit $commit -ExpectedTotal 6 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: missing workflow_id was accepted.' }

    $nonPositiveRunNumber = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $nonPositiveRunNumber[0].run_number = 0
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $nonPositiveRunNumber -SourceCommit $commit -ExpectedTotal 6 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: non-positive run_number was accepted.' }

    $pending = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $pending[4].status = 'in_progress'
    $pending[4].conclusion = $null
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $pending -SourceCommit $commit -ExpectedTotal 6 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: pending exact-head workflow was accepted.' }

    $failed = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $failed[1].conclusion = 'failure'
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $failed -SourceCommit $commit -ExpectedTotal 6 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: failed exact-head workflow was accepted.' }

    $missingRequired = @($greenRuns | Where-Object { [string]$_.name -ne $required[2] })
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $missingRequired -SourceCommit $commit -ExpectedTotal 5 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Self-test failed: missing required release-proof workflow was accepted.' }

    $foreignHead = @($greenRuns | ForEach-Object { $_.PSObject.Copy() })
    $foreignHead[4].head_sha = $otherCommit
    $rejected = $false
    try { Assert-ExactHeadCiSnapshot -Runs $foreignHead -SourceCommit $commit -ExpectedTotal 6 -RequiredWorkflowNames $required | Out-Null } catch { $rejected = $true }
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
