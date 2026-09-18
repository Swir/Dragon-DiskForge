[CmdletBinding()]
param(
    [ValidateSet("prepare", "publish", "self-test")]
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
    [string]$ProofScriptPath = "scripts/beta-release-proof.ps1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

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

function Assert-VersionAndTag {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Tag
    )

    if ($Version -notmatch '^0\.[0-9]+\.[0-9]+-beta\.[0-9]+$') {
        throw "ExpectedVersion '$Version' is not a supported Dragon DiskForge beta version."
    }
    if ($Tag -ne $Version) {
        throw "TagName '$Tag' must exactly match ExpectedVersion '$Version'."
    }
    return $Version
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

function Get-FileProof {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string]$SidecarPath = "",
        [Parameter(Mandatory = $true)][string]$Label
    )

    $file = Resolve-File -Path $FilePath -Label $Label
    if ([string]::IsNullOrWhiteSpace($SidecarPath)) {
        $SidecarPath = "$file.sha256"
    }
    $sidecar = Resolve-File -Path $SidecarPath -Label "$Label SHA-256 sidecar"

    $line = (Get-Content -LiteralPath $sidecar -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "$Label SHA-256 sidecar has an invalid format."
    }

    $declaredHash = $Matches[1].ToLowerInvariant()
    $declaredName = $Matches[2].Trim()
    $fileName = [System.IO.Path]::GetFileName($file)
    if ($declaredName -ne $fileName) {
        throw "$Label SHA-256 sidecar targets '$declaredName' instead of '$fileName'."
    }

    $actualHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $declaredHash) {
        throw "$Label SHA-256 mismatch. Expected '$declaredHash', actual '$actualHash'."
    }

    return [pscustomobject]@{
        path = $file
        sidecar = $sidecar
        fileName = $fileName
        sha256 = $actualHash
    }
}

function Get-PlainFileProof {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $file = Resolve-File -Path $FilePath -Label $Label
    return [pscustomobject]@{
        path = $file
        fileName = [System.IO.Path]::GetFileName($file)
        sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    }
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
    throw "Cannot locate a PowerShell executable for isolated release-proof verification."
}

function Invoke-ReleaseProof {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [string]$CandidateChecksumPath,
        [Parameter(Mandatory = $true)][string]$QaKitPath,
        [string]$QaKitChecksumPath,
        [Parameter(Mandatory = $true)][string]$PackageFile,
        [string]$PackageChecksumPath,
        [Parameter(Mandatory = $true)][string]$EvidenceFile,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$SourceCommit
    )

    $proofScript = Resolve-File -Path $ScriptPath -Label "Beta release proof script"
    $hostExecutable = Get-CurrentPowerShellExecutable

    $arguments = @(
        '-NoProfile',
        '-NonInteractive',
        '-File', $proofScript,
        '-Mode', 'verify',
        '-CandidateMetadataPath', $CandidatePath,
        '-QaKitManifestPath', $QaKitPath,
        '-PackagePath', $PackageFile,
        '-EvidencePath', $EvidenceFile,
        '-ExpectedVersion', $Version,
        '-ExpectedSourceCommit', $SourceCommit
    )
    if (-not [string]::IsNullOrWhiteSpace($CandidateChecksumPath)) {
        $arguments += @('-CandidateMetadataChecksumFile', $CandidateChecksumPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($QaKitChecksumPath)) {
        $arguments += @('-QaKitManifestChecksumFile', $QaKitChecksumPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($PackageChecksumPath)) {
        $arguments += @('-PackageChecksumFile', $PackageChecksumPath)
    }

    & $hostExecutable @arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Beta release proof failed with exit code $exitCode. Release preparation is blocked."
    }
}

