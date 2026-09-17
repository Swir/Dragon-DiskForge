$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$viewPaths = @(
    'src/DragonDiskForge.App/Views/DirectBrowseView.xaml',
    'src/DragonDiskForge.App/Views/ExplorerView.xaml',
    'src/DragonDiskForge.App/Views/ExplorerWorkspaceView.xaml',
    'src/DragonDiskForge.App/Views/ImagesView.xaml',
    'src/DragonDiskForge.App/Views/MountedView.xaml'
)

$failures = [System.Collections.Generic.List[string]]::new()

function Get-AttributeValue {
    param(
        [System.Xml.XmlNode] $Node,
        [string] $Name
    )

    foreach ($attribute in $Node.Attributes) {
        if ($attribute.Name -eq $Name) {
            return $attribute.Value
        }
    }

    return $null
}

function Test-Attribute {
    param(
        [System.Xml.XmlNode] $Node,
        [string] $Name
    )

    return $null -ne (Get-AttributeValue -Node $Node -Name $Name)
}

foreach ($relativePath in $viewPaths) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        $failures.Add("Missing accessibility surface: $relativePath")
        continue
    }

    [xml] $document = Get-Content -LiteralPath $path -Raw -Encoding UTF8

    $automationAttributes = @($document.SelectNodes('//@*[starts-with(name(), "AutomationProperties.")]'))
    if ($automationAttributes.Count -eq 0) {
        $failures.Add("$relativePath has no AutomationProperties metadata.")
    }

    foreach ($button in @($document.SelectNodes("//*[local-name()='Button']"))) {
        $content = Get-AttributeValue -Node $button -Name 'Content'
        $automationName = Get-AttributeValue -Node $button -Name 'AutomationProperties.Name'
        if ([string]::IsNullOrWhiteSpace($content) -and [string]::IsNullOrWhiteSpace($automationName)) {
            $failures.Add("$relativePath contains an unlabeled Button.")
        }
    }

    foreach ($textBox in @($document.SelectNodes("//*[local-name()='TextBox']"))) {
        $automationName = Get-AttributeValue -Node $textBox -Name 'AutomationProperties.Name'
        if ([string]::IsNullOrWhiteSpace($automationName)) {
            $failures.Add("$relativePath contains a TextBox without AutomationProperties.Name.")
        }
    }

    foreach ($ring in @($document.SelectNodes("//*[local-name()='ProgressRing']"))) {
        $automationName = Get-AttributeValue -Node $ring -Name 'AutomationProperties.Name'
        if ([string]::IsNullOrWhiteSpace($automationName)) {
            $failures.Add("$relativePath contains a ProgressRing without AutomationProperties.Name.")
        }
    }

    foreach ($infoBar in @($document.SelectNodes("//*[local-name()='InfoBar']"))) {
        if (-not (Test-Attribute -Node $infoBar -Name 'AutomationProperties.LiveSetting')) {
            $failures.Add("$relativePath contains an InfoBar without a polite/assertive live setting.")
        }
    }

    foreach ($collection in @($document.SelectNodes("//*[local-name()='ListView' or local-name()='TabView']"))) {
        $automationName = Get-AttributeValue -Node $collection -Name 'AutomationProperties.Name'
        if ([string]::IsNullOrWhiteSpace($automationName)) {
            $failures.Add("$relativePath contains a ListView/TabView without AutomationProperties.Name.")
        }
    }

    $accessKeys = @($document.SelectNodes('//@AccessKey') | ForEach-Object { $_.Value.ToUpperInvariant() })
    $duplicates = @($accessKeys | Group-Object | Where-Object Count -gt 1)
    foreach ($duplicate in $duplicates) {
        $failures.Add("$relativePath reuses AccessKey '$($duplicate.Name)' $($duplicate.Count) times.")
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'Accessibility contract FAILED:' -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host " - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host 'Accessibility contract passed.' -ForegroundColor Green
Write-Host 'Verified accessible labels for buttons/text boxes/progress, live status regions, named collections, and per-view unique access keys.'
