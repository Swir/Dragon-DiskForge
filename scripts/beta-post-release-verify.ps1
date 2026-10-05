[CmdletBinding()]
param(
    [ValidateSet('verify', 'self-test')]
    [string]$Mode = 'verify',

    [string]$Repository = 'Swir/Dragon-DiskForge',
    [string]$TagName = '0.5.0-beta.1',
    [string]$ExpectedVersion = '0.5.0-beta.1',
    [string]$ExpectedSourceCommit = '',
    [string]$ExpectedVerifierCommit = '',
    [string]$VerifyPackageScriptPath = 'scripts/verify-package.ps1',
    [string]$VerifyInstallerScriptPath = 'scripts/verify-installer.ps1',
    [string]$BaselinePath = 'docs/post-release-baseline-0.5.0-beta.1.json',
    [string]$RetainedEvidencePath = 'docs/retained-beta-candidate.json',
    [string]$RetainedPublicBindingScriptPath = 'scripts/beta-retained-public-binding.ps1',
    [string]$RetainedReleaseIdentityScriptPath = 'scripts/beta-retained-release-identity.ps1',
    [string]$PublicInstallerSmokeScriptPath = 'scripts/beta-public-installer-smoke-contract.ps1',
    [switch]$KeepDownloads
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-RepositoryName {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw "Repository '$Value' must be in owner/name form."
    }
    return $Value
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

function Assert-HexSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Label must be a 64-character SHA-256 value."
    }
    return $Value.ToLowerInvariant()
}

function Get-ExpectedAssetNames {
    param([Parameter(Mandatory = $true)][string]$Version)

    return @(
        "DragonDiskForge-$Version-win-x64.zip",
        "DragonDiskForge-$Version-win-x64.zip.sha256",
        "DragonDiskForge-$Version-win-x64-setup.exe",
        "DragonDiskForge-$Version-win-x64-setup.exe.sha256",
        "release-manifest-$Version.json",
        "release-manifest-$Version.json.sha256",
        "SUPPORTED-CAPABILITIES-$Version.md"
    )
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file is missing: $Path"
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}


function Assert-GitSha1 {
    param([Parameter(Mandatory = $true)][string]$Value,[Parameter(Mandatory = $true)][string]$Label)
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { throw "$Label must be a 40-character Git SHA-1 value." }
    return $Value.ToLowerInvariant()
}

function Get-GitExecutable {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git -or [string]::IsNullOrWhiteSpace([string]$git.Source)) { throw 'Git is required for exact tooling blob verification.' }
    return $git.Source
}

function Invoke-GitSingleLine {
    param([Parameter(Mandatory = $true)][string]$GitExecutable,[Parameter(Mandatory = $true)][string]$RepositoryRoot,[Parameter(Mandatory = $true)][string[]]$Arguments,[Parameter(Mandatory = $true)][string]$Label)
    $output = @(& $GitExecutable -C $RepositoryRoot @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with Git exit code $LASTEXITCODE." }
    $lines = @($output | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -ne 1) { throw "$Label must return exactly one non-empty line." }
    return $lines[0].Trim()
}

function Resolve-GitRepositoryRoot {
    param([Parameter(Mandatory = $true)][string]$Path)
    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $start = if (Test-Path -LiteralPath $resolved -PathType Leaf) { Split-Path -Parent $resolved } else { $resolved }
    $git = Get-GitExecutable
    $root = Invoke-GitSingleLine -GitExecutable $git -RepositoryRoot $start -Arguments @('rev-parse','--show-toplevel') -Label 'Git repository-root resolution'
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Git repository root does not exist: $root" }
    return [pscustomobject]@{ git = $git; root = (Resolve-Path -LiteralPath $root).Path }
}

function Get-GitCommitBlobSha {
    param([Parameter(Mandatory = $true)][string]$GitExecutable,[Parameter(Mandatory = $true)][string]$RepositoryRoot,[Parameter(Mandatory = $true)][string]$Commit,[Parameter(Mandatory = $true)][string]$RepositoryPath)
    $spec = ('{0}:{1}' -f $Commit,$RepositoryPath)
    return Assert-GitSha1 -Value (Invoke-GitSingleLine -GitExecutable $GitExecutable -RepositoryRoot $RepositoryRoot -Arguments @('rev-parse',$spec) -Label "Exact-commit blob lookup for $RepositoryPath") -Label "Exact-commit blob for $RepositoryPath"
}

function Get-GitWorkingTreeBlobSha {
    param([Parameter(Mandatory = $true)][string]$GitExecutable,[Parameter(Mandatory = $true)][string]$RepositoryRoot,[Parameter(Mandatory = $true)][string]$RepositoryPath,[Parameter(Mandatory = $true)][string]$LocalPath)
    return Assert-GitSha1 -Value (Invoke-GitSingleLine -GitExecutable $GitExecutable -RepositoryRoot $RepositoryRoot -Arguments @('hash-object',"--path=$RepositoryPath",'--',$LocalPath) -Label "Git-filtered working-tree blob for $RepositoryPath") -Label "Git-filtered working-tree blob for $RepositoryPath"
}

function Get-GitRawFileBlobSha {
    param([Parameter(Mandatory = $true)][string]$GitExecutable,[Parameter(Mandatory = $true)][string]$RepositoryRoot,[Parameter(Mandatory = $true)][string]$Path,[Parameter(Mandatory = $true)][string]$Label)
    return Assert-GitSha1 -Value (Invoke-GitSingleLine -GitExecutable $GitExecutable -RepositoryRoot $RepositoryRoot -Arguments @('hash-object','--no-filters','--',$Path) -Label $Label) -Label $Label
}

function Read-Sha256Sidecar {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ExpectedFileName,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label is missing: $Path"
    }
    $line = (Get-Content -LiteralPath $Path -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
        throw "$Label has an invalid SHA-256 sidecar format."
    }

    $hash = $Matches[1].ToLowerInvariant()
    $fileName = $Matches[2].Trim()
    if ($fileName -ne $ExpectedFileName) {
        throw "$Label targets '$fileName' instead of '$ExpectedFileName'."
    }
    return $hash
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function Assert-PublicReleaseMetadata {
    param(
        [Parameter(Mandatory = $true)]$Release,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ([string]$Release.tag_name -ne $Tag) {
        throw "Public release tag '$($Release.tag_name)' does not match '$Tag'."
    }
    if ([bool]$Release.draft) {
        throw 'Public beta Release is still a draft.'
    }
    if (-not [bool]$Release.prerelease) {
        throw 'Public beta Release is not marked as a pre-release.'
    }
    if ([string]::IsNullOrWhiteSpace([string]$Release.published_at)) {
        throw 'Public beta Release has no published_at timestamp.'
    }

    $assets = @($Release.assets)
    $names = @($assets | ForEach-Object { [string]$_.name })
    $duplicates = @($names | Group-Object | Where-Object { $_.Count -gt 1 })
    if ($duplicates.Count -ne 0) {
        throw "Public beta Release contains duplicate asset names: $((@($duplicates | ForEach-Object { $_.Name })) -join ', ')."
    }

    foreach ($name in (Get-ExpectedAssetNames -Version $Version)) {
        $matches = @($assets | Where-Object { [string]$_.name -eq $name })
        if ($matches.Count -ne 1) {
            throw "Public beta Release must contain exactly one '$name' asset."
        }
        $asset = $matches[0]
        if ($null -ne $asset.PSObject.Properties['state'] -and [string]$asset.state -ne 'uploaded') {
            throw "Public beta asset '$name' is not in uploaded state."
        }
        if ($null -ne $asset.PSObject.Properties['size'] -and [int64]$asset.size -le 0) {
            throw "Public beta asset '$name' is empty."
        }
        if ([string]::IsNullOrWhiteSpace([string]$asset.browser_download_url)) {
            throw "Public beta asset '$name' has no browser download URL."
        }
    }
}

function Assert-PublicAssetUrl {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$RepositoryName,
        [Parameter(Mandatory = $true)][string]$Tag
    )

    $uri = [Uri]$Url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com') {
        throw "Release asset URL is not an HTTPS github.com URL: $Url"
    }
    $prefix = '/' + $RepositoryName + '/releases/download/' + [Uri]::EscapeDataString($Tag) + '/'
    if (-not $uri.AbsolutePath.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
        throw "Release asset URL is outside the expected repository/tag release path: $Url"
    }
}

