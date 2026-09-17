# Physical Media Safety Boundary

Dragon DiskForge 0.7 remains **non-destructive at the product/UI surface**. The repository now contains a hard-gated Windows `PhysicalDriveN` writer candidate and a disposable-media validation harness, but the final 0.7 hardware gate is still open. No destructive physical-media action is exposed to normal application users.

## Current proven scope

Implemented and CI-validated:

- read-only Windows physical-disk inventory
- canonical `\\.\PhysicalDriveN` device paths
- capacity, bus type and removable-media evidence
- Windows system-volume to physical-disk extent evidence
- serial-backed stable identity when Windows exposes sufficient identity material
- fail-closed handling when stable identity or capacity is unavailable
- source/destination write-plan preview
- explicit refusal of system disks, ambiguous targets, invalid source lengths, physical-device sources and images larger than the destination
- destination-bound exact confirmation contract
- Core `IPhysicalMediaWriteSink` + bounded execution/recovery contract
- destination identity + confirmation revalidation immediately before write I/O
- source-length revalidation after opening the source file
- monotonic progress and explicit pre-write/post-write-attempt cancellation/failure states
- SHA-256 evidence for chunks successfully accepted by an injected sink
- Windows read-only writer preflight that resolves source backing disks and destination sector geometry
- fail-closed rejection of UNC/unprovable source topology and source-on-target cases
- target-volume enumeration, lock and dismount before raw write access
- sector-aligned bounded `PhysicalDriveN` sequential writer candidate
- explicit device flush
- independent source-length read-back SHA-256 verification path
- hard-locked disposable-media validation harness that remains inert without explicit opt-in and exact destination/source/confirmation evidence

Still **not proven / not user-visible**:

- successful destructive execution against real dedicated disposable media
- a user-visible Write/Burn-to-disk action
- generic rollback after a partial physical-media write
- automatic override of any refusal
- claims that every storage controller exposes reliable serial/removable evidence

## Read-only inventory

`WindowsPhysicalDiskInventoryService` opens physical disks with query-only access and uses Windows storage IOCTLs to collect evidence. Inventory records retain evidence strings so later safety decisions can distinguish proven facts from unknown values.

The inventory path queries capacity, storage identity/bus/removable evidence and the Windows system-volume backing physical-disk extents. A destination without sufficient stable identity or capacity evidence cannot cross the destructive gate.

## Stable identity policy

A destination is considered to have stable identity only when the current implementation has sufficient serial-backed identity material. A SHA-256 identifier is derived from the reported identity fields for comparison and confirmation binding.

Fallback fingerprints may be useful for diagnostics, but do not promote an ambiguous destination into an eligible destructive target. Access-denied or incomplete inventory records also fail closed.

## System-disk protection

The Windows system volume is mapped to physical disks through `IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`. Every matching physical disk is treated as protected by the safety planner.

`PhysicalMediaSafetyService` refuses a destructive write plan for any system disk. A refused plan receives no confirmation token and cannot be promoted by confirmation matching. This is intentionally stronger than a warning.

## Write-plan preview

`PhysicalMediaSafetyService.PreviewImageToDiskWrite` models intent before mutation. It rejects a plan when source/destination/capacity/identity evidence is unsafe or incomplete, including physical-device sources, same-device source/destination, system disks, ambiguous identity, unknown capacity and oversized source images.

The plan may also report non-fatal evidence/warnings such as trailing destination capacity, removable-media semantics or unknown bus details.

## Confirmation contract

An otherwise eligible plan still requires exact explicit confirmation shaped like:

`ERASE PHYSICALDRIVE7 AABBCCDDEEFF`

The token binds the physical disk number and stable identity suffix. Matching is exact and case-sensitive. A token from another destination, a modified token or any token attached to a refused plan is rejected.

Confirmation is only one gate. It never bypasses destination/source revalidation.

## Core execution contract

`PhysicalMediaWriteExecutionService` coordinates bounded transfer through an injected `IPhysicalMediaWriteSink`. Before the first destination write attempt it requires the allowed plan, exact confirmation, matching current destination identity/path, a successfully revalidated safety plan and the unchanged source length.

Cancellation before a write attempt reports a safe pre-write cancellation. Once a destination write is attempted, cancellation or failure is treated as potentially destructive and requires recovery/rewrite. The service does not claim generic rollback that physical media cannot reliably provide.

`BytesWritten` and `WrittenSha256Hex` describe chunks accepted by the sink; they are operation evidence, **not device read-back verification**.

## Windows writer candidate

PR #47 adds a Windows-specific candidate behind the existing Core contract and an additional read-only preflight boundary.

Before requesting a destructive physical-disk handle, the preflight:

1. re-enumerates the requested physical destination;
2. revalidates stable identity, capacity and system-disk state;
3. resolves logical-sector geometry;
4. validates current source length and sector alignment;
5. maps the local source file's volume to its backing physical-disk extents;
6. rejects source-on-target and any source topology that cannot be proven safely;
7. enumerates all target volumes so they can be locked/dismounted before mutation.

The writer candidate then requires target-volume locks/dismounts, opens the exact planned `PhysicalDriveN`, rechecks final destination evidence, performs sequential sector-aligned bounded writes, flushes the device and releases locks in `finally` paths.

When requested by the validation path, independent read-back hashing reopens the destination read-only and computes SHA-256 over exactly the source-length region. A matching hash is meaningful verification evidence only for an actual completed real-media execution.

## Disposable-media harness

The harness is deliberately hard to invoke accidentally. It requires all relevant explicit inputs, including:

- destructive opt-in
- exact `PhysicalDriveN`
- exact stable destination identity
- source image path
- destination-bound confirmation token
- extra explicit confirmation when fixed media is involved

Normal CI runs the harness **without destructive opt-in**, proving that it stays locked. CI must never discover or select a physical target automatically.

## Validation evidence

PR #45 / run #310 proves query-only inventory, real Windows system-disk evidence, refusal behavior and destination-bound confirmation.

PR #46 / run #319 proves the platform-independent execution contract with injected non-device sinks, including identity/confirmation revalidation, cancellation/failure states, progress isolation and recovery-required semantics.

PR #47 / full Windows run #338 proves compilation/regression/package compatibility of the Windows writer candidate and its read-only preflight tests. Disposable Media Guard #10 proves that the harness compiles/runs while remaining locked without destructive opt-in.

Those automated runs do **not** prove a successful write to real physical media.

## Remaining 0.7 gate

The final 0.7 deliverable requires an intentionally supervised run against dedicated disposable test media. That test must record and verify:

- exact destination identity and device number
- source backing-device evidence
- logical-sector alignment
- successful target-volume lock/dismount behavior
- bounded write completion
- device flush result
- independent read-back SHA-256 result
- recovery expectations for any interrupted or failed attempt

Only after that real-media evidence succeeds may 0.7 be considered complete or a user-facing physical write workflow be designed for exposure.

Until then, Dragon DiskForge intentionally exposes **no physical-device write capability in the product UI**.