function Get-CandidateSummary {
    param(
        [Parameter(Mandatory = $true)]$CandidateProof,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][string]$PackageSha256
    )

    $metadata = Get-Content -LiteralPath $CandidateProof.path -Raw | ConvertFrom-Json
    if ([int]$metadata.schemaVersion -ne 1) { throw "Unsupported beta candidate metadata schema '$($metadata.schemaVersion)'." }
    if ([string]$metadata.kind -ne 'DragonDiskForgeBetaCandidate') { throw "Unexpected beta candidate metadata kind '$($metadata.kind)'." }
    if ([string]$metadata.product -ne 'Dragon DiskForge') { throw "Unexpected beta candidate product '$($metadata.product)'." }
    if ([string]$metadata.version -ne $Version) { throw "Candidate version '$($metadata.version)' does not match '$Version'." }
    if ([string]$metadata.architecture -ne 'x64') { throw "Only the verified x64 beta package can be promoted by this script." }
    if ([bool]$metadata.publicRelease) { throw "Retained candidate metadata already claims public release state; refusing promotion from ambiguous evidence." }

    $candidateCommit = Assert-ExactCommit -Commit ([string]$metadata.sourceCommit) -Label 'Candidate sourceCommit'
    if ($candidateCommit -ne $SourceCommit) { throw "Candidate source commit '$candidateCommit' does not match expected source '$SourceCommit'." }
    if ([string]$metadata.workflowRunId -notmatch '^[0-9]+$') { throw "Candidate workflowRunId is missing or invalid." }
    if ([string]$metadata.packageSha256 -ne $PackageSha256) { throw "Candidate metadata package SHA-256 does not match the supplied package." }
    if ([string]::IsNullOrWhiteSpace([string]$metadata.createdUtc)) { throw "Candidate metadata createdUtc is missing." }
    try { [DateTimeOffset]::Parse([string]$metadata.createdUtc) | Out-Null } catch { throw "Candidate metadata createdUtc is invalid." }

    return [pscustomobject]@{
        sourceCommit = $candidateCommit
        workflowRunId = [string]$metadata.workflowRunId
        packageSha256 = [string]$metadata.packageSha256
        entryPointSha256 = [string]$metadata.entryPointSha256
        betaManualQaEntryPointSha256 = [string]$metadata.betaManualQaEntryPointSha256
        createdUtc = [string]$metadata.createdUtc
    }
}

function Expand-ReleaseNotesTemplate {
    param(
        [Parameter(Mandatory = $true)][string]$TemplatePath,
        [Parameter(Mandatory = $true)][hashtable]$Values
    )

    $template = Get-Content -LiteralPath (Resolve-File -Path $TemplatePath -Label 'Release notes template') -Raw
    foreach ($key in $Values.Keys) {
        $token = '{{' + $key + '}}'
        $template = $template.Replace($token, [string]$Values[$key])
    }

    if ($template -match '\{\{[A-Z0-9_]+\}\}') {
        throw "Release notes template still contains an unresolved token '$($Matches[0])'."
    }
    return $template
}

function Get-ReleaseCreateArguments {
    param(
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$NotesPath,
        [Parameter(Mandatory = $true)][string[]]$Assets
    )

    $args = @(
        'release', 'create', $Tag,
        '--repo', $Repo,
        '--target', $SourceCommit,
        '--title', "Dragon DiskForge $Version",
        '--notes-file', $NotesPath,
        '--prerelease'
    )
    $args += $Assets
    return $args
}

