[CmdletBinding()]
param(
    [ValidateSet("status", "next", "verify-group", "self-test")]
    [string]$Mode = "status",

    [string]$EvidencePath = "artifacts/manual-qa/beta-manual-qa.json",
    [string]$PackagePath = "",
    [string]$ChecksumFile = "",
    [ValidateSet("desktop", "uac", "drag")]
    [string]$Group = "desktop",
    [string]$ExpectedVersion = "0.5.0-beta.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Script:EvidenceSchemaVersion = 3
$Script:EvidenceGateName = "Dragon DiskForge interactive beta QA"
$Script:MinObservationNoteLength = 12
$Script:MaxObservationNoteLength = 1000

function Get-RequiredChecks {
    return @(
        [pscustomobject]@{ id = "desktop.clean-launch"; group = "desktop"; description = "Clean supported Windows desktop launch succeeds from the packaged app without developer tooling." },
        [pscustomobject]@{ id = "desktop.basic-regression"; group = "desktop"; description = "Open, mount, explore, verify and analyze complete successfully in the packaged WinUI app." },
        [pscustomobject]@{ id = "uac.iso-no-prompt"; group = "uac"; description = "ISO mount/unmount completes without an unnecessary administrator prompt." },
        [pscustomobject]@{ id = "uac.vhd-cancel"; group = "uac"; description = "Cancelling VHD elevation leaves the image detached and reports cancellation cleanly." },
        [pscustomobject]@{ id = "uac.vhd-approve-readonly"; group = "uac"; description = "Approving VHD elevation mounts read-only and refreshes the real mounted state." },
        [pscustomobject]@{ id = "uac.vhdx-cancel"; group = "uac"; description = "Cancelling VHDX elevation leaves the image detached and reports cancellation cleanly." },
        [pscustomobject]@{ id = "uac.vhdx-approve-readonly"; group = "uac"; description = "Approving VHDX elevation mounts read-only and refreshes the real mounted state." },
        [pscustomobject]@{ id = "uac.unmount-refresh"; group = "uac"; description = "VHD/VHDX unmount requests elevation only when required and refreshes to the real detached state." },
        [pscustomobject]@{ id = "drag.file-explorer-copy"; group = "drag"; description = "A file dragged from Dragon Explorer to Windows Explorer lands as Copy and source remains unchanged." },
        [pscustomobject]@{ id = "drag.folder-explorer-copy"; group = "drag"; description = "A folder dragged from Dragon Explorer to Windows Explorer lands as Copy and source remains unchanged." },
        [pscustomobject]@{ id = "drag.desktop-copy"; group = "drag"; description = "Drag-out to the Windows desktop behaves as Copy and preserves the mounted source." },
        [pscustomobject]@{ id = "drag.cancel-no-mutation"; group = "drag"; description = "Cancelling an in-progress drag creates no destination item and no false success state." },
        [pscustomobject]@{ id = "drag.reparse-blocked"; group = "drag"; description = "A reparse point or junction source is blocked from drag-out." },
        [pscustomobject]@{ id = "drag.stale-blocked"; group = "drag"; description = "A stale or invalidated source cannot be dragged successfully after state refresh." }
    )
}

function Assert-Sha256Sidecar {
    param(
        [Parameter(Mandatory = $true)][string]$PayloadPath,
        [string]$SidecarPath = ""
    )

    $payload = (Resolve-Path -LiteralPath $PayloadPath).Path
    if ([string]::IsNullOrWhiteSpace($SidecarPath)) {
        $SidecarPath = "$payload.sha256"
    }
    $sidecar = (Resolve-Path -LiteralPath $SidecarPath).Path
    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "SHA-256 sidecar has an invalid format: $sidecar"
    }

    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    if ($declaredName -ne [System.IO.Path]::GetFileName($payload)) {
        throw "SHA-256 sidecar targets '$declaredName' instead of '$([System.IO.Path]::GetFileName($payload))'."
    }

    $actualHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "SHA-256 mismatch for '$payload'."
    }
    return $actualHash
}