function Get-PublicRawToolUrl {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryName,
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$RepositoryPath
    )

    $repo = Assert-RepositoryName -Value $RepositoryName
    $sha = Assert-ExactCommit -Commit $Commit -Label 'Verifier tooling commit'
    if ($RepositoryPath -notmatch '^scripts/[A-Za-z0-9_.-]+\.ps1$') {
        throw "Repository tooling path '$RepositoryPath' is not an allowed fixed scripts/*.ps1 path."
    }
    return "https://raw.githubusercontent.com/$repo/$sha/$RepositoryPath"
}

function Assert-ToolingProvenance {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryName,
        [Parameter(Mandatory = $true)][string]$ToolingCommit,
        [Parameter(Mandatory = $true)][string]$BaselineFile,
        [Parameter(Mandatory = $true)][string]$LocalVerifierPath,
        [Parameter(Mandatory = $true)][string]$LocalPackageVerifierPath,
        [Parameter(Mandatory = $true)][string]$LocalInstallerVerifierPath,
        [Parameter(Mandatory = $true)][string]$LocalRetainedIdentityPath,
        [Parameter(Mandatory = $true)][string]$LocalRetainedBindingPath,
        [Parameter(Mandatory = $true)][string]$LocalInstallerSmokePath
    )

    $repo = Assert-RepositoryName -Value $RepositoryName
    $commit = Assert-ExactCommit -Commit $ToolingCommit -Label 'ExpectedVerifierCommit'
    $gitContext = Resolve-GitRepositoryRoot -Path $LocalVerifierPath
    $git = [string]$gitContext.git
    $repositoryRoot = [string]$gitContext.root
    $head = Assert-ExactCommit -Commit (Invoke-GitSingleLine -GitExecutable $git -RepositoryRoot $repositoryRoot -Arguments @('rev-parse','HEAD') -Label 'Checked-out HEAD read-back') -Label 'Checked-out HEAD'
    if ($head -ne $commit) { throw "Checked-out HEAD '$head' does not match exact verifier tooling commit '$commit'." }

    $baseline = Get-Content -LiteralPath (Resolve-Path -LiteralPath $BaselineFile -ErrorAction Stop).Path -Raw | ConvertFrom-Json
    if ([int]$baseline.schemaVersion -ne 3 -or [string]$baseline.kind -ne 'DragonDiskForgePostReleaseAcceptanceBaseline' -or [string]$baseline.version -ne '0.5.0-beta.1') { throw 'Post-release baseline identity is invalid.' }
    if ([string]$baseline.toolingPinPolicy.mode -ne 'exact-commit-plus-blob' -or -not [bool]$baseline.toolingPinPolicy.failClosed -or -not [bool]$baseline.toolingPinPolicy.requirePublicReadBack) { throw 'Post-release baseline tooling pin policy is invalid.' }

    $expected = @(
        [pscustomobject]@{ repositoryPath='scripts/beta-post-release-verify.ps1';localPath=(Resolve-Path -LiteralPath $LocalVerifierPath -ErrorAction Stop).Path;label='post-release verifier';resultName='postReleaseVerifierSha256';helper=$false },
        [pscustomobject]@{ repositoryPath='scripts/verify-package.ps1';localPath=(Resolve-Path -LiteralPath $LocalPackageVerifierPath -ErrorAction Stop).Path;label='package verifier';resultName='packageVerifierSha256';helper=$false },
        [pscustomobject]@{ repositoryPath='scripts/verify-installer.ps1';localPath=(Resolve-Path -LiteralPath $LocalInstallerVerifierPath -ErrorAction Stop).Path;label='installer verifier';resultName='installerVerifierSha256';helper=$false },
        [pscustomobject]@{ repositoryPath='scripts/beta-retained-release-identity.ps1';localPath=(Resolve-Path -LiteralPath $LocalRetainedIdentityPath -ErrorAction Stop).Path;label='retained identity helper';resultName='retainedReleaseIdentitySha256';helper=$true },
        [pscustomobject]@{ repositoryPath='scripts/beta-retained-public-binding.ps1';localPath=(Resolve-Path -LiteralPath $LocalRetainedBindingPath -ErrorAction Stop).Path;label='retained binding helper';resultName='retainedPublicBindingSha256';helper=$true },
        [pscustomobject]@{ repositoryPath='scripts/beta-public-installer-smoke-contract.ps1';localPath=(Resolve-Path -LiteralPath $LocalInstallerSmokePath -ErrorAction Stop).Path;label='public installer smoke helper';resultName='publicInstallerSmokeSha256';helper=$true }
    )

    $headers=@{'Accept'='application/vnd.github+json';'User-Agent'='DragonDiskForge-PostReleaseVerifier';'X-GitHub-Api-Version'='2022-11-28'}
    $commitResponse=Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$repo/commits/$commit" -Headers $headers
    if ((Assert-ExactCommit -Commit ([string]$commitResponse.sha) -Label 'Verifier tooling commit read-back') -ne $commit) { throw 'Verifier tooling commit read-back mismatch.' }

    $root=Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-verifier-provenance-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $hashes=@{};$helperCount=0
    try {
        foreach($tool in $expected){
            $property=$baseline.requiredToolingBlobs.PSObject.Properties[$tool.repositoryPath]
            if($null -eq $property){throw "Post-release baseline is missing required tooling blob pin '$($tool.repositoryPath)'."}
            $pin=Assert-GitSha1 -Value ([string]$property.Value) -Label "Baseline blob pin for $($tool.repositoryPath)"
            $expectedLocal=[System.IO.Path]::GetFullPath((Join-Path $repositoryRoot ($tool.repositoryPath.Replace('/',[System.IO.Path]::DirectorySeparatorChar))))
            $actualLocal=[System.IO.Path]::GetFullPath([string]$tool.localPath)
            $comparison=if($env:OS -eq 'Windows_NT'){[System.StringComparison]::OrdinalIgnoreCase}else{[System.StringComparison]::Ordinal}
            if(-not [string]::Equals($expectedLocal,$actualLocal,$comparison)){throw "Local tooling path '$actualLocal' does not match repository path '$($tool.repositoryPath)'."}
            $commitBlob=Get-GitCommitBlobSha -GitExecutable $git -RepositoryRoot $repositoryRoot -Commit $commit -RepositoryPath $tool.repositoryPath
            $workingBlob=Get-GitWorkingTreeBlobSha -GitExecutable $git -RepositoryRoot $repositoryRoot -RepositoryPath $tool.repositoryPath -LocalPath $actualLocal
            if($commitBlob -ne $pin -or $workingBlob -ne $pin){throw "Exact/local Git blob mismatch for $($tool.repositoryPath): baseline=$pin exact=$commitBlob working=$workingBlob."}
            $destination=Join-Path $root ([System.IO.Path]::GetFileName($tool.repositoryPath))
            Invoke-WebRequest -UseBasicParsing -Uri (Get-PublicRawToolUrl -RepositoryName $repo -Commit $commit -RepositoryPath $tool.repositoryPath) -Headers @{'User-Agent'='DragonDiskForge-PostReleaseVerifier'} -OutFile $destination
            if(-not(Test-Path -LiteralPath $destination -PathType Leaf)-or(Get-Item -LiteralPath $destination).Length -le 0){throw "Public read-back of $($tool.label) did not produce a non-empty file."}
            $publicBlob=Get-GitRawFileBlobSha -GitExecutable $git -RepositoryRoot $repositoryRoot -Path $destination -Label "Public exact-commit Git blob for $($tool.repositoryPath)"
            if($publicBlob -ne $pin){throw "Public exact-commit Git blob '$publicBlob' for $($tool.repositoryPath) does not match baseline pin '$pin'."}
            $hashes[[string]$tool.resultName]=Get-Sha256 -Path $actualLocal
            if([bool]$tool.helper){$helperCount++}
        }
        if($helperCount -ne 3){throw "Exact public helper read-back expected 3 helpers, observed $helperCount."}
        return [pscustomobject]@{verifierCommit=$commit;postReleaseVerifierSha256=[string]$hashes.postReleaseVerifierSha256;packageVerifierSha256=[string]$hashes.packageVerifierSha256;installerVerifierSha256=[string]$hashes.installerVerifierSha256;retainedReleaseIdentitySha256=[string]$hashes.retainedReleaseIdentitySha256;retainedPublicBindingSha256=[string]$hashes.retainedPublicBindingSha256;publicInstallerSmokeSha256=[string]$hashes.publicInstallerSmokeSha256;exactPublicToolingReadBack=$true;exactPublicHelperReadBack=$true}
    }
    finally {if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}}
}

