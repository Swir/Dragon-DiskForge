[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Write,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$CliProject = Join-Path $RepoRoot 'src/DragonDiskForge.Cli/DragonDiskForge.Cli.csproj'
$DocumentPath = Join-Path $RepoRoot 'docs/SUPPORTED-CAPABILITIES.md'
$AllowedCapabilities = @(
    'Inspect',
    'DirectBrowse',
    'Search',
    'CopyOut',
    'PartitionTable',
    'MediaGeometry',
    'TrackLayout',
    'VirtualDiskMetadata',
    'ContainerMetadata'
)

function Normalize-Text {
    param([Parameter(Mandatory = $true)][string]$Value)

    $normalized = $Value.Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd()
    return $normalized + "`n"
}

function Escape-MarkdownCell {
    param([Parameter(Mandatory = $true)][string]$Value)

    return $Value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Get-CapabilityNames {
    param([Parameter(Mandatory = $true)][string]$Value)

    $result = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::Ordinal)
    foreach ($name in ($Value -split ',')) {
        $trimmed = $name.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -eq 'None') {
            continue
        }
        if ($AllowedCapabilities -notcontains $trimmed) {
            throw "Unknown provider capability '$trimmed'. Update the matrix contract deliberately before documenting it."
        }
        [void]$result.Add($trimmed)
    }

    return ,$result
}

function Assert-ProviderCatalog {
    param([Parameter(Mandatory = $true)][object[]]$Providers)

    if ($Providers.Count -eq 0) {
        throw 'The canonical provider registry returned no providers.'
    }

    $ids = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
    $previousPriority = [int]::MaxValue

    foreach ($provider in $Providers) {
        $id = [string]$provider.Id
        $displayName = [string]$provider.DisplayName
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($displayName)) {
            throw 'Every provider must have a non-empty id and display name.'
        }
        if (-not $ids.Add($id)) {
            throw "Duplicate provider id '$id' in canonical registry output."
        }

        $priority = [int]$provider.Priority
        if ($priority -gt $previousPriority) {
            throw "Provider registry output is not in deterministic descending-priority order at '$id'."
        }
        $previousPriority = $priority

        $extensions = @($provider.Extensions)
        if ($extensions.Count -eq 0) {
            throw "Provider '$id' has no declared extensions; document signature-only providers explicitly before accepting them."
        }

        $seenExtensions = New-Object 'System.Collections.Generic.HashSet[string]' -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
        foreach ($extensionValue in $extensions) {
            $extension = [string]$extensionValue
            if ($extension -notmatch '^\.[A-Za-z0-9._-]+$') {
                throw "Provider '$id' emitted invalid extension '$extension'."
            }
            if (-not $seenExtensions.Add($extension)) {
                throw "Provider '$id' emitted duplicate extension '$extension'."
            }
        }

        $capabilities = Get-CapabilityNames ([string]$provider.Capabilities)
        if (-not $capabilities.Contains('Inspect')) {
            throw "Provider '$id' does not expose the mandatory Inspect capability."
        }
    }
}

function Format-Extensions {
    param([Parameter(Mandatory = $true)][object[]]$Extensions)

    $cells = foreach ($extensionValue in $Extensions) {
        '`' + (Escape-MarkdownCell ([string]$extensionValue)) + '`'
    }
    return ($cells -join '<br>')
}

function Capability-Mark {
    param(
        [Parameter(Mandatory = $true)]$Capabilities,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Capabilities.Contains($Name)) { return '✅' }
    return '—'
}