function Get-PackageIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][string]$Version
    )

    $zip = (Resolve-Path -LiteralPath $ZipPath).Path
    $zipHash = Assert-Sha256Sidecar -PayloadPath $zip -SidecarPath $SidecarPath
    $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-manual-gate-status-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    try {
        Expand-Archive -LiteralPath $zip -DestinationPath $workspace -Force
        $manifestPath = Join-Path $workspace "package-manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Package manifest is missing."
        }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([int]$manifest.schemaVersion -lt 5) {
            throw "Package manifest schema '$($manifest.schemaVersion)' predates the beta manual-QA contract."
        }
        if ([string]$manifest.product -ne "Dragon DiskForge") {
            throw "Unexpected package product '$($manifest.product)'."
        }
        if ([string]$manifest.version -ne $Version) {
            throw "Package version '$($manifest.version)' does not match required version '$Version'."
        }
        if ([string]$manifest.architecture -ne "x64") {
            throw "Unexpected package architecture '$($manifest.architecture)'."
        }

        $entryPoint = Join-Path $workspace ([string]$manifest.entryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
            throw "Package desktop entry point is missing."
        }
        $entryHash = (Get-FileHash -LiteralPath $entryPoint -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($entryHash -ne ([string]$manifest.entryPointSha256).ToLowerInvariant()) {
            throw "Package desktop entry-point SHA-256 does not match the manifest."
        }

        $qaRelative = ([string]$manifest.betaManualQaEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        if ([string]::IsNullOrWhiteSpace($qaRelative)) {
            throw "Package manifest does not declare the beta manual-QA entry point."
        }
        $qaPath = Join-Path $workspace $qaRelative
        if (-not (Test-Path -LiteralPath $qaPath -PathType Leaf)) {
            throw "Packaged beta manual-QA tool is missing."
        }
        $qaHash = (Get-FileHash -LiteralPath $qaPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($qaHash -ne ([string]$manifest.betaManualQaEntryPointSha256).ToLowerInvariant()) {
            throw "Packaged beta manual-QA tool SHA-256 does not match the manifest."
        }

        return [pscustomobject]@{
            fileName = [System.IO.Path]::GetFileName($zip)
            sha256 = $zipHash
            version = [string]$manifest.version
            architecture = [string]$manifest.architecture
            entryPointSha256 = $entryHash
            betaManualQaEntryPointSha256 = $qaHash
        }
    }
    finally {
        if (Test-Path -LiteralPath $workspace) {
            Remove-Item -LiteralPath $workspace -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Assert-PassRecord {
    param(
        [Parameter(Mandatory = $true)]$Record,
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)]$PackageIdentity
    )

    $id = [string]$Record.id
    if (-not [bool]$Record.humanConfirmed) {
        throw "Manual QA check '$id' is pass but is not explicitly human-confirmed."
    }

    $note = if ($null -eq $Record.note) { "" } else { ([string]$Record.note).Trim() }
    if ($note.Length -lt $Script:MinObservationNoteLength -or $note.Length -gt $Script:MaxObservationNoteLength) {
        throw "Manual QA check '$id' has an invalid observation-note length."
    }

    if ([string]$Record.packageVersion -ne [string]$PackageIdentity.version -or
        ([string]$Record.packageSha256).ToLowerInvariant() -ne ([string]$PackageIdentity.sha256).ToLowerInvariant() -or
        ([string]$Record.packageEntryPointSha256).ToLowerInvariant() -ne ([string]$PackageIdentity.entryPointSha256).ToLowerInvariant() -or
        ([string]$Record.runningQaToolSha256).ToLowerInvariant() -ne ([string]$PackageIdentity.betaManualQaEntryPointSha256).ToLowerInvariant()) {
        throw "Manual QA check '$id' is not bound to this exact package/tool identity."
    }

    if ([string]::IsNullOrWhiteSpace([string]$Record.observedUtc)) {
        throw "Manual QA check '$id' is missing its observation timestamp."
    }
    try { [DateTimeOffset]::Parse([string]$Record.observedUtc) | Out-Null } catch { throw "Manual QA check '$id' has an invalid observation timestamp." }

    if (-not [bool]$Record.observedInteractive -or [int]$Record.observedSessionId -le 0) {
        throw "Manual QA check '$id' was not recorded in an interactive desktop session."
    }
    if ([int]$Record.observedSessionId -ne [int]$Evidence.createdEnvironment.sessionId) {
        throw "Manual QA check '$id' was recorded in a different desktop session."
    }
    if ($null -eq $Record.observedProcessElevated -or [bool]$Record.observedProcessElevated) {
        throw "Manual QA check '$id' was not recorded from a provably unelevated process."
    }
    if ($null -eq $Record.observedUacEnabled -or -not [bool]$Record.observedUacEnabled) {
        throw "Manual QA check '$id' cannot prove UAC was enabled."
    }
    if ([string]$Record.observedOsBuild -ne [string]$Evidence.createdEnvironment.osBuild -or
        [string]$Record.observedProcessArchitecture -ne [string]$Evidence.createdEnvironment.processArchitecture) {
        throw "Manual QA check '$id' does not match the baseline Windows build/process architecture."
    }
}

function Assert-EvidenceContract {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)]$PackageIdentity,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ([int]$Evidence.schemaVersion -ne $Script:EvidenceSchemaVersion -or [string]$Evidence.gate -ne $Script:EvidenceGateName) {
        throw "Manual-QA evidence identity/schema is invalid."
    }
    if ([string]$Evidence.targetVersion -ne $Version -or [string]$Evidence.package.version -ne $Version) {
        throw "Manual-QA evidence targets a different version."
    }
    if (([string]$Evidence.package.sha256).ToLowerInvariant() -ne ([string]$PackageIdentity.sha256).ToLowerInvariant() -or
        ([string]$Evidence.package.entryPointSha256).ToLowerInvariant() -ne ([string]$PackageIdentity.entryPointSha256).ToLowerInvariant() -or
        ([string]$Evidence.package.betaManualQaEntryPointSha256).ToLowerInvariant() -ne ([string]$PackageIdentity.betaManualQaEntryPointSha256).ToLowerInvariant() -or
        ([string]$Evidence.createdQaToolSha256).ToLowerInvariant() -ne ([string]$PackageIdentity.betaManualQaEntryPointSha256).ToLowerInvariant()) {
        throw "Manual-QA evidence is bound to a different package or QA tool."
    }

    $baseline = $Evidence.createdEnvironment
    if (-not [bool]$baseline.userInteractive -or [int]$baseline.sessionId -le 0) {
        throw "Manual-QA evidence baseline is not an interactive desktop session."
    }
    if ($null -eq $baseline.processElevated -or [bool]$baseline.processElevated) {
        throw "Manual-QA evidence baseline is not provably unelevated."
    }
    if ($null -eq $baseline.uacEnabled -or -not [bool]$baseline.uacEnabled) {
        throw "Manual-QA evidence baseline cannot prove UAC is enabled."
    }

    $catalog = @(Get-RequiredChecks)
    $records = @($Evidence.checks)
    if ($records.Count -ne $catalog.Count) {
        throw "Manual-QA evidence has $($records.Count) checks; expected $($catalog.Count)."
    }

    foreach ($definition in $catalog) {
        $matches = @($records | Where-Object { [string]$_.id -eq [string]$definition.id })
        if ($matches.Count -ne 1) {
            throw "Manual-QA evidence must contain exactly one '$($definition.id)' record."
        }
        $record = $matches[0]
        if ([string]$record.group -ne [string]$definition.group) {
            throw "Manual-QA check '$($definition.id)' is assigned to the wrong group."
        }
        if ([string]$record.status -notin @("pending", "pass", "fail")) {
            throw "Manual-QA check '$($definition.id)' has unsupported status '$($record.status)'."
        }
        if ([string]$record.status -eq "pass") {
            Assert-PassRecord -Record $record -Evidence $Evidence -PackageIdentity $PackageIdentity
        }
    }
}

