[CmdletBinding()]
param(
    [ValidateSet('verify', 'self-test')]
    [string]$Mode = 'verify',

    [string]$Repository = 'Swir/Dragon-DiskForge',
    [string]$TagName = '0.5.0-beta.1',
    [string]$ExpectedVersion = '0.5.0-beta.1',
    [string]$ExpectedSourceCommit = '',
    [string]$VerifyPackageScriptPath = 'scripts/verify-package.ps1',
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
        [Parameter(Mandatory = $true)][string]$RepositoryName
    )

    $uri = [Uri]$Url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com') {
        throw "Release asset URL is not an HTTPS github.com URL: $Url"
    }
    $prefix = '/' + $RepositoryName + '/releases/download/'
    if (-not $uri.AbsolutePath.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
        throw "Release asset URL is outside the expected repository release path: $Url"
    }
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
    $manifestName = "release-manifest-$Version.json"
    $manifestPath = Join-Path $Directory $manifestName
    $manifestSidecarPath = "$manifestPath.sha256"
    $capabilityName = "SUPPORTED-CAPABILITIES-$Version.md"
    $capabilityPath = Join-Path $Directory $capabilityName

    foreach ($required in @($packagePath, $packageSidecarPath, $manifestPath, $manifestSidecarPath, $capabilityPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Downloaded public release is missing '$required'."
        }
    }

    $declaredPackageHash = Read-Sha256Sidecar -Path $packageSidecarPath -ExpectedFileName $packageName -Label 'Public package SHA-256 sidecar'
    $actualPackageHash = Get-Sha256 -Path $packagePath
    if ($declaredPackageHash -ne $actualPackageHash) {
        throw "Downloaded public package SHA-256 mismatch. Declared '$declaredPackageHash', actual '$actualPackageHash'."
    }

    $declaredManifestHash = Read-Sha256Sidecar -Path $manifestSidecarPath -ExpectedFileName $manifestName -Label 'Public release-manifest SHA-256 sidecar'
    $actualManifestHash = Get-Sha256 -Path $manifestPath
    if ($declaredManifestHash -ne $actualManifestHash) {
        throw "Downloaded public release manifest SHA-256 mismatch. Declared '$declaredManifestHash', actual '$actualManifestHash'."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported public release manifest schema '$($manifest.schemaVersion)'." }
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

    foreach ($proofField in @('candidateMetadataSha256', 'qaKitManifestSha256', 'manualQaEvidenceSha256', 'releaseNotesSha256')) {
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
        manifestPath = $manifestPath
        manifestSha256 = $actualManifestHash
        capabilityMatrixPath = $capabilityPath
        capabilityMatrixSha256 = $actualCapabilityHash
        candidateWorkflowRunId = [string]$manifest.candidateWorkflowRunId
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

function Invoke-PublicReleaseVerification {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryName,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][string]$PackageVerifier,
        [bool]$PreserveDownloads
    )

    $repo = Assert-RepositoryName -Value $RepositoryName
    Assert-VersionAndTag -Version $Version -Tag $Tag | Out-Null
    $commit = Assert-ExactCommit -Commit $SourceCommit -Label 'ExpectedSourceCommit'

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
            Assert-PublicAssetUrl -Url $url -RepositoryName $repo
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
            releaseUrl = [string]$release.html_url
            publishedAt = [string]$release.published_at
            packageSha256 = $bundle.packageSha256
            releaseManifestSha256 = $bundle.manifestSha256
            capabilityMatrixSha256 = $bundle.capabilityMatrixSha256
            candidateWorkflowRunId = $bundle.candidateWorkflowRunId
            manualQaEvidenceSha256 = $bundle.manualQaEvidenceSha256
            downloadedFromPublicRelease = $true
            runtimePackageVerified = $true
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
            Assert-PublicAssetUrl -Url ([string]$asset.browser_download_url) -RepositoryName 'Swir/Dragon-DiskForge'
        }

        $packageName = "DragonDiskForge-$version-win-x64.zip"
        $packagePath = Join-Path $root $packageName
        Write-Utf8NoBom -Path $packagePath -Text "public package fixture`n"
        $packageHash = Get-Sha256 -Path $packagePath
        Write-Utf8NoBom -Path "$packagePath.sha256" -Text ("{0}  {1}{2}" -f $packageHash, $packageName, [Environment]::NewLine)

        $capabilityName = "SUPPORTED-CAPABILITIES-$version.md"
        $capabilityPath = Join-Path $root $capabilityName
        Write-Utf8NoBom -Path $capabilityPath -Text "# capability fixture`n"
        $capabilityHash = Get-Sha256 -Path $capabilityPath

        $manifestName = "release-manifest-$version.json"
        $manifestPath = Join-Path $root $manifestName
        $manifest = [ordered]@{
            schemaVersion = 1
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
            manualQaEvidenceSha256 = ('3' * 64)
            packageFile = $packageName
            packageSha256 = $packageHash
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

$result = Invoke-PublicReleaseVerification -RepositoryName $Repository -Tag $TagName -Version $ExpectedVersion -SourceCommit $ExpectedSourceCommit -PackageVerifier $VerifyPackageScriptPath -PreserveDownloads $KeepDownloads.IsPresent
Write-Host 'Dragon DiskForge public beta post-release verification passed.'
Write-Host ("Release: {0}" -f $result.releaseUrl)
Write-Host ("Tag/source: {0} -> {1}" -f $result.tag, $result.sourceCommit)
Write-Host ("Package SHA-256: {0}" -f $result.packageSha256)
Write-Host ("Release manifest SHA-256: {0}" -f $result.releaseManifestSha256)
Write-Host ("Capability matrix SHA-256: {0}" -f $result.capabilityMatrixSha256)
