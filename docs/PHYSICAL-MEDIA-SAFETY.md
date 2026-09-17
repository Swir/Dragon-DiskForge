# Physical Media Safety Boundary

Dragon DiskForge 0.7 remains **non-destructive at the product/platform surface**. Query-only inventory, planning and the Core write-execution safety contract are now implemented; no Windows physical-device writer or user-visible destructive action is enabled.

## Current proven scope

Implemented and CI-validated:

- read-only Windows physical-disk inventory
- canonical `\\.\PhysicalDriveN` device paths
- capacity, bus type and removable-media evidence
- Windows system-volume to physical-disk extent evidence
- serial-backed stable identity when Windows exposes a serial number
- fail-closed handling when stable identity or capacity is unavailable
- source/destination write-plan preview
- explicit refusal of system disks, ambiguous targets, invalid source lengths, physical-device sources and images larger than the destination
- destination-bound destructive confirmation token contract
- injected physical-write sink contract with bounded sequential transfer semantics
- destination identity + confirmation revalidation immediately before write I/O
- source-length revalidation after opening the source file
- monotonic progress and explicit cancellation/failure result states
- fail-safe recovery state after any destination write attempt
- SHA-256 evidence for chunks successfully accepted by the sink

Not implemented:

- opening a physical disk for write access
- a Windows `PhysicalDriveN` writer
- sector writes against real physical media
- partition-table mutation
- a user-visible Write/Burn-to-disk action
- automatic override of any refusal
- generic rollback after a partial physical-media write
- claims that every storage controller exposes a serial number or removable flag perfectly

## Read-only inventory

`WindowsPhysicalDiskInventoryService` opens physical disks with `CreateFileW` using `dwDesiredAccess = 0`. It uses Windows storage IOCTL queries only to collect evidence. The inventory path does not request write access.

The service currently queries:

- `IOCTL_DISK_GET_LENGTH_INFO` for capacity
- `IOCTL_STORAGE_QUERY_PROPERTY` for vendor/product/revision/serial, bus and removable evidence
- `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS` on the Windows system volume to identify the backing physical disk or disks

Inventory records carry explicit evidence strings so later safety decisions can distinguish proven facts from unknown values.

## Stable identity policy

A destination is considered to have stable identity only when the current implementation has serial-backed identity material. A SHA-256 identifier is derived from the reported identity fields for comparison and confirmation binding.

When a serial number is unavailable, Dragon DiskForge may still build a fallback fingerprint for diagnostics, but `HasStableIdentity` remains false. Destructive plans fail closed on that state.

An access-denied or otherwise incomplete inventory record is also ambiguous and cannot cross the destructive-operation safety gate.

## System-disk protection

The Windows system volume is mapped to physical disks through `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`. Every matching physical disk is marked `IsSystemDisk = true`.

`PhysicalMediaSafetyService` refuses a destructive write plan for any system disk. A refused plan receives no confirmation token and cannot be promoted by `ConfirmationMatches`.

This is intentionally stronger than a warning.

## Write-plan preview

`PhysicalMediaSafetyService.PreviewImageToDiskWrite` is a planning boundary, not a writer. It rejects a plan when any of the following is true:

- source image length is zero or negative
- the source is itself a `PhysicalDrive` path
- source and destination resolve to the same physical device
- destination identity is incomplete
- destination has no stable hardware identity
- destination is a system disk
- destination capacity is unknown or invalid
- source image is larger than destination capacity

The plan also reports non-fatal warnings, including a smaller source leaving trailing capacity outside the written image, removable-media semantics and an unknown bus type.

## Confirmation contract

An otherwise eligible plan still requires explicit confirmation. The current contract produces a token shaped like:

`ERASE PHYSICALDRIVE7 AABBCCDDEEFF`

The token binds both the physical disk number and a suffix of the stable identity. Matching is exact and case-sensitive. A token from another destination, a modified token or any token attached to a refused plan is rejected.

The token is only one gate. It does not bypass destination revalidation and is not sufficient to expose a physical writer.

## Write-execution contract

`PhysicalMediaWriteExecutionService` coordinates a write only through an injected `IPhysicalMediaWriteSink`. The Core service does not open a physical device itself.

Before the first destination write attempt it:

1. requires an allowed write plan;
2. requires the exact destination-bound confirmation token;
3. requires the sink to report the same disk number, canonical device path and stable identity as the planned destination;
4. re-runs the physical-media safety plan against the sink's current destination evidence;
5. requires the revalidated confirmation binding to remain unchanged;
6. opens the source read-only and requires its current length to equal the planned length.

The transfer path uses a bounded 4 KiB–8 MiB buffer range with a 1 MiB default. Progress is monotonic and observational. A throwing progress callback is isolated so UI/reporting code cannot abort destructive I/O after it has begun.

Cancellation is checked before source reads, before destination writes and before final flush. A cancellation before any write attempt reports `CancelledBeforeWrite`. A cancellation after any destination write attempt reports `CancelledAfterWriteStarted`, marks `DestinationMayBeModified`, and requires recovery/rewrite.

The same fail-safe rule applies to failures: once `WriteAsync` has been attempted, even a failure before the sink reports a completed chunk is treated as potentially destructive because an underlying device transfer may have been partial. The coordinator does not claim a generic rollback mechanism that physical media cannot reliably provide.

`BytesWritten` and `WrittenSha256Hex` cover only chunks that the sink returned as successfully accepted. They are operation evidence, **not device read-back verification**. A separately validated platform writer must add any required read-back verification policy.

## Validation

PR #45 implementation run #310 validates:

- refusal and confirmation behavior with dedicated Core smoke tests
- missing/ambiguous identity failure paths
- capacity and physical-source refusal paths
- real read-only physical-disk enumeration on the elevated Windows runner
- real detection of the Windows system disk by volume extent
- refusal of every detected system disk by the write-plan safety service

PR #46 implementation run #319 validates the execution contract with generated file data and injected non-device sinks:

- successful bounded sequential transfer, flush and SHA-256 evidence
- monotonic progress
- wrong-confirmation refusal before I/O
- destination stable-identity swap / TOCTOU refusal before I/O
- source-length drift refusal before destination I/O
- cancellation before the first write
- cancellation after a successful prefix write with explicit recovery requirement
- injected write failure after modification began
- flush failure after all chunks were accepted
- isolation of throwing progress observers
- the complete existing provider, intelligence, Explorer, native Windows, Release x64 build and clean-package regression path

## Remaining 0.7 gate

Before a physical write path can be considered for exposure, 0.7 still requires one separately validated Windows physical writer exercised under dedicated safety gates and **disposable test-media conditions**.

That future validation must prove the platform writer's actual device-open/access mode, target identity binding at execution time, aligned/bounded transfer behavior, cancellation/failure handling, flush semantics, and any claimed read-back verification on disposable media. It must not be inferred from the injected Core sink tests.

Until that gate is proven, Dragon DiskForge intentionally exposes **no physical-device write capability**.