function Get-GroupResult {
    param(
        [Parameter(Mandatory = $true)]$Evidence,
        [Parameter(Mandatory = $true)][string]$GroupName
    )

    $requiredIds = @((Get-RequiredChecks) | Where-Object { $_.group -eq $GroupName } | ForEach-Object { $_.id })
    $records = @($Evidence.checks | Where-Object { [string]$_.group -eq $GroupName })
    $passed = @($records | Where-Object { [string]$_.status -eq "pass" }).Count
    $failed = @($records | Where-Object { [string]$_.status -eq "fail" }).Count
    $pending = @($records | Where-Object { [string]$_.status -eq "pending" }).Count
    return [pscustomobject]@{
        group = $GroupName
        required = $requiredIds.Count
        passed = $passed
        failed = $failed
        pending = $pending
        complete = ($passed -eq $requiredIds.Count -and $failed -eq 0 -and $pending -eq 0)
    }
}

function Get-UnresolvedChecks {
    param([Parameter(Mandatory = $true)]$Evidence)

    $catalog = @(Get-RequiredChecks)
    $unresolved = @()
    for ($index = 0; $index -lt $catalog.Count; $index++) {
        $definition = $catalog[$index]
        $record = @($Evidence.checks | Where-Object { [string]$_.id -eq [string]$definition.id })[0]
        if ([string]$record.status -eq "pass") {
            continue
        }

        $priority = if ([string]$record.status -eq "fail") { 0 } else { 1 }
        $unresolved += [pscustomobject]@{
            id = [string]$definition.id
            group = [string]$definition.group
            description = [string]$definition.description
            status = [string]$record.status
            priority = $priority
            order = $index
        }
    }

    return @($unresolved | Sort-Object priority, order)
}

