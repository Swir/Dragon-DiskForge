# Physical Media Safety Boundary

Dragon DiskForge 0.7 starts with **non-destructive physical-media discovery and planning only**. This document defines the proven boundary before any physical-device write implementation is allowed to exist.

## Current scope

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

Not implemented:

- opening a physical disk for write access
- sector writes or destructive mutation
- partition-table mutation
- a user-visible Write/Burn-to-disk action
- automatic override of any refusal
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

When a serial number is unavailable, Dragon DiskForge may still build a fallback fingerprint for diagnostics, but `HasStableIdentity` remains false. Future destructive operations must fail closed on that state.

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

The token is only one gate. It is **not permission to implement or expose writes before the remaining 0.7 safety work passes**.

## Validation

PR #45 implementation run #310 validates:

- refusal and confirmation behavior with dedicated Core smoke tests
- missing/ambiguous identity failure paths
- capacity and physical-source refusal paths
- real read-only physical-disk enumeration on the elevated Windows runner
- real detection of the Windows system disk by volume extent
- refusal of every detected system disk by the write-plan safety service
- the complete existing provider, intelligence, Explorer, native mount, Release x64 build and clean-package regression path

## Remaining 0.7 gates

Before any physical write path can be considered for exposure, 0.7 still requires:

1. a bounded progress/cancellation and fail-safe design for a physical write operation, including explicit handling of cancellation after mutation has begun; and
2. a separately validated physical write path exercised only under dedicated safety gates and disposable test-media conditions.

Until those are proven, Dragon DiskForge intentionally exposes **no physical-device write capability**.