function ConvertTo-CapabilityMatrixMarkdown {
    param([Parameter(Mandatory = $true)][object[]]$Providers)

    Assert-ProviderCatalog $Providers

    $lines = New-Object 'System.Collections.Generic.List[string]'
    [void]$lines.Add('# Dragon DiskForge — Supported Provider Capabilities')
    [void]$lines.Add('')
    [void]$lines.Add('> This document is generated from the canonical Core provider registry exposed by `dragon-diskforge formats --format json`. Do not hand-edit provider rows; update the implementation and regenerate the matrix.')
    [void]$lines.Add('')
    [void]$lines.Add('This matrix describes **provider-layer capabilities only**. It does not turn metadata-only providers into full decoders, does not imply write support, and does not replace the separate native Windows mount contract or beta release gates.')
    [void]$lines.Add('')
    [void]$lines.Add('## Canonical provider matrix')
    [void]$lines.Add('')
    [void]$lines.Add('| Priority | Provider | Extensions | Inspect | Browse | Search | Copy out | Partitions | Geometry | Tracks | Virtual metadata | Container metadata |')
    [void]$lines.Add('|---:|---|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|')

    foreach ($provider in $Providers) {
        $capabilities = Get-CapabilityNames ([string]$provider.Capabilities)
        $providerCell = (Escape-MarkdownCell ([string]$provider.DisplayName)) + ' (`' + (Escape-MarkdownCell ([string]$provider.Id)) + '`)'
        $extensions = Format-Extensions @($provider.Extensions)
        $formatArgs = @(
            ([int]$provider.Priority),
            $providerCell,
            $extensions,
            (Capability-Mark $capabilities 'Inspect'),
            (Capability-Mark $capabilities 'DirectBrowse'),
            (Capability-Mark $capabilities 'Search'),
            (Capability-Mark $capabilities 'CopyOut'),
            (Capability-Mark $capabilities 'PartitionTable'),
            (Capability-Mark $capabilities 'MediaGeometry'),
            (Capability-Mark $capabilities 'TrackLayout'),
            (Capability-Mark $capabilities 'VirtualDiskMetadata'),
            (Capability-Mark $capabilities 'ContainerMetadata')
        )
        $row = '| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} |' -f $formatArgs
        [void]$lines.Add($row)
    }

    [void]$lines.Add('')
    [void]$lines.Add('### Capability meanings')
    [void]$lines.Add('')
    [void]$lines.Add('- **Inspect** — the provider can validate its supported subset and return bounded image/container metadata.')
    [void]$lines.Add('- **Browse / Search / Copy out** — provider-backed filesystem browsing is implemented without relying on a native mount.')
    [void]$lines.Add('- **Partitions** — the provider exposes a bounded partition-table path.')
    [void]$lines.Add('- **Geometry / Tracks** — the provider exposes bounded media-geometry or optical track-layout evidence.')
    [void]$lines.Add('- **Virtual metadata / Container metadata** — metadata inspection only unless another explicitly documented service supplies a narrower guest-byte path.')
    [void]$lines.Add('')
    [void]$lines.Add('## Important boundaries')
    [void]$lines.Add('')
    [void]$lines.Add('- `.img` intentionally overlaps the CCD and RAW providers. Extension matching only influences deterministic probe order; the selected provider must still validate the content.')
    [void]$lines.Add('- Native Windows **Mount / Unmount** for ISO/VHD/VHDX is a separate Windows integration path and is not represented by provider capability flags.')
    [void]$lines.Add('- ISO9660/Joliet direct browse/search/copy-out is a managed provider path and does not require mounting.')
    [void]$lines.Add('- QCOW2 and hosted-sparse VMDK have bounded guest-byte readers only for the explicitly proven uncompressed subsets documented in [`GUEST-BYTE-READERS.md`](GUEST-BYTE-READERS.md). The matrix does not claim arbitrary QCOW2/VMDK decoding, writing or mounting.')
    [void]$lines.Add('- WIM/ESD and FFU rows describe bounded container metadata. They do not claim file extraction, image application or mutation.')
    [void]$lines.Add('- SHA-256/SHA-512 verification is a shared read-only service and CLI capability for readable files, not a provider capability flag.')
    [void]$lines.Add('')
    [void]$lines.Add('## Native Windows paths outside the provider flags')
    [void]$lines.Add('')
    [void]$lines.Add('| Path | Proven formats/scope | Release boundary |')
    [void]$lines.Add('|---|---|---|')
    [void]$lines.Add('| Native Mount / Unmount | ISO, VHD, VHDX | Read-only-first Windows path; normal-user UAC remains part of interactive beta QA. |')
    [void]$lines.Add('| Managed direct browse | ISO9660/Joliet ISO | Browse, search and safe copy-out without a native mount. |')
    [void]$lines.Add('| Shared verification | Any readable file | SHA-256 + SHA-512 are computed in one bounded sequential pass; this does not imply format decoding. |')
    [void]$lines.Add('')
    [void]$lines.Add('## Release truthfulness')
    [void]$lines.Add('')
    [void]$lines.Add('The matrix is a source-derived capability reference, **not a beta-readiness signal**. Public `0.5.0-beta.1` remains governed by [`BETA-RELEASE.md`](BETA-RELEASE.md), including the outstanding human-confirmed clean-desktop/UAC/Explorer gates.')
    [void]$lines.Add('')
    [void]$lines.Add('## Regenerate / verify')
    [void]$lines.Add('')
    [void]$lines.Add('```powershell')
    [void]$lines.Add('.\scripts\generate-capability-matrix.ps1 -Write')
    [void]$lines.Add('.\scripts\generate-capability-matrix.ps1 -Check')
    [void]$lines.Add('.\scripts\generate-capability-matrix.ps1 -SelfTest')
    [void]$lines.Add('```')
    [void]$lines.Add('')
    [void]$lines.Add('`-Check` builds the Release CLI, asks the canonical registry for JSON provider descriptors, validates ids/extensions/capabilities/order and fails if this committed document is stale.')
    [void]$lines.Add('')
    [void]$lines.Add('---')
    [void]$lines.Add('')
    [void]$lines.Add('Generated capability reference for **Dragon DiskForge** · by Swir · [GitHub](https://github.com/Swir/Dragon-DiskForge)')

    return Normalize-Text ($lines -join "`n")
}

