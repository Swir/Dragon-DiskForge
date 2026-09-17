[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageZip,

    [Parameter(Mandatory = $true)]
    [string]$ChecksumFile,

    [Parameter(Mandatory = $true)]
    [string]$EvidencePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = & $Executable @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "'$Executable $($Arguments -join ' ')' failed with exit code $exitCode.`n$($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function Assert-EqualHash {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $actual = (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected.ToLowerInvariant()) {
        throw "$Label SHA-256 mismatch. Expected $Expected, got $actual."
    }
    return $actual
}

$zip = (Resolve-Path $PackageZip).Path
$sidecar = (Resolve-Path $ChecksumFile).Path
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)
$evidenceDirectory = Split-Path $evidenceFullPath -Parent
if (-not (Test-Path $evidenceDirectory)) {
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
}

$checksumLine = (Get-Content -Path $sidecar -Raw).Trim()
if ($checksumLine -notmatch '^([0-9a-fA-F]{64})\s+(.+)$') {
    throw "Checksum sidecar has an invalid format."
}
$expectedZipHash = $Matches[1].ToLowerInvariant()
$expectedZipName = $Matches[2].Trim()
if ($expectedZipName -ne [System.IO.Path]::GetFileName($zip)) {
    throw "Checksum sidecar targets '$expectedZipName' instead of '$([System.IO.Path]::GetFileName($zip))'."
}
$zipHash = Assert-EqualHash -Path $zip -Expected $expectedZipHash -Label "Package ZIP"

$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("DragonDiskForge-clean-runtime-" + [Guid]::NewGuid().ToString("N"))
$extract = Join-Path $workspace "package"
New-Item -ItemType Directory -Path $extract -Force | Out-Null

