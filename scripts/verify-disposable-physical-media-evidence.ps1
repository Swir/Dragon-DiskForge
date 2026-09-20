param(
    [string]$EvidencePath,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Sha256 {
    param([string]$Value, [string]$Name)
    Assert-True ($Value -match '^[0-9A-Fa-f]{64}$') "$Name must be a 64-hex SHA-256 value."
}

function Test-DisposableMediaEvidence {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    Assert-True (Test-Path -LiteralPath $fullPath -PathType Leaf) "Evidence JSON does not exist: $fullPath"

    $sidecarPath = $fullPath + '.sha256'
    Assert-True (Test-Path -LiteralPath $sidecarPath -PathType Leaf) "Evidence SHA-256 sidecar does not exist: $sidecarPath"

    $sidecar = (Get-Content -LiteralPath $sidecarPath -Raw).Trim()
    Assert-True ($sidecar -match '^([0-9A-Fa-f]{64})  (.+)$') 'Evidence sidecar must contain: <64-hex SHA-256><two spaces><json filename>.'
    $expectedHash = $Matches[1].ToUpperInvariant()
    $expectedName = $Matches[2]
    Assert-True ($expectedName -ceq [IO.Path]::GetFileName($fullPath)) 'Evidence sidecar filename does not exactly match the JSON filename.'

    $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToUpperInvariant()
    Assert-True ($actualHash -ceq $expectedHash) 'Evidence JSON SHA-256 does not match its sidecar.'

    $evidence = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json
    Assert-True ([int]$evidence.SchemaVersion -eq 1) 'Unsupported disposable-media evidence schema.'
    Assert-True ([string]$evidence.Result -ceq 'pass') 'Evidence does not record a passing real-media validation.'

    $completed = [DateTimeOffset]::MinValue
    Assert-True ([DateTimeOffset]::TryParse([string]$evidence.CompletedUtc, [ref]$completed)) 'CompletedUtc is not a valid timestamp.'
    Assert-True ($completed -le [DateTimeOffset]::UtcNow.AddMinutes(5)) 'CompletedUtc is implausibly in the future.'

    $diskNumber = [int]$evidence.DiskNumber
    Assert-True ($diskNumber -ge 0) 'DiskNumber must be non-negative.'
    Assert-True ([string]$evidence.DevicePath -match '^\\\\\.\\PhysicalDrive\d+$') 'DevicePath must be a canonical \\.\PhysicalDriveN path.'
    Assert-True ([string]$evidence.DevicePath -ceq "\\.\PhysicalDrive$diskNumber") 'DevicePath does not match DiskNumber.'
    Assert-True (-not [string]::IsNullOrWhiteSpace([string]$evidence.StableId)) 'StableId is required.'

    $capacity = [long]$evidence.CapacityBytes
    $sourceLength = [long]$evidence.SourceLengthBytes
    $bytesWritten = [long]$evidence.BytesWritten
    $sector = [int]$evidence.LogicalSectorSizeBytes
    $buffer = [int]$evidence.ExecutionBufferSizeBytes

    Assert-True ($capacity -gt 0) 'CapacityBytes must be positive.'
    Assert-True ($sourceLength -gt 0 -and $sourceLength -le $capacity) 'SourceLengthBytes must be positive and fit inside the destination.'
    Assert-True ($bytesWritten -eq $sourceLength) 'BytesWritten must exactly equal SourceLengthBytes.'
    Assert-True ($sector -gt 0 -and ($sourceLength % $sector) -eq 0) 'Source length must be aligned to LogicalSectorSizeBytes.'
    Assert-True ($buffer -ge 4096 -and $buffer -le 8388608 -and ($buffer % $sector) -eq 0) 'ExecutionBufferSizeBytes is outside the supported aligned range.'

    Assert-Sha256 ([string]$evidence.SourceSha256) 'SourceSha256'
    Assert-Sha256 ([string]$evidence.ExecutionWrittenSha256) 'ExecutionWrittenSha256'
    Assert-Sha256 ([string]$evidence.ReadbackSha256) 'ReadbackSha256'
    Assert-Sha256 ([string]$evidence.ConfirmationTokenSha256) 'ConfirmationTokenSha256'

    $sourceHash = ([string]$evidence.SourceSha256).ToUpperInvariant()
    Assert-True (([string]$evidence.ExecutionWrittenSha256).ToUpperInvariant() -ceq $sourceHash) 'ExecutionWrittenSha256 does not match SourceSha256.'
    Assert-True (([string]$evidence.ReadbackSha256).ToUpperInvariant() -ceq $sourceHash) 'ReadbackSha256 does not match SourceSha256.'
    Assert-True ([string]$evidence.ExecutionStatus -ceq 'Completed') 'ExecutionStatus must be Completed.'
    Assert-True (-not [bool]$evidence.RequiresRecovery) 'Passing evidence cannot require recovery.'
    Assert-True ([bool]$evidence.TargetVolumeLockDismountCompleted) 'Target lock/dismount completion evidence is missing.'
    Assert-True ([bool]$evidence.DeviceFlushCompleted) 'Device flush completion evidence is missing.'
    Assert-True ([bool]$evidence.ReadbackVerified) 'Independent read-back verification evidence is missing.'

    $sourceBacking = @($evidence.SourceBackingDiskNumbers | ForEach-Object { [int]$_ })
    Assert-True ($sourceBacking.Count -gt 0) 'SourceBackingDiskNumbers must contain proven source topology.'
    Assert-True (-not ($sourceBacking -contains $diskNumber)) 'Source backing disks include the destructive destination.'

    $evidenceBacking = @($evidence.EvidenceBackingDiskNumbers | ForEach-Object { [int]$_ })
    Assert-True ($evidenceBacking.Count -gt 0) 'EvidenceBackingDiskNumbers must contain proven local evidence-storage topology.'
    foreach ($evidenceDisk in $evidenceBacking) {
        Assert-True ($evidenceDisk -ge 0) 'EvidenceBackingDiskNumbers contains an invalid physical disk number.'
    }
    Assert-True (-not ($evidenceBacking -contains $diskNumber)) 'Evidence storage is backed by the destructive destination disk.'

    $preflight = @($evidence.PreflightEvidence | ForEach-Object { [string]$_ })
    Assert-True ($preflight -contains 'windows-preflight:read-only') 'Read-only Windows preflight marker is missing.'
    Assert-True ($preflight -contains "logical-sector-bytes:$sector") 'Logical-sector preflight evidence does not match the recorded sector size.'
    foreach ($sourceDisk in $sourceBacking) {
        Assert-True ($preflight -contains "source-backing-disk:$sourceDisk") "Source backing disk $sourceDisk is not bound into preflight evidence."
    }

    [pscustomobject]@{
        EvidencePath = $fullPath
        EvidenceSha256 = $actualHash
        DiskNumber = $diskNumber
        StableId = [string]$evidence.StableId
        SourceSha256 = $sourceHash
        BytesWritten = $bytesWritten
        LogicalSectorSizeBytes = $sector
        CompletedUtc = $completed.ToUniversalTime().ToString('o')
    }
}

function Write-SelfTestEvidence {
    param(
        [string]$Path,
        [string]$Result = 'pass',
        [int[]]$EvidenceBackingDiskNumbers = @(0)
    )

    $obj = [ordered]@{
        SchemaVersion = 1
        Result = $Result
        CompletedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        OsVersion = 'self-test'
        ProcessArchitecture = 'X64'
        DiskNumber = 7
        DevicePath = '\\.\PhysicalDrive7'
        StableId = 'SELFTEST-STABLE-ID'
        DisplayName = 'Synthetic disposable-media witness'
        CapacityBytes = 1048576
        BusType = 'USB'
        IsRemovable = $true
        SourceFileName = 'fixture.img'
        SourceLengthBytes = 4096
        SourceSha256 = ('A' * 64)
        SourceBackingDiskNumbers = @(0)
        EvidenceBackingDiskNumbers = $EvidenceBackingDiskNumbers
        LogicalSectorSizeBytes = 512
        ExecutionBufferSizeBytes = 4096
        BytesWritten = 4096
        ExecutionStatus = 'Completed'
        ExecutionWrittenSha256 = ('A' * 64)
        RequiresRecovery = $false
        TargetVolumeLockDismountCompleted = $true
        DeviceFlushCompleted = $true
        ReadbackVerified = $true
        ReadbackSha256 = ('A' * 64)
        ConfirmationTokenSha256 = ('B' * 64)
        PreflightEvidence = @('windows-preflight:read-only', 'planned-disk-number:7', 'source-backing-disk:0', 'logical-sector-bytes:512')
    }

    $json = $obj | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    [IO.File]::WriteAllText($Path + '.sha256', "$hash  $([IO.Path]::GetFileName($Path))$([Environment]::NewLine)", [Text.UTF8Encoding]::new($false))
}

if ($SelfTest) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('ddf-disposable-evidence-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root | Out-Null
    try {
        $path = Join-Path $root 'evidence.json'
        Write-SelfTestEvidence -Path $path
        $null = Test-DisposableMediaEvidence -Path $path

        Write-SelfTestEvidence -Path $path -Result 'fail'
        $rejected = $false
        try { $null = Test-DisposableMediaEvidence -Path $path } catch { $rejected = $true }
        Assert-True $rejected 'Semantic negative self-test was not rejected.'

        Write-SelfTestEvidence -Path $path -EvidenceBackingDiskNumbers 7
        $rejectedTargetEvidence = $false
        try { $null = Test-DisposableMediaEvidence -Path $path } catch { $rejectedTargetEvidence = $true }
        Assert-True $rejectedTargetEvidence 'Evidence-on-target negative self-test was not rejected.'

        Write-Host 'PASS  Disposable-media evidence verifier self-test passed.'
        exit 0
    }
    finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    throw 'Provide -EvidencePath <path-to-evidence.json> or use -SelfTest.'
}

$result = Test-DisposableMediaEvidence -Path $EvidencePath
Write-Host 'PASS  Disposable-media hardware evidence is internally consistent and hash-bound.'
Write-Host ("PASS  PhysicalDrive{0} stable-id={1} source-sha256={2} bytes={3} sector={4} completed={5}" -f $result.DiskNumber, $result.StableId, $result.SourceSha256, $result.BytesWritten, $result.LogicalSectorSizeBytes, $result.CompletedUtc)