function Prepare-ReleaseBundle {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [string]$CandidateChecksumPath,
        [Parameter(Mandatory = $true)][string]$QaKitPath,
        [string]$QaKitChecksumPath,
        [Parameter(Mandatory = $true)][string]$PackageFile,
        [string]$PackageChecksumPath,
        [Parameter(Mandatory = $true)][string]$EvidenceFile,
        [string]$EvidenceChecksumPath,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][string]$TemplatePath,
        [Parameter(Mandatory = $true)][string]$CapabilitiesPath,
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$ReleaseProofScript
    )

    Assert-VersionAndTag -Version $Version -Tag $Tag | Out-Null
    Assert-RepositoryName -Name $Repo | Out-Null
    $commit = Assert-ExactCommit -Commit $SourceCommit -Label 'ExpectedSourceCommit'

    Invoke-ReleaseProof -ScriptPath $ReleaseProofScript -CandidatePath $CandidatePath -CandidateChecksumPath $CandidateChecksumPath -QaKitPath $QaKitPath -QaKitChecksumPath $QaKitChecksumPath -PackageFile $PackageFile -PackageChecksumPath $PackageChecksumPath -EvidenceFile $EvidenceFile -Version $Version -SourceCommit $commit

    $candidateProof = Get-FileProof -FilePath $CandidatePath -SidecarPath $CandidateChecksumPath -Label 'Candidate metadata'
    $qaKitProof = Get-FileProof -FilePath $QaKitPath -SidecarPath $QaKitChecksumPath -Label 'QA kit manifest'
    $packageProof = Get-FileProof -FilePath $PackageFile -SidecarPath $PackageChecksumPath -Label 'Candidate package'
    $evidenceProof = Get-FileProof -FilePath $EvidenceFile -SidecarPath $EvidenceChecksumPath -Label 'Manual QA evidence'
    $capabilitiesProof = Get-PlainFileProof -FilePath $CapabilitiesPath -Label 'Supported capability matrix'
    $candidate = Get-CandidateSummary -CandidateProof $candidateProof -Version $Version -SourceCommit $commit -PackageSha256 $packageProof.sha256

    $root = [System.IO.Path]::GetFullPath($OutputRoot)
    $releaseDirectory = Join-Path $root $Version
    $temporaryDirectory = "$releaseDirectory.tmp.$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null

    try {
        $releasePackageName = "DragonDiskForge-$Version-win-x64.zip"
        $releasePackagePath = Join-Path $temporaryDirectory $releasePackageName
        Copy-Item -LiteralPath $packageProof.path -Destination $releasePackagePath -Force
        $releasePackageHash = (Get-FileHash -LiteralPath $releasePackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($releasePackageHash -ne $packageProof.sha256) {
            throw "Release package copy changed SHA-256 unexpectedly."
        }
        $releasePackageSidecarPath = "$releasePackagePath.sha256"
        Write-Utf8NoBom -Path $releasePackageSidecarPath -Text ("{0}  {1}{2}" -f $releasePackageHash, $releasePackageName, [Environment]::NewLine)

        $capabilityFileName = "SUPPORTED-CAPABILITIES-$Version.md"
        $releaseCapabilityPath = Join-Path $temporaryDirectory $capabilityFileName
        Copy-Item -LiteralPath $capabilitiesProof.path -Destination $releaseCapabilityPath -Force
        $releaseCapabilityHash = (Get-FileHash -LiteralPath $releaseCapabilityPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($releaseCapabilityHash -ne $capabilitiesProof.sha256) {
            throw "Release capability-matrix copy changed SHA-256 unexpectedly."
        }

        $notesFileName = "RELEASE-NOTES-$Version.md"
        $notesPath = Join-Path $temporaryDirectory $notesFileName
        $notes = Expand-ReleaseNotesTemplate -TemplatePath $TemplatePath -Values @{
            VERSION = $Version
            TAG = $Tag
            SOURCE_COMMIT = $commit
            CANDIDATE_RUN_ID = $candidate.workflowRunId
            PACKAGE_SHA256 = $releasePackageHash
            MANUAL_QA_SHA256 = $evidenceProof.sha256
            CAPABILITY_MATRIX_SHA256 = $capabilitiesProof.sha256
            REPOSITORY = $Repo
        }
        Write-Utf8NoBom -Path $notesPath -Text ($notes.TrimEnd() + [Environment]::NewLine)
        $notesHash = (Get-FileHash -LiteralPath $notesPath -Algorithm SHA256).Hash.ToLowerInvariant()

        $manifestFileName = "release-manifest-$Version.json"
        $manifestPath = Join-Path $temporaryDirectory $manifestFileName
        $manifest = [ordered]@{
            schemaVersion = 1
            kind = 'DragonDiskForgeBetaReleaseBundle'
            product = 'Dragon DiskForge'
            version = $Version
            tag = $Tag
            releaseKind = 'github-prerelease'
            architecture = 'x64'
            sourceCommit = $commit
            candidateWorkflowRunId = $candidate.workflowRunId
            candidateMetadataSha256 = $candidateProof.sha256
            qaKitManifestSha256 = $qaKitProof.sha256
            manualQaEvidenceSha256 = $evidenceProof.sha256
            packageFile = $releasePackageName
            packageSha256 = $releasePackageHash
            releaseNotesFile = $notesFileName
            releaseNotesSha256 = $notesHash
            capabilityMatrixRepositoryPath = 'docs/SUPPORTED-CAPABILITIES.md'
            capabilityMatrixReleaseFile = $capabilityFileName
            capabilityMatrixSha256 = $releaseCapabilityHash
            proofState = 'verified'
            publicationIntent = 'public-github-prerelease'
            candidateCreatedUtc = $candidate.createdUtc
        }
        Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
        $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $manifestSidecarPath = "$manifestPath.sha256"
        Write-Utf8NoBom -Path $manifestSidecarPath -Text ("{0}  {1}{2}" -f $manifestHash, $manifestFileName, [Environment]::NewLine)

        if (Test-Path -LiteralPath $releaseDirectory) {
            Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
        }
        $parent = Split-Path -Parent $releaseDirectory
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Move-Item -LiteralPath $temporaryDirectory -Destination $releaseDirectory

        return [pscustomobject]@{
            directory = $releaseDirectory
            packagePath = Join-Path $releaseDirectory $releasePackageName
            packageChecksumPath = Join-Path $releaseDirectory "$releasePackageName.sha256"
            packageSha256 = $releasePackageHash
            notesPath = Join-Path $releaseDirectory $notesFileName
            notesSha256 = $notesHash
            manifestPath = Join-Path $releaseDirectory $manifestFileName
            manifestChecksumPath = Join-Path $releaseDirectory "$manifestFileName.sha256"
            manifestSha256 = $manifestHash
            capabilityMatrixPath = Join-Path $releaseDirectory $capabilityFileName
            capabilityMatrixSha256 = $releaseCapabilityHash
            sourceCommit = $commit
            tag = $Tag
            candidateWorkflowRunId = $candidate.workflowRunId
            manualQaEvidenceSha256 = $evidenceProof.sha256
        }
    }
    catch {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

function Publish-ReleaseBundle {
    param(
        [Parameter(Mandatory = $true)]$Bundle,
        [Parameter(Mandatory = $true)][string]$Repo,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $gh) {
        throw "GitHub CLI (gh) is required for publish mode. Prepare mode remains available without it."
    }

    & $gh.Source auth status --hostname github.com
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI is not authenticated for github.com."
    }

    $repoIdentity = (& $gh.Source repo view $Repo --json nameWithOwner --jq '.nameWithOwner' 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or [string]$repoIdentity -ne $Repo) {
        throw "GitHub CLI cannot prove repository identity '$Repo'."
    }

    & $gh.Source release view $Bundle.tag --repo $Repo --json tagName *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "A GitHub Release already exists for tag '$($Bundle.tag)'; refusing to overwrite it."
    }

    & $gh.Source api "repos/$Repo/git/ref/tags/$($Bundle.tag)" *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Git tag '$($Bundle.tag)' already exists without the expected new release; refusing ambiguous promotion."
    }

    $assets = @(
        $Bundle.packagePath,
        $Bundle.packageChecksumPath,
        $Bundle.manifestPath,
        $Bundle.manifestChecksumPath,
        $Bundle.capabilityMatrixPath
    )
    $releaseArguments = Get-ReleaseCreateArguments -Repo $Repo -Tag $Bundle.tag -SourceCommit $Bundle.sourceCommit -Version $Version -NotesPath $Bundle.notesPath -Assets $assets
    $created = $false
    try {
        & $gh.Source @releaseArguments
        if ($LASTEXITCODE -ne 0) {
            throw "GitHub pre-release creation failed with exit code $LASTEXITCODE."
        }
        $created = $true

        $resolvedCommit = (& $gh.Source api "repos/$Repo/commits/$($Bundle.tag)" --jq '.sha' 2>$null | Select-Object -First 1)
        if ($LASTEXITCODE -ne 0 -or [string]$resolvedCommit -ne [string]$Bundle.sourceCommit) {
            throw "Published tag does not resolve to the exact verified source commit '$($Bundle.sourceCommit)'."
        }

        $releaseJson = & $gh.Source release view $Bundle.tag --repo $Repo --json url,tagName,isPrerelease,assets
        if ($LASTEXITCODE -ne 0) {
            throw "Cannot read back the GitHub pre-release after creation."
        }
        $release = $releaseJson | ConvertFrom-Json
        if ([string]$release.tagName -ne [string]$Bundle.tag -or -not [bool]$release.isPrerelease) {
            throw "Published GitHub Release does not match the requested prerelease identity."
        }

        $expectedAssets = @(
            [System.IO.Path]::GetFileName($Bundle.packagePath),
            [System.IO.Path]::GetFileName($Bundle.packageChecksumPath),
            [System.IO.Path]::GetFileName($Bundle.manifestPath),
            [System.IO.Path]::GetFileName($Bundle.manifestChecksumPath),
            [System.IO.Path]::GetFileName($Bundle.capabilityMatrixPath)
        )
        $actualAssets = @($release.assets | ForEach-Object { [string]$_.name })
        foreach ($asset in $expectedAssets) {
            if ($actualAssets -notcontains $asset) {
                throw "Published pre-release is missing expected asset '$asset'."
            }
        }
    }
    catch {
        $publicationError = $_
        if ($created) {
            Write-Warning "Post-publication verification failed; attempting to remove the newly created pre-release and tag."
            & $gh.Source release delete $Bundle.tag --repo $Repo --yes --cleanup-tag *> $null
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Automatic cleanup failed. Inspect tag/release '$($Bundle.tag)' manually before retrying."
            }
        }
        throw $publicationError
    }

    return [pscustomobject]@{
        url = [string]$release.url
        tag = [string]$release.tagName
        sourceCommit = [string]$Bundle.sourceCommit
        packageSha256 = [string]$Bundle.packageSha256
    }
}