function Assert-DownloadedReleaseBundle {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$SourceCommit
    )

    $packageName = "DragonDiskForge-$Version-win-x64.zip"
    $packagePath = Join-Path $Directory $packageName
    $packageSidecarPath = "$packagePath.sha256"
    $installerName = "DragonDiskForge-$Version-win-x64-setup.exe"
    $installerPath = Join-Path $Directory $installerName
    $installerSidecarPath = "$installerPath.sha256"
    $manifestName = "release-manifest-$Version.json"
    $manifestPath = Join-Path $Directory $manifestName
    $manifestSidecarPath = "$manifestPath.sha256"
    $capabilityName = "SUPPORTED-CAPABILITIES-$Version.md"
    $capabilityPath = Join-Path $Directory $capabilityName

    foreach ($required in @($packagePath, $packageSidecarPath, $installerPath, $installerSidecarPath, $manifestPath, $manifestSidecarPath, $capabilityPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Downloaded public release is missing '$required'."
        }
    }

    $declaredPackageHash = Read-Sha256Sidecar -Path $packageSidecarPath -ExpectedFileName $packageName -Label 'Public package SHA-256 sidecar'
    $actualPackageHash = Get-Sha256 -Path $packagePath
    if ($declaredPackageHash -ne $actualPackageHash) {
        throw "Downloaded public package SHA-256 mismatch. Declared '$declaredPackageHash', actual '$actualPackageHash'."
    }

    $declaredInstallerHash = Read-Sha256Sidecar -Path $installerSidecarPath -ExpectedFileName $installerName -Label 'Public installer SHA-256 sidecar'
    $actualInstallerHash = Get-Sha256 -Path $installerPath
    if ($declaredInstallerHash -ne $actualInstallerHash) {
        throw "Downloaded public installer SHA-256 mismatch. Declared '$declaredInstallerHash', actual '$actualInstallerHash'."
    }

    $declaredManifestHash = Read-Sha256Sidecar -Path $manifestSidecarPath -ExpectedFileName $manifestName -Label 'Public release-manifest SHA-256 sidecar'
    $actualManifestHash = Get-Sha256 -Path $manifestPath
    if ($declaredManifestHash -ne $actualManifestHash) {
        throw "Downloaded public release manifest SHA-256 mismatch. Declared '$declaredManifestHash', actual '$actualManifestHash'."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 2) { throw "Unsupported public release manifest schema '$($manifest.schemaVersion)'." }
    if ([string]$manifest.kind -ne 'DragonDiskForgeBetaReleaseBundle') { throw "Unexpected public release manifest kind '$($manifest.kind)'." }
    if ([string]$manifest.product -ne 'Dragon DiskForge') { throw "Unexpected public release product '$($manifest.product)'." }
    if ([string]$manifest.version -ne $Version) { throw "Public release manifest version '$($manifest.version)' does not match '$Version'." }
    if ([string]$manifest.tag -ne $Tag) { throw "Public release manifest tag '$($manifest.tag)' does not match '$Tag'." }
    if ([string]$manifest.releaseKind -ne 'github-prerelease') { throw "Unexpected release kind '$($manifest.releaseKind)'." }
    if ([string]$manifest.architecture -ne 'x64') { throw "Unexpected public release architecture '$($manifest.architecture)'." }
    if ([string]$manifest.proofState -ne 'verified') { throw "Public release manifest proofState is not 'verified'." }
    if ([string]$manifest.publicationIntent -ne 'public-github-prerelease') { throw "Public release manifest publicationIntent is not canonical." }

    $manifestCommit = Assert-ExactCommit -Commit ([string]$manifest.sourceCommit) -Label 'Public release manifest sourceCommit'
    if ($manifestCommit -ne $SourceCommit) { throw "Public release manifest source commit '$manifestCommit' does not match '$SourceCommit'." }
    if ([string]$manifest.packageFile -ne $packageName) { throw "Public release manifest packageFile '$($manifest.packageFile)' does not match '$packageName'." }
    $manifestPackageHash = Assert-HexSha256 -Value ([string]$manifest.packageSha256) -Label 'Public release manifest packageSha256'
    if ($manifestPackageHash -ne $actualPackageHash) { throw 'Public release manifest packageSha256 does not match the downloaded package.' }
    if ([string]$manifest.installerFile -ne $installerName) { throw "Public release manifest installerFile '$($manifest.installerFile)' does not match '$installerName'." }
    $manifestInstallerHash = Assert-HexSha256 -Value ([string]$manifest.installerSha256) -Label 'Public release manifest installerSha256'
    if ($manifestInstallerHash -ne $actualInstallerHash) { throw 'Public release manifest installerSha256 does not match the downloaded installer.' }
    if ([string]$manifest.installerScope -ne 'per-user') { throw 'Public release manifest installerScope is not per-user.' }

    foreach ($proofField in @('candidateMetadataSha256', 'qaKitManifestSha256', 'retainedEvidenceSha256', 'manualQaEvidenceSha256', 'releaseNotesSha256')) {
        $null = Assert-HexSha256 -Value ([string]$manifest.$proofField) -Label "Public release manifest $proofField"
    }

    if ([string]$manifest.capabilityMatrixReleaseFile -ne $capabilityName) {
        throw "Public release manifest capabilityMatrixReleaseFile '$($manifest.capabilityMatrixReleaseFile)' does not match '$capabilityName'."
    }
    $actualCapabilityHash = Get-Sha256 -Path $capabilityPath
    $manifestCapabilityHash = Assert-HexSha256 -Value ([string]$manifest.capabilityMatrixSha256) -Label 'Public release manifest capabilityMatrixSha256'
    if ($manifestCapabilityHash -ne $actualCapabilityHash) {
        throw 'Public release capability-matrix SHA-256 does not match the release manifest.'
    }

    return [pscustomobject]@{
        packagePath = $packagePath
        packageSha256 = $actualPackageHash
        installerPath = $installerPath
        installerSha256 = $actualInstallerHash
        manifestPath = $manifestPath
        manifestSha256 = $actualManifestHash
        capabilityMatrixPath = $capabilityPath
        capabilityMatrixSha256 = $actualCapabilityHash
        candidateWorkflowRunId = [string]$manifest.candidateWorkflowRunId
        retainedEvidenceSha256 = ([string]$manifest.retainedEvidenceSha256).ToLowerInvariant()
        manualQaEvidenceSha256 = ([string]$manifest.manualQaEvidenceSha256).ToLowerInvariant()
    }
}