function Copy-Object {
    param([Parameter(Mandatory = $true)]$Value)
    return ($Value | ConvertTo-Json -Depth 12 | ConvertFrom-Json)
}

function Assert-Throws {
    param([Parameter(Mandatory = $true)][scriptblock]$Action, [Parameter(Mandatory = $true)][string]$Name)
    $threw = $false
    try { & $Action } catch { $threw = $true }
    if (-not $threw) { throw "Self-test failed: '$Name' did not fail closed." }
}

function New-SelfTestEvidence {
    param([Parameter(Mandatory = $true)]$PackageIdentity)
    $environment = [pscustomobject]@{
        osBuild = "self-test-build"
        processArchitecture = "X64"
        userInteractive = $true
        processElevated = $false
        sessionId = 7
        uacEnabled = $true
    }
    $checks = @()
    foreach ($definition in Get-RequiredChecks) {
        $checks += [pscustomobject]@{
            id = $definition.id
            group = $definition.group
            description = "Self-test"
            status = "pass"
            humanConfirmed = $true
            packageVersion = $PackageIdentity.version
            packageSha256 = $PackageIdentity.sha256
            packageEntryPointSha256 = $PackageIdentity.entryPointSha256
            runningQaToolSha256 = $PackageIdentity.betaManualQaEntryPointSha256
            observedUtc = [DateTimeOffset]::UtcNow.ToString("O")
            observedOsBuild = $environment.osBuild
            observedProcessArchitecture = $environment.processArchitecture
            observedSessionId = $environment.sessionId
            observedInteractive = $true
            observedProcessElevated = $false
            observedUacEnabled = $true
            note = "Self-test human observation."
        }
    }
    return [pscustomobject]@{
        schemaVersion = $Script:EvidenceSchemaVersion
        gate = $Script:EvidenceGateName
        targetVersion = $PackageIdentity.version
        package = $PackageIdentity
        createdQaToolSha256 = $PackageIdentity.betaManualQaEntryPointSha256
        createdEnvironment = $environment
        checks = $checks
    }
}