function Write-TestFileWithSidecar {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    Write-Utf8NoBom -Path $Path -Text $Text
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Utf8NoBom -Path "$Path.sha256" -Text ("{0}  {1}{2}" -f $hash, [System.IO.Path]::GetFileName($Path), [Environment]::NewLine)
    return $hash
}

function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-release-publish-selftest-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    try {
        $version = '0.5.0-beta.1'
        $commit = 'a' * 40
        $packagePath = Join-Path $tempRoot 'DragonDiskForge-win-x64.zip'
        $packageHash = Write-TestFileWithSidecar -Path $packagePath -Text 'self-test-package'

        $candidatePath = Join-Path $tempRoot 'beta-candidate.json'
        $candidate = [ordered]@{
            schemaVersion = 1
            kind = 'DragonDiskForgeBetaCandidate'
            product = 'Dragon DiskForge'
            version = $version
            architecture = 'x64'
            sourceCommit = $commit
            workflowRunId = '123456789'
            packageSha256 = $packageHash
            entryPointSha256 = ('b' * 64)
            betaManualQaEntryPointSha256 = ('c' * 64)
            createdUtc = '2026-09-18T00:00:00.0000000+00:00'
            publicRelease = $false
        }
        Write-TestFileWithSidecar -Path $candidatePath -Text (($candidate | ConvertTo-Json -Depth 5) + [Environment]::NewLine) | Out-Null

        $qaKitPath = Join-Path $tempRoot 'beta-qa-kit.json'
        Write-TestFileWithSidecar -Path $qaKitPath -Text "{`"schemaVersion`":2}`n" | Out-Null
        $evidencePath = Join-Path $tempRoot 'beta-manual-qa.json'
        Write-TestFileWithSidecar -Path $evidencePath -Text "{`"schemaVersion`":3}`n" | Out-Null
        $capabilitiesPath = Join-Path $tempRoot 'SUPPORTED-CAPABILITIES.md'
        Write-Utf8NoBom -Path $capabilitiesPath -Text "# Supported capabilities`n"

        $templatePath = Join-Path $tempRoot 'notes.tmpl'
        Write-Utf8NoBom -Path $templatePath -Text @'
# Dragon DiskForge {{VERSION}}

Tag: `{{TAG}}`
Source: `{{SOURCE_COMMIT}}`
Run: `{{CANDIDATE_RUN_ID}}`
Package SHA-256: `{{PACKAGE_SHA256}}`
Manual QA SHA-256: `{{MANUAL_QA_SHA256}}`
Capability matrix SHA-256: `{{CAPABILITY_MATRIX_SHA256}}`
Repository: `{{REPOSITORY}}`
'@

        $proofScript = Join-Path $tempRoot 'fake-proof.ps1'
        Write-Utf8NoBom -Path $proofScript -Text @'
param(
    [string]$Mode,
    [string]$CandidateMetadataPath,
    [string]$CandidateMetadataChecksumFile,
    [string]$QaKitManifestPath,
    [string]$QaKitManifestChecksumFile,
    [string]$PackagePath,
    [string]$PackageChecksumFile,
    [string]$EvidencePath,
    [string]$ExpectedVersion,
    [string]$ExpectedSourceCommit
)
if ($env:DRAGON_DISKFORGE_RELEASE_PROOF_FAIL -eq '1') { exit 19 }
if ($Mode -ne 'verify') { exit 17 }
exit 0
'@

        $outputRoot = Join-Path $tempRoot 'release'
        $bundle = Prepare-ReleaseBundle -CandidatePath $candidatePath -CandidateChecksumPath "$candidatePath.sha256" -QaKitPath $qaKitPath -QaKitChecksumPath "$qaKitPath.sha256" -PackageFile $packagePath -PackageChecksumPath "$packagePath.sha256" -EvidenceFile $evidencePath -EvidenceChecksumPath "$evidencePath.sha256" -Version $version -SourceCommit $commit -TemplatePath $templatePath -CapabilitiesPath $capabilitiesPath -OutputRoot $outputRoot -Tag $version -Repo 'Swir/Dragon-DiskForge' -ReleaseProofScript $proofScript

        if (-not (Test-Path -LiteralPath $bundle.packagePath -PathType Leaf)) { throw 'Self-test failed: release package was not prepared.' }
        if ((Get-FileHash -LiteralPath $bundle.packagePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $packageHash) { throw 'Self-test failed: prepared package hash changed.' }
        $manifest = Get-Content -LiteralPath $bundle.manifestPath -Raw | ConvertFrom-Json
        if ([string]$manifest.sourceCommit -ne $commit -or [string]$manifest.packageSha256 -ne $packageHash -or [string]$manifest.proofState -ne 'verified') {
            throw 'Self-test failed: release manifest lost verified source/package identity.'
        }
        $notes = Get-Content -LiteralPath $bundle.notesPath -Raw
        if ($notes -match '\{\{[A-Z0-9_]+\}\}' -or $notes -notmatch [regex]::Escape($packageHash)) {
            throw 'Self-test failed: release notes were not rendered deterministically from verified values.'
        }

        $arguments = Get-ReleaseCreateArguments -Repo 'Swir/Dragon-DiskForge' -Tag $version -SourceCommit $commit -Version $version -NotesPath $bundle.notesPath -Assets @($bundle.packagePath, $bundle.packageChecksumPath, $bundle.manifestPath, $bundle.manifestChecksumPath, $bundle.capabilityMatrixPath)
        if ($arguments -notcontains '--prerelease' -or $arguments -notcontains $commit -or $arguments -notcontains $bundle.packagePath) {
            throw 'Self-test failed: GitHub pre-release arguments lost prerelease/source/asset binding.'
        }

        $env:DRAGON_DISKFORGE_RELEASE_PROOF_FAIL = '1'
        $proofRejected = $false
        try {
            Prepare-ReleaseBundle -CandidatePath $candidatePath -CandidateChecksumPath "$candidatePath.sha256" -QaKitPath $qaKitPath -QaKitChecksumPath "$qaKitPath.sha256" -PackageFile $packagePath -PackageChecksumPath "$packagePath.sha256" -EvidenceFile $evidencePath -EvidenceChecksumPath "$evidencePath.sha256" -Version $version -SourceCommit $commit -TemplatePath $templatePath -CapabilitiesPath $capabilitiesPath -OutputRoot (Join-Path $tempRoot 'proof-fail') -Tag $version -Repo 'Swir/Dragon-DiskForge' -ReleaseProofScript $proofScript | Out-Null
        }
        catch {
            $proofRejected = $true
        }
        finally {
            Remove-Item Env:DRAGON_DISKFORGE_RELEASE_PROOF_FAIL -ErrorAction SilentlyContinue
        }
        if (-not $proofRejected) { throw 'Self-test failed: failed release proof did not block preparation.' }

        Add-Content -LiteralPath $packagePath -Value 'tamper'
        $tamperRejected = $false
        try {
            Prepare-ReleaseBundle -CandidatePath $candidatePath -CandidateChecksumPath "$candidatePath.sha256" -QaKitPath $qaKitPath -QaKitChecksumPath "$qaKitPath.sha256" -PackageFile $packagePath -PackageChecksumPath "$packagePath.sha256" -EvidenceFile $evidencePath -EvidenceChecksumPath "$evidencePath.sha256" -Version $version -SourceCommit $commit -TemplatePath $templatePath -CapabilitiesPath $capabilitiesPath -OutputRoot (Join-Path $tempRoot 'tamper-fail') -Tag $version -Repo 'Swir/Dragon-DiskForge' -ReleaseProofScript $proofScript | Out-Null
        }
        catch {
            $tamperRejected = $true
        }
        if (-not $tamperRejected) { throw 'Self-test failed: tampered package was accepted.' }

        $candidate.sourceCommit = 'd' * 40
        Write-TestFileWithSidecar -Path $candidatePath -Text (($candidate | ConvertTo-Json -Depth 5) + [Environment]::NewLine) | Out-Null
        $sourceRejected = $false
        try {
            Prepare-ReleaseBundle -CandidatePath $candidatePath -CandidateChecksumPath "$candidatePath.sha256" -QaKitPath $qaKitPath -QaKitChecksumPath "$qaKitPath.sha256" -PackageFile $bundle.packagePath -PackageChecksumPath $bundle.packageChecksumPath -EvidenceFile $evidencePath -EvidenceChecksumPath "$evidencePath.sha256" -Version $version -SourceCommit $commit -TemplatePath $templatePath -CapabilitiesPath $capabilitiesPath -OutputRoot (Join-Path $tempRoot 'source-fail') -Tag $version -Repo 'Swir/Dragon-DiskForge' -ReleaseProofScript $proofScript | Out-Null
        }
        catch {
            $sourceRejected = $true
        }
        if (-not $sourceRejected) { throw 'Self-test failed: mismatched candidate source commit was accepted.' }

        $tagRejected = $false
        try { Assert-VersionAndTag -Version $version -Tag 'v0.5.0-beta.1' | Out-Null } catch { $tagRejected = $true }
        if (-not $tagRejected) { throw 'Self-test failed: non-canonical tag was accepted.' }

        Write-Host 'Dragon DiskForge beta release publish contract self-test passed.'
    }
    finally {
        Remove-Item Env:DRAGON_DISKFORGE_RELEASE_PROOF_FAIL -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

switch ($Mode) {
    'self-test' {
        Invoke-SelfTest
        exit 0
    }

    'prepare' {
        $bundle = Prepare-ReleaseBundle -CandidatePath $CandidateMetadataPath -CandidateChecksumPath $CandidateMetadataChecksumFile -QaKitPath $QaKitManifestPath -QaKitChecksumPath $QaKitManifestChecksumFile -PackageFile $PackagePath -PackageChecksumPath $PackageChecksumFile -EvidenceFile $EvidencePath -EvidenceChecksumPath $EvidenceChecksumFile -Version $ExpectedVersion -SourceCommit $ExpectedSourceCommit -TemplatePath $ReleaseNotesTemplatePath -CapabilitiesPath $CapabilityMatrixPath -OutputRoot $OutputDirectory -Tag $TagName -Repo $Repository -ReleaseProofScript $ProofScriptPath
        Write-Host "Dragon DiskForge beta release bundle is prepared after exact release-proof verification."
        Write-Host "Directory: $($bundle.directory)"
        Write-Host "Source commit: $($bundle.sourceCommit)"
        Write-Host "Package SHA-256: $($bundle.packageSha256)"
        Write-Host "Manual QA evidence SHA-256: $($bundle.manualQaEvidenceSha256)"
        Write-Host "GitHub pre-release has NOT been published."
        exit 0
    }

    'publish' {
        $bundle = Prepare-ReleaseBundle -CandidatePath $CandidateMetadataPath -CandidateChecksumPath $CandidateMetadataChecksumFile -QaKitPath $QaKitManifestPath -QaKitChecksumPath $QaKitManifestChecksumFile -PackageFile $PackagePath -PackageChecksumPath $PackageChecksumFile -EvidenceFile $EvidencePath -EvidenceChecksumPath $EvidenceChecksumFile -Version $ExpectedVersion -SourceCommit $ExpectedSourceCommit -TemplatePath $ReleaseNotesTemplatePath -CapabilitiesPath $CapabilityMatrixPath -OutputRoot $OutputDirectory -Tag $TagName -Repo $Repository -ReleaseProofScript $ProofScriptPath
        $published = Publish-ReleaseBundle -Bundle $bundle -Repo $Repository -Version $ExpectedVersion
        Write-Host "Dragon DiskForge public beta prerelease was published only after exact proof verification."
        Write-Host "URL: $($published.url)"
        Write-Host "Tag: $($published.tag)"
        Write-Host "Source commit: $($published.sourceCommit)"
        Write-Host "Package SHA-256: $($published.packageSha256)"
        exit 0
    }
}
