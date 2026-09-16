param([Parameter(Mandatory=$true)][string]$AppPath)
$ErrorActionPreference = 'Stop'
$resolvedApp = (Resolve-Path -LiteralPath $AppPath).Path
$process = Start-Process -FilePath $resolvedApp -WorkingDirectory (Split-Path $resolvedApp) -PassThru -WindowStyle Hidden
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "Packaged app exited before showing its window. Exit code: $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { $ready = $true; break }
    }
    if (-not $ready) { throw 'Packaged app did not create its main window.' }
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited) { throw 'Packaged app exited immediately after startup.' }
    Write-Host 'PASS packaged WinUI app starts and creates its main window.'
} finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { $process.Kill() }
    }
    $process.Dispose()
}