function Invoke-SelfTest {
    $package = [pscustomobject]@{
        fileName = "DragonDiskForge-win-x64.zip"
        sha256 = ("a" * 64)
        version = "0.5.0-beta.1"
        architecture = "x64"
        entryPointSha256 = ("b" * 64)
        betaManualQaEntryPointSha256 = ("c" * 64)
    }
    $evidence = New-SelfTestEvidence -PackageIdentity $package
    Assert-EvidenceContract -Evidence $evidence -PackageIdentity $package -Version $package.version

    foreach ($name in @("desktop", "uac", "drag")) {
        $result = Get-GroupResult -Evidence $evidence -GroupName $name
        if (-not $result.complete) { throw "Self-test failed: all-pass '$name' group was not complete." }
    }

    $pending = Copy-Object -Value $evidence
    ($pending.checks | Where-Object { $_.id -eq "uac.vhd-cancel" }).status = "pending"
    Assert-EvidenceContract -Evidence $pending -PackageIdentity $package -Version $package.version
    if ((Get-GroupResult -Evidence $pending -GroupName "uac").complete) { throw "Self-test failed: pending UAC evidence was marked complete." }
    if (-not (Get-GroupResult -Evidence $pending -GroupName "desktop").complete) { throw "Self-test failed: independent desktop completion was lost." }

    $nextPending = @(Get-UnresolvedChecks -Evidence $pending)
    if ($nextPending.Count -ne 1 -or [string]$nextPending[0].id -ne "uac.vhd-cancel") {
        throw "Self-test failed: next-action selection did not identify the only pending check."
    }

    $failedFirst = Copy-Object -Value $pending
    ($failedFirst.checks | Where-Object { $_.id -eq "drag.desktop-copy" }).status = "fail"
    $nextFailed = @(Get-UnresolvedChecks -Evidence $failedFirst)
    if ($nextFailed.Count -ne 2 -or [string]$nextFailed[0].id -ne "drag.desktop-copy" -or [string]$nextFailed[0].status -ne "fail") {
        throw "Self-test failed: failed observations were not prioritized ahead of pending observations."
    }

    $shortNote = Copy-Object -Value $evidence
    ($shortNote.checks | Where-Object { $_.id -eq "drag.desktop-copy" }).note = "too short"
    Assert-Throws -Name "short pass note" -Action { Assert-EvidenceContract -Evidence $shortNote -PackageIdentity $package -Version $package.version }

    $crossSession = Copy-Object -Value $evidence
    ($crossSession.checks | Where-Object { $_.id -eq "uac.iso-no-prompt" }).observedSessionId = 99
    Assert-Throws -Name "cross-session pass" -Action { Assert-EvidenceContract -Evidence $crossSession -PackageIdentity $package -Version $package.version }

    $duplicate = Copy-Object -Value $evidence
    $duplicate.checks = @($duplicate.checks) + @($duplicate.checks[0])
    Assert-Throws -Name "duplicate check" -Action { Assert-EvidenceContract -Evidence $duplicate -PackageIdentity $package -Version $package.version }

    $mismatchPackage = Copy-Object -Value $package
    $mismatchPackage.sha256 = ("d" * 64)
    Assert-Throws -Name "package mismatch" -Action { Assert-EvidenceContract -Evidence $evidence -PackageIdentity $mismatchPackage -Version $package.version }

    $notHuman = Copy-Object -Value $evidence
    ($notHuman.checks | Where-Object { $_.id -eq "desktop.clean-launch" }).humanConfirmed = $false
    Assert-Throws -Name "non-human pass" -Action { Assert-EvidenceContract -Evidence $notHuman -PackageIdentity $package -Version $package.version }

    Write-Host "Manual gate status contract self-test passed."
}