try {
    Expand-Archive -Path $zip -DestinationPath $extract -Force

    $manifestPath = Join-Path $extract "package-manifest.json"
    if (-not (Test-Path $manifestPath -PathType Leaf)) { throw "package-manifest.json is missing." }
    $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json

    if ($manifest.schemaVersion -lt 4) { throw "Unexpected package manifest schema version '$($manifest.schemaVersion)'." }
    if ($manifest.architecture -ne "x64") { throw "Unexpected package architecture '$($manifest.architecture)'." }
    if ($manifest.debugSymbolsIncluded -ne $false) { throw "Package manifest claims debug symbols are included." }

    $app = Join-Path $extract ([string]$manifest.entryPoint)
    $cli = Join-Path $extract (([string]$manifest.cliEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $shell = Join-Path $extract (([string]$manifest.shellIntegrationEntryPoint).Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    foreach ($required in @($app, $cli, $shell)) {
        if (-not (Test-Path $required -PathType Leaf)) { throw "Required packaged entry point is missing: $required" }
    }

    $appHash = Assert-EqualHash -Path $app -Expected ([string]$manifest.entryPointSha256) -Label "Desktop entry point"
    $cliHash = Assert-EqualHash -Path $cli -Expected ([string]$manifest.cliEntryPointSha256) -Label "CLI entry point"
    $shellHash = Assert-EqualHash -Path $shell -Expected ([string]$manifest.shellIntegrationEntryPointSha256) -Label "Shell entry point"

    $debugFiles = @(Get-ChildItem -Path $extract -Recurse -File -Filter "*.pdb")
    if ($debugFiles.Count -ne 0) { throw "Extracted clean package contains PDB files." }
    $testFiles = @(Get-ChildItem -Path $extract -Recurse -File | Where-Object { $_.FullName -match '[\\/]tests?[\\/]' -or $_.Name -match '(?i)(SmokeTests|IntegrationTests)' })
    if ($testFiles.Count -ne 0) { throw "Extracted clean package contains test-only files." }

    # Runtime jobs intentionally do not check out the repository. Reduce PATH so the
    # self-contained entry points cannot accidentally depend on SDK/toolchain folders.
    $originalPath = $env:PATH
    $env:DOTNET_ROOT = ""
    $env:DOTNET_ROOT_X64 = ""
    $env:PATH = [string]::Join(';', @($originalPath -split ';' | Where-Object {
        $_ -and $_ -notmatch '(?i)\\dotnet($|\\)' -and $_ -notmatch '(?i)Visual Studio' -and $_ -notmatch '(?i)Git\\cmd'
    }))

    Invoke-Checked -Executable $cli -Arguments @('--help') | Out-Null

    $formatsText = Invoke-Checked -Executable $cli -Arguments @('formats', '--format', 'json')
    $formatsJson = ($formatsText -join [Environment]::NewLine) | ConvertFrom-Json
    $providers = if ($formatsJson -is [System.Array]) { @($formatsJson) } elseif ($null -ne $formatsJson.Providers) { @($formatsJson.Providers) } else { @($formatsJson) }
    if ($providers.Count -lt 8) { throw "Packaged CLI returned only $($providers.Count) provider records." }

    $sample = Join-Path $workspace "clean-machine-sample.img"
    $sampleBytes = New-Object byte[] (1024 * 1024)
    [System.IO.File]::WriteAllBytes($sample, $sampleBytes)

    $analyzeText = Invoke-Checked -Executable $cli -Arguments @('analyze', $sample, '--format', 'json')
    $analyze = ($analyzeText -join [Environment]::NewLine) | ConvertFrom-Json
    if ($null -eq $analyze) { throw "Analyze returned no JSON object." }

    $verifyText = Invoke-Checked -Executable $cli -Arguments @('verify', $sample, '--format', 'json')
    $verify = ($verifyText -join [Environment]::NewLine) | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$verify.Sha256) -or ([string]$verify.Sha256).Length -ne 64) {
        throw "Verify did not return a 64-character SHA-256 digest."
    }
    if ([string]::IsNullOrWhiteSpace([string]$verify.Sha512) -or ([string]$verify.Sha512).Length -ne 128) {
        throw "Verify did not return a 128-character SHA-512 digest."
    }

    $portableState = Join-Path $workspace "portable-state.json"
    Invoke-Checked -Executable $cli -Arguments @('state-show', '--state', $portableState, '--format', 'json') | Out-Null
    Invoke-Checked -Executable $cli -Arguments @('restore-last-image', 'off', '--state', $portableState) | Out-Null
    $supportZip = Join-Path $workspace "support.zip"
    Invoke-Checked -Executable $cli -Arguments @('diagnostics', $supportZip, '--state', $portableState) | Out-Null
    if (-not (Test-Path $supportZip -PathType Leaf)) { throw "Diagnostics did not create a support ZIP." }

    Invoke-Checked -Executable $shell -Arguments @('--help') | Out-Null
    Invoke-Checked -Executable $shell -Arguments @('register') | Out-Null
    try {
        Invoke-Checked -Executable $shell -Arguments @('status') | Out-Null
    }
    finally {
        Invoke-Checked -Executable $shell -Arguments @('unregister') | Out-Null
    }

    $appVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($app)
    if ([string]::IsNullOrWhiteSpace([string]$appVersion.ProductVersion)) {
        throw "Desktop entry point has no ProductVersion."
    }

    $os = Get-CimInstance Win32_OperatingSystem
    $evidence = [ordered]@{
        schemaVersion = 1
        product = [string]$manifest.product
        packageVersion = [string]$manifest.version
        osCaption = [string]$os.Caption
        osVersion = [string]$os.Version
        osBuild = [string]$os.BuildNumber
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        packageSha256 = $zipHash
        appSha256 = $appHash
        cliSha256 = $cliHash
        shellSha256 = $shellHash
        providerCount = $providers.Count
        analyzeJson = $true
        verifySha256 = [string]$verify.Sha256
        verifySha512 = [string]$verify.Sha512
        isolatedState = Test-Path $portableState -PathType Leaf
        diagnosticsZip = Test-Path $supportZip -PathType Leaf
        shellRegisterStatusUnregister = $true
        repositoryCheckoutPresent = Test-Path (Join-Path (Get-Location) '.git')
        utc = [DateTimeOffset]::UtcNow.ToString('O')
    }

    $evidence | ConvertTo-Json -Depth 5 | Set-Content -Path $evidenceFullPath -Encoding utf8NoBOM
    Write-Host "Clean-machine package runtime probe passed on $($evidence.osCaption) build $($evidence.osBuild)."
    Write-Host "Evidence: $evidenceFullPath"
}
finally {
    if (Test-Path $workspace) {
        Remove-Item $workspace -Recurse -Force -ErrorAction SilentlyContinue
    }
}