function Invoke-RuntimePackageVerification {
    param(
        [Parameter(Mandatory = $true)][string]$DownloadedPackagePath,
        [Parameter(Mandatory = $true)][string]$PackageSha256,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$VerifierPath,
        [Parameter(Mandatory = $true)][string]$TemporaryRoot
    )

    if ($env:OS -ne 'Windows_NT') {
        throw 'Post-release runtime package verification must run on Windows.'
    }

    $resolvedVerifier = (Resolve-Path -LiteralPath $VerifierPath -ErrorAction Stop).Path
    $powershell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($null -eq $powershell) {
        throw 'Windows PowerShell is required to execute the existing package verifier.'
    }

    $stageParent = Join-Path $TemporaryRoot 'runtime-verification'
    $stage = Join-Path $stageParent 'package'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $canonicalPackagePath = Join-Path $stage 'DragonDiskForge-win-x64.zip'
    Copy-Item -LiteralPath $DownloadedPackagePath -Destination $canonicalPackagePath -Force
    Write-Utf8NoBom -Path "$canonicalPackagePath.sha256" -Text ("{0}  DragonDiskForge-win-x64.zip{1}" -f $PackageSha256, [Environment]::NewLine)

    Push-Location $stageParent
    try {
        & $powershell.Source -NoLogo -NoProfile -ExecutionPolicy Bypass -File $resolvedVerifier -OutputDirectory 'package' -ExpectedVersion $Version
        if ($LASTEXITCODE -ne 0) {
            throw "Downloaded public package failed the existing package verifier with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}


function Assert-RetainedIdentityBinding {
    param([Parameter(Mandatory = $true)][string]$EvidencePath,[Parameter(Mandatory = $true)][string]$BaselineFile,[Parameter(Mandatory = $true)][string]$ExpectedVersion,[Parameter(Mandatory = $true)][string]$ExpectedSourceCommit,[Parameter(Mandatory = $true)][string]$PublicPackageSha256,[Parameter(Mandatory = $true)][string]$PublicInstallerSha256,[Parameter(Mandatory = $true)][string]$ManifestCandidateWorkflowRunId,[Parameter(Mandatory = $true)][string]$ManifestRetainedEvidenceSha256,[Parameter(Mandatory = $true)][string]$BindingScriptPath)
    $baselinePath=(Resolve-Path -LiteralPath $BaselineFile -ErrorAction Stop).Path;$baseline=Get-Content -LiteralPath $baselinePath -Raw|ConvertFrom-Json
    if([int]$baseline.schemaVersion -ne 3 -or [string]$baseline.kind -ne 'DragonDiskForgePostReleaseAcceptanceBaseline' -or [string]$baseline.version -ne $ExpectedVersion){throw 'Canonical post-release baseline identity is invalid.'}
    $candidate=$baseline.retainedCandidate;if($null -eq $candidate){throw 'Canonical post-release baseline is missing retainedCandidate.'}
    $canonicalEvidencePath=[string]$candidate.evidencePath;if($canonicalEvidencePath -ne 'docs/retained-beta-candidate.json'){throw 'Canonical retainedCandidate evidencePath is invalid.'}
    $repoRoot=Split-Path -Parent (Split-Path -Parent $baselinePath);$expectedEvidencePath=[System.IO.Path]::GetFullPath((Join-Path $repoRoot ($canonicalEvidencePath.Replace('/',[System.IO.Path]::DirectorySeparatorChar))))
    $path=(Resolve-Path -LiteralPath $EvidencePath -ErrorAction Stop).Path;$comparison=if($env:OS -eq 'Windows_NT'){[System.StringComparison]::OrdinalIgnoreCase}else{[System.StringComparison]::Ordinal};if(-not [string]::Equals($expectedEvidencePath,[System.IO.Path]::GetFullPath($path),$comparison)){throw 'Retained evidence path does not match baseline retainedCandidate evidencePath.'}
    [int64]$canonicalRunId=0;if(-not [int64]::TryParse([string]$candidate.workflowRunId,[ref]$canonicalRunId)-or $canonicalRunId -le 0){throw 'Baseline retainedCandidate workflowRunId must be a positive integer.'}
    $canonicalSource=Assert-ExactCommit -Commit ([string]$candidate.sourceCommit) -Label 'Baseline retainedCandidate sourceCommit';if($canonicalSource -ne $ExpectedSourceCommit){throw 'Baseline retainedCandidate sourceCommit does not match public sourceCommit.'}
    $canonicalPackage=Assert-HexSha256 -Value ([string]$candidate.packageSha256) -Label 'Baseline retainedCandidate packageSha256';$canonicalInstaller=Assert-HexSha256 -Value ([string]$candidate.installerSha256) -Label 'Baseline retainedCandidate installerSha256';$canonicalEvidenceSha=Assert-HexSha256 -Value ([string]$candidate.retainedEvidenceSha256) -Label 'Baseline retainedCandidate retainedEvidenceSha256'
    $evidence=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    if([int]$evidence.schemaVersion -ne 2 -or [string]$evidence.kind -ne 'DragonDiskForgeRetainedBetaCandidateEvidence'){throw 'Canonical retained evidence identity is invalid.'}
    if([string]$evidence.product -ne 'Dragon DiskForge' -or [string]$evidence.version -ne $ExpectedVersion -or [string]$evidence.architecture -ne 'x64'){throw 'Canonical retained evidence product/version/architecture is invalid.'}
    if([string]$evidence.installerScope -ne 'per-user' -or [bool]$evidence.publicRelease -or [bool]$evidence.betaReady){throw 'Canonical retained evidence must remain per-user, non-public, and not beta-ready.'}
    $source=Assert-ExactCommit -Commit ([string]$evidence.sourceCommit) -Label 'Retained sourceCommit';[int64]$runId=0;if(-not [int64]::TryParse([string]$evidence.workflowRunId,[ref]$runId)-or $runId -le 0){throw 'Retained workflowRunId must be a positive integer.'}
    $packageSha=Assert-HexSha256 -Value ([string]$evidence.packageSha256) -Label 'Retained packageSha256';$installerSha=Assert-HexSha256 -Value ([string]$evidence.installerSha256) -Label 'Retained installerSha256';$evidenceSha=Get-Sha256 -Path $path
    if($source -ne $canonicalSource -or $runId -ne $canonicalRunId -or $packageSha -ne $canonicalPackage -or $installerSha -ne $canonicalInstaller -or $evidenceSha -ne $canonicalEvidenceSha){throw 'Retained evidence identity does not exactly match baseline retainedCandidate Candidate #278.'}
    $manifestEvidenceSha=Assert-HexSha256 -Value $ManifestRetainedEvidenceSha256 -Label 'Manifest retainedEvidenceSha256';if($evidenceSha -ne $manifestEvidenceSha){throw "Canonical retained evidence SHA-256 '$evidenceSha' does not match manifest '$manifestEvidenceSha'."}
    $bindingPath=(Resolve-Path -LiteralPath $BindingScriptPath -ErrorAction Stop).Path;. $bindingPath -Mode library
    if($null -eq (Get-Command Assert-RetainedPublicBinding -CommandType Function -ErrorAction SilentlyContinue)){throw 'Retained/public binding helper did not expose Assert-RetainedPublicBinding.'}
    $identity=[pscustomobject]@{sourceCommit=$source;workflowRunId=$runId;packageSha256=$packageSha;installerSha256=$installerSha;retainedEvidenceSha256=$evidenceSha}
    $binding=Assert-RetainedPublicBinding -RetainedIdentity $identity -PublicSourceCommit $ExpectedSourceCommit -PublicPackageSha256 $PublicPackageSha256 -PublicInstallerSha256 $PublicInstallerSha256 -ManifestCandidateWorkflowRunId $ManifestCandidateWorkflowRunId -ManifestRetainedEvidenceSha256 $manifestEvidenceSha
    if(-not [bool]$binding.retainedIdentityMatched){throw 'Retained/public identity binding did not produce retainedIdentityMatched=true.'};return $binding
}

function Invoke-RuntimeInstallerVerification {
    param([Parameter(Mandatory = $true)][string]$DownloadedInstallerPath,[Parameter(Mandatory = $true)][string]$Version,[Parameter(Mandatory = $true)][string]$SmokeScriptPath,[Parameter(Mandatory = $true)][string]$InstallerVerifierPath)
    if($env:OS -ne 'Windows_NT'){throw 'Post-release runtime installer verification must run on Windows.'}
    $smoke=(Resolve-Path -LiteralPath $SmokeScriptPath -ErrorAction Stop).Path;$installerVerifier=(Resolve-Path -LiteralPath $InstallerVerifierPath -ErrorAction Stop).Path;$powershell=Get-Command powershell.exe -ErrorAction SilentlyContinue
    if($null -eq $powershell){throw 'Windows PowerShell is required to execute the public installer smoke contract.'}
    & $powershell.Source -NoLogo -NoProfile -ExecutionPolicy Bypass -File $smoke -Mode verify -InstallerPath $DownloadedInstallerPath -InstallerVerifierPath $installerVerifier -ExpectedVersion $Version
    if($LASTEXITCODE -ne 0){throw "Downloaded public installer failed runtime smoke verification with exit code $LASTEXITCODE."};return $true
}

function Invoke-PublicReleaseVerification {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryName,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][string]$ToolingCommit,
        [Parameter(Mandatory = $true)][string]$VerifierScriptPath,
        [Parameter(Mandatory = $true)][string]$PackageVerifier,
        [Parameter(Mandatory = $true)][string]$InstallerVerifier,
        [bool]$PreserveDownloads
    )

    $repo = Assert-RepositoryName -Value $RepositoryName
    Assert-VersionAndTag -Version $Version -Tag $Tag | Out-Null
    $commit = Assert-ExactCommit -Commit $SourceCommit -Label 'ExpectedSourceCommit'
    $tooling = Assert-ToolingProvenance -RepositoryName $repo -ToolingCommit $ToolingCommit -LocalVerifierPath $VerifierScriptPath -LocalPackageVerifierPath $PackageVerifier -LocalInstallerVerifierPath $InstallerVerifier

    $encodedTag = [Uri]::EscapeDataString($Tag)
    $headers = @{
        'Accept' = 'application/vnd.github+json'
        'User-Agent' = 'DragonDiskForge-PostReleaseVerifier'
        'X-GitHub-Api-Version' = '2022-11-28'
    }

    $release = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$repo/releases/tags/$encodedTag" -Headers $headers
    Assert-PublicReleaseMetadata -Release $release -Tag $Tag -Version $Version

    $commitResponse = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$repo/commits/$encodedTag" -Headers $headers
    $resolvedCommit = Assert-ExactCommit -Commit ([string]$commitResponse.sha) -Label 'Published tag commit'
    if ($resolvedCommit -ne $commit) {
        throw "Published tag resolves to '$resolvedCommit' instead of exact verified source '$commit'."
    }

    $downloadRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-post-release-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
    $succeeded = $false
    try {
        foreach ($assetName in (Get-ExpectedAssetNames -Version $Version)) {
            $asset = @($release.assets | Where-Object { [string]$_.name -eq $assetName })[0]
            $url = [string]$asset.browser_download_url
            Assert-PublicAssetUrl -Url $url -RepositoryName $repo -Tag $Tag
            $destination = Join-Path $downloadRoot $assetName
            Invoke-WebRequest -UseBasicParsing -Uri $url -Headers @{ 'User-Agent' = 'DragonDiskForge-PostReleaseVerifier' } -OutFile $destination
            if (-not (Test-Path -LiteralPath $destination -PathType Leaf) -or (Get-Item -LiteralPath $destination).Length -le 0) {
                throw "Public download of '$assetName' did not produce a non-empty file."
            }
        }

        $bundle = Assert-DownloadedReleaseBundle -Directory $downloadRoot -Version $Version -Tag $Tag -SourceCommit $commit
        Invoke-RuntimePackageVerification -DownloadedPackagePath $bundle.packagePath -PackageSha256 $bundle.packageSha256 -Version $Version -VerifierPath $PackageVerifier -TemporaryRoot $downloadRoot
        $succeeded = $true

        return [pscustomobject]@{
            repository = $repo
            tag = $Tag
            version = $Version
            sourceCommit = $commit
            verifierCommit = $tooling.verifierCommit
            postReleaseVerifierSha256 = $tooling.postReleaseVerifierSha256
            packageVerifierSha256 = $tooling.packageVerifierSha256
            installerVerifierSha256 = $tooling.installerVerifierSha256
            releaseUrl = [string]$release.html_url
            publishedAt = [string]$release.published_at
            packageSha256 = $bundle.packageSha256
            releaseManifestSha256 = $bundle.manifestSha256
            capabilityMatrixSha256 = $bundle.capabilityMatrixSha256
            candidateWorkflowRunId = $bundle.candidateWorkflowRunId
            manualQaEvidenceSha256 = $bundle.manualQaEvidenceSha256
            downloadedFromPublicRelease = $true
            runtimePackageVerified = $true
            exactPublicToolingReadBack = $true
        }
    }
    finally {
        if ($PreserveDownloads) {
            Write-Host "Post-release verification downloads retained at: $downloadRoot"
        }
        elseif (Test-Path -LiteralPath $downloadRoot) {
            Remove-Item -LiteralPath $downloadRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (-not $succeeded) {
            Write-Warning 'Public post-release verification did not complete successfully.'
        }
    }
}

function New-SelfTestRelease {
    param([Parameter(Mandatory = $true)][string]$Version)

    $assets = foreach ($name in (Get-ExpectedAssetNames -Version $Version)) {
        [pscustomobject]@{
            name = $name
            state = 'uploaded'
            size = 1
            browser_download_url = "https://github.com/Swir/Dragon-DiskForge/releases/download/$Version/$name"
        }
    }
    return [pscustomobject]@{
        tag_name = $Version
        draft = $false
        prerelease = $true
        published_at = '2026-09-21T00:00:00Z'
        assets = @($assets)
    }
}

function Invoke-SelfTest {
    $version = '0.5.0-beta.1'
    $tag = $version
    $commit = ('a' * 40)
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ('DragonDiskForge-post-release-self-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null

    try {
        $release = New-SelfTestRelease -Version $version
        Assert-PublicReleaseMetadata -Release $release -Tag $tag -Version $version
        foreach ($asset in @($release.assets)) {
            Assert-PublicAssetUrl -Url ([string]$asset.browser_download_url) -RepositoryName 'Swir/Dragon-DiskForge' -Tag $tag
        }

        $wrongTagUrlRejected = $false
        try {
            Assert-PublicAssetUrl -Url "https://github.com/Swir/Dragon-DiskForge/releases/download/0.5.0-beta.2/file.zip" -RepositoryName 'Swir/Dragon-DiskForge' -Tag $tag
        }
        catch {
            $wrongTagUrlRejected = $true
        }
        if (-not $wrongTagUrlRejected) { throw 'Self-test failed: asset URL from the wrong release tag was accepted.' }

        $rawUrl = Get-PublicRawToolUrl -RepositoryName 'Swir/Dragon-DiskForge' -Commit $commit -RepositoryPath 'scripts/beta-post-release-verify.ps1'
        if ($rawUrl -ne "https://raw.githubusercontent.com/Swir/Dragon-DiskForge/$commit/scripts/beta-post-release-verify.ps1") {
            throw 'Self-test failed: canonical public tooling URL was not generated deterministically.'
        }
        $unsafeToolPathRejected = $false
        try { $null = Get-PublicRawToolUrl -RepositoryName 'Swir/Dragon-DiskForge' -Commit $commit -RepositoryPath '../verify-package.ps1' } catch { $unsafeToolPathRejected = $true }
        if (-not $unsafeToolPathRejected) { throw 'Self-test failed: unsafe tooling repository path was accepted.' }

        $packageName = "DragonDiskForge-$version-win-x64.zip"
        $packagePath = Join-Path $root $packageName
        Write-Utf8NoBom -Path $packagePath -Text "public package fixture`n"
        $packageHash = Get-Sha256 -Path $packagePath
        Write-Utf8NoBom -Path "$packagePath.sha256" -Text ("{0}  {1}{2}" -f $packageHash, $packageName, [Environment]::NewLine)

        $installerName = "DragonDiskForge-$version-win-x64-setup.exe"
        $installerPath = Join-Path $root $installerName
        Write-Utf8NoBom -Path $installerPath -Text "public installer fixture`n"
        $installerHash = Get-Sha256 -Path $installerPath
        Write-Utf8NoBom -Path "$installerPath.sha256" -Text ("{0}  {1}{2}" -f $installerHash, $installerName, [Environment]::NewLine)

        $capabilityName = "SUPPORTED-CAPABILITIES-$version.md"
        $capabilityPath = Join-Path $root $capabilityName
        Write-Utf8NoBom -Path $capabilityPath -Text "# capability fixture`n"
        $capabilityHash = Get-Sha256 -Path $capabilityPath

        $manifestName = "release-manifest-$version.json"
        $manifestPath = Join-Path $root $manifestName
        $manifest = [ordered]@{
            schemaVersion = 2
            kind = 'DragonDiskForgeBetaReleaseBundle'
            product = 'Dragon DiskForge'
            version = $version
            tag = $tag
            releaseKind = 'github-prerelease'
            architecture = 'x64'
            sourceCommit = $commit
            candidateWorkflowRunId = '123456'
            candidateMetadataSha256 = ('1' * 64)
            qaKitManifestSha256 = ('2' * 64)
            retainedEvidenceSha256 = ('5' * 64)
            manualQaEvidenceSha256 = ('3' * 64)
            packageFile = $packageName
            packageSha256 = $packageHash
            installerFile = $installerName
            installerSha256 = $installerHash
            installerScope = 'per-user'
            releaseNotesFile = "RELEASE-NOTES-$version.md"
            releaseNotesSha256 = ('4' * 64)
            capabilityMatrixRepositoryPath = 'docs/SUPPORTED-CAPABILITIES.md'
            capabilityMatrixReleaseFile = $capabilityName
            capabilityMatrixSha256 = $capabilityHash
            proofState = 'verified'
            publicationIntent = 'public-github-prerelease'
            candidateCreatedUtc = '2026-09-20T00:00:00Z'
        }
        Write-Utf8NoBom -Path $manifestPath -Text (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
        $manifestHash = Get-Sha256 -Path $manifestPath
        Write-Utf8NoBom -Path "$manifestPath.sha256" -Text ("{0}  {1}{2}" -f $manifestHash, $manifestName, [Environment]::NewLine)

        $verified = Assert-DownloadedReleaseBundle -Directory $root -Version $version -Tag $tag -SourceCommit $commit
        if ($verified.packageSha256 -ne $packageHash) { throw 'Self-test failed: package hash did not round-trip.' }

        $tamperedRelease = New-SelfTestRelease -Version $version
        $tamperedRelease.prerelease = $false
        $rejected = $false
        try { Assert-PublicReleaseMetadata -Release $tamperedRelease -Tag $tag -Version $version } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: non-prerelease state was accepted.' }

        Write-Utf8NoBom -Path $packagePath -Text "tampered public package fixture`n"
        $rejected = $false
        try { $null = Assert-DownloadedReleaseBundle -Directory $root -Version $version -Tag $tag -SourceCommit $commit } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: tampered public package was accepted.' }

        Write-Utf8NoBom -Path $packagePath -Text "public package fixture`n"
        Write-Utf8NoBom -Path $installerPath -Text "tampered public installer fixture`n"
        $rejected = $false
        try { $null = Assert-DownloadedReleaseBundle -Directory $root -Version $version -Tag $tag -SourceCommit $commit } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: tampered public installer was accepted.' }

        Write-Utf8NoBom -Path $installerPath -Text "public installer fixture`n"
        $rejected = $false
        try { $null = Assert-DownloadedReleaseBundle -Directory $root -Version $version -Tag $tag -SourceCommit ('b' * 40) } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: wrong expected source commit was accepted.' }

        $missingAssetRelease = New-SelfTestRelease -Version $version
        $missingAssetRelease.assets = @($missingAssetRelease.assets | Where-Object { [string]$_.name -ne $capabilityName })
        $rejected = $false
        try { Assert-PublicReleaseMetadata -Release $missingAssetRelease -Tag $tag -Version $version } catch { $rejected = $true }
        if (-not $rejected) { throw 'Self-test failed: missing required public asset was accepted.' }

        Write-Host 'Dragon DiskForge post-release verification self-test passed.'
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Mode -eq 'self-test') {
    Invoke-SelfTest
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ExpectedSourceCommit)) {
    throw 'ExpectedSourceCommit is required in verify mode; post-release verification must be bound to the exact approved source commit.'
}
if ([string]::IsNullOrWhiteSpace($ExpectedVerifierCommit)) {
    throw 'ExpectedVerifierCommit is required in verify mode; post-release verification tooling must be bound to an exact public repository commit.'
}

$result = Invoke-PublicReleaseVerification -RepositoryName $Repository -Tag $TagName -Version $ExpectedVersion -SourceCommit $ExpectedSourceCommit -ToolingCommit $ExpectedVerifierCommit -VerifierScriptPath $PSCommandPath -PackageVerifier $VerifyPackageScriptPath -InstallerVerifier $VerifyInstallerScriptPath -PreserveDownloads $KeepDownloads.IsPresent
Write-Host 'Dragon DiskForge public beta post-release verification passed.'
Write-Host ("Release: {0}" -f $result.releaseUrl)
Write-Host ("Tag/source: {0} -> {1}" -f $result.tag, $result.sourceCommit)
Write-Host ("Verifier tooling commit: {0}" -f $result.verifierCommit)
Write-Host ("Post-release verifier SHA-256: {0}" -f $result.postReleaseVerifierSha256)
Write-Host ("Package verifier SHA-256: {0}" -f $result.packageVerifierSha256)
Write-Host ("Installer verifier SHA-256: {0}" -f $result.installerVerifierSha256)
Write-Host ("Package SHA-256: {0}" -f $result.packageSha256)
Write-Host ("Release manifest SHA-256: {0}" -f $result.releaseManifestSha256)
Write-Host ("Capability matrix SHA-256: {0}" -f $result.capabilityMatrixSha256)