if ($Mode -eq "self-test") {
    Invoke-SelfTest
    exit 0
}

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    throw "-PackagePath is required so group status remains bound to the exact retained candidate."
}
if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
    throw "Manual-QA evidence file is missing: $EvidencePath"
}

$evidenceHash = Assert-Sha256Sidecar -PayloadPath $EvidencePath
$evidence = Get-Content -LiteralPath (Resolve-Path -LiteralPath $EvidencePath).Path -Raw | ConvertFrom-Json
$identity = Get-PackageIdentity -ZipPath $PackagePath -SidecarPath $ChecksumFile -Version $ExpectedVersion
Assert-EvidenceContract -Evidence $evidence -PackageIdentity $identity -Version $ExpectedVersion

if ($Mode -eq "next") {
    $unresolved = @(Get-UnresolvedChecks -Evidence $evidence)
    if ($unresolved.Count -eq 0) {
        Write-Host "All 14 interactive manual-QA checks are complete for this exact package/evidence set."
        Write-Host "This does not by itself publish or authorize the beta release."
        exit 0
    }

    $next = $unresolved[0]
    Write-Host "NEXT MANUAL QA CHECK"
    Write-Host "ID: $($next.id)"
    Write-Host "Group: $($next.group)"
    Write-Host "Current status: $($next.status)"
    Write-Host "Observe: $($next.description)"
    Write-Host ""
    Write-Host "Record only after a real human observation, in the same unelevated Windows session, using the exact packaged tools\beta-manual-qa.ps1."
    Write-Host "Example record command:"
    Write-Host ".\tools\beta-manual-qa.ps1 -Mode record -EvidencePath <evidence.json> -PackagePath <DragonDiskForge-win-x64.zip> -ChecksumFile <DragonDiskForge-win-x64.zip.sha256> -Check '$($next.id)' -Result pass -HumanConfirmed -Note '<what you observed>'"
    Write-Host ""
    Write-Host "Remaining unresolved checks: $($unresolved.Count)"
    exit 0
}

if ($Mode -eq "status") {
    foreach ($name in @("desktop", "uac", "drag")) {
        $result = Get-GroupResult -Evidence $evidence -GroupName $name
        $state = if ($result.complete) { "COMPLETE" } elseif ($result.failed -gt 0) { "FAILED" } else { "PENDING" }
        Write-Host ("{0,-8} {1,-8} {2}/{3} passed; {4} failed; {5} pending" -f $name, $state, $result.passed, $result.required, $result.failed, $result.pending)
    }
    $unresolved = @(Get-UnresolvedChecks -Evidence $evidence)
    if ($unresolved.Count -gt 0) {
        Write-Host "Unresolved checks:"
        foreach ($item in $unresolved) {
            Write-Host ("- [{0}] {1} ({2}) - {3}" -f $item.status.ToUpperInvariant(), $item.id, $item.group, $item.description)
        }
    }
    else {
        Write-Host "Unresolved checks: none"
    }
    Write-Host "Package SHA-256: $($identity.sha256)"
    Write-Host "Evidence SHA-256: $evidenceHash"
    Write-Host "Group status is evidence classification only; it does not claim full beta readiness."
    exit 0
}

$result = Get-GroupResult -Evidence $evidence -GroupName $Group
if (-not $result.complete) {
    throw "Manual QA group '$Group' is not complete: $($result.passed)/$($result.required) passed, $($result.failed) failed, $($result.pending) pending."
}

Write-Host "Manual QA group '$Group' is COMPLETE for the exact package and schema-v3 evidence."
Write-Host "Package SHA-256: $($identity.sha256)"
Write-Host "Evidence SHA-256: $evidenceHash"
Write-Host "This proves only the selected manual-QA group; other groups and the full beta release gate remain independent."