function Get-CanonicalProviders {
    if (-not (Test-Path -LiteralPath $CliProject -PathType Leaf)) {
        throw "CLI project not found: $CliProject"
    }

    $buildRoot = Join-Path ([IO.Path]::GetTempPath()) ('dragon-diskforge-capability-matrix-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null

    try {
        & dotnet build $CliProject --configuration Release --nologo --output $buildRoot | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Release CLI build failed with exit code $LASTEXITCODE."
        }

        $cliDll = Join-Path $buildRoot 'dragon-diskforge.dll'
        if (-not (Test-Path -LiteralPath $cliDll -PathType Leaf)) {
            $outputs = @(Get-ChildItem -LiteralPath $buildRoot -File | Select-Object -ExpandProperty Name)
            throw "Release CLI output not found after build: $cliDll. Outputs: $($outputs -join ', ')"
        }

        $jsonLines = & dotnet $cliDll formats --format json
        if ($LASTEXITCODE -ne 0) {
            throw "Canonical 'formats --format json' command failed with exit code $LASTEXITCODE."
        }

        $json = $jsonLines -join "`n"
        if ([string]::IsNullOrWhiteSpace($json)) {
            throw 'Canonical provider command returned empty output.'
        }

        try {
            $providers = @($json | ConvertFrom-Json)
        }
        catch {
            throw "Canonical provider JSON could not be parsed: $($_.Exception.Message)"
        }

        Assert-ProviderCatalog $providers
        return ,$providers
    }
    finally {
        Remove-Item -LiteralPath $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-SelfTest {
    $sample = @(
        [pscustomobject]@{
            Id = 'sample-direct'
            DisplayName = 'Sample | Direct'
            Extensions = @('.one')
            Capabilities = 'Inspect, DirectBrowse, Search, CopyOut'
            Priority = 20
        },
        [pscustomobject]@{
            Id = 'sample-meta'
            DisplayName = 'Sample Metadata'
            Extensions = @('.two')
            Capabilities = 'Inspect, ContainerMetadata'
            Priority = 10
        }
    )

    $rendered = ConvertTo-CapabilityMatrixMarkdown $sample
    if (-not $rendered.Contains('Sample \| Direct') -or -not $rendered.Contains('| 20 |') -or -not $rendered.Contains('✅')) {
        throw 'Capability-matrix renderer self-test failed.'
    }

    $duplicate = @($sample[0], $sample[0])
    $duplicateRejected = $false
    try { Assert-ProviderCatalog $duplicate } catch { $duplicateRejected = $true }
    if (-not $duplicateRejected) {
        throw 'Duplicate provider id self-test did not fail closed.'
    }

    $unknown = @(
        [pscustomobject]@{
            Id = 'unknown'
            DisplayName = 'Unknown'
            Extensions = @('.bad')
            Capabilities = 'Inspect, ImaginaryCapability'
            Priority = 1
        }
    )
    $unknownRejected = $false
    try { Assert-ProviderCatalog $unknown } catch { $unknownRejected = $true }
    if (-not $unknownRejected) {
        throw 'Unknown capability self-test did not fail closed.'
    }

    Write-Host 'Capability matrix self-tests passed.'
}

if (-not $Check -and -not $Write -and -not $SelfTest) {
    $Check = $true
}
if ($Check -and $Write) {
    throw 'Choose either -Check or -Write, not both.'
}

if ($SelfTest) {
    Invoke-SelfTest
    if (-not $Check -and -not $Write) {
        exit 0
    }
}

$providers = Get-CanonicalProviders
$expected = ConvertTo-CapabilityMatrixMarkdown $providers

if ($Write) {
    $encoding = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [IO.File]::WriteAllText($DocumentPath, $expected, $encoding)
    Write-Host "Updated $DocumentPath from the canonical provider registry."
    exit 0
}

if (-not (Test-Path -LiteralPath $DocumentPath -PathType Leaf)) {
    throw "Capability matrix is missing: $DocumentPath. Run with -Write and commit the generated document."
}

$actual = Normalize-Text (Get-Content -LiteralPath $DocumentPath -Raw)
if ($actual -cne $expected) {
    throw 'docs/SUPPORTED-CAPABILITIES.md is stale or hand-edited. Run scripts/generate-capability-matrix.ps1 -Write and commit the result.'
}

Write-Host 'Capability matrix matches the canonical provider registry.'
