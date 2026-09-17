# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a WinUI 3 / .NET 10 desktop application for inspecting, mounting, exploring, verifying and analyzing disk images. The project uses a strict truthful-capability rule: unsupported actions stay disabled until a real engine path exists and is tested.

## Current development version — 0.5.0-alpha.1

The public beta suffix is intentionally not promoted until the independent release gate in [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md) passes. Engineering work beyond the 0.5 beta scope continues on `main` without weakening that gate.

## Project progress — 74% toward 1.0

`███████████████░░░░░ 74%`

**Overall completion:** **74%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **100%** ✅
- `0.5 Partitions + File Systems + Image Intelligence` — **100%** ✅
- `0.6 Create + Convert + Verify` — **100%** ✅
- `0.7 Physical Media Tools` — **~86% (6/7)** 🚧
- `0.8 Windows Integration + Power Tools` — planned
- `0.9 Quality, Security + Beta Hardening` — planned
- `1.0 Production Release` — planned

> Progress changes only after meaningful implementation and validation checkpoints. CI count, documentation-only edits and placeholders do not increase completion.

## Proven product foundation

- native Windows ISO/VHD/VHDX read-only-first Mount + Unmount
- mounted-volume Dragon Explorer with navigation, search, Preview and safe Copy out
- Recent Images, Favorites, mount history and multi-image workspace
- safe Copy-only drag-out to Windows Explorer/Desktop
- managed ISO9660/Joliet direct browsing without mounting
- hardened provider registry with truthful capabilities and deterministic resolution
- IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU provider coverage
- bounded partition, filesystem, boot/install and image-intelligence services
- bounded QCOW2 and hosted-sparse VMDK guest-byte readers for their explicitly supported uncompressed subsets
- guest-relative MBR/EBR/GPT and filesystem intelligence over proven guest-byte mappings
- user-facing **Analyze** action with text/JSON reporting and Save JSON
- required **by Swir** + GitHub footer in the Windows UI
- SHA-256 + SHA-512 verification in one bounded sequential pass
- checksum-verified clean Windows x64 package-candidate pipeline
- query-only Windows physical-disk inventory with capacity/bus/removable/system-disk evidence
- fail-closed physical-media write-plan preview and destination-bound confirmation contract
- bounded physical-write execution contract with destination revalidation and explicit recovery semantics; no Windows physical writer is exposed

## 0.7 Physical Media Tools — IN PROGRESS 🚧

The safety-first engineering scope is now **6/7 roadmap deliverables (~86%)**.

### Read-only physical disk inventory ✅

- canonical `\\.\PhysicalDriveN` discovery on Windows
- handles are opened query-only (`dwDesiredAccess = 0`)
- capacity from `IOCTL_DISK_GET_LENGTH_INFO`
- vendor/product/revision/serial, bus and removable evidence from `IOCTL_STORAGE_QUERY_PROPERTY`
- Windows system volume mapped to backing physical disk extents
- serial-backed stable identity when Windows exposes sufficient identity material
- ambiguous or access-denied identity remains explicitly untrusted

### Fail-closed physical-media planning ✅

- `PhysicalMediaSafetyService` previews image-to-disk intent without writing
- system disks are refused, not merely warned
- missing stable identity or unknown destination capacity is refused
- physical-device sources and same-device source/destination are refused
- source images larger than the destination are refused
- otherwise eligible plans still require an exact destination-bound confirmation token
- refused plans never receive a confirmation token

### Bounded execution + recovery semantics ✅

- `PhysicalMediaWriteExecutionService` operates only through an injected `IPhysicalMediaWriteSink`
- current destination identity and exact confirmation are revalidated before the first write attempt
- source length is rechecked after opening
- sequential transfer is bounded to a 4 KiB–8 MiB buffer range with a 1 MiB default
- progress is monotonic and observer failures are isolated from destructive I/O
- cancellation before any write is safe and distinct from cancellation after modification may have begun
- after any destination write attempt, abnormal termination is marked as possibly modified and requiring recovery/rewrite
- successful accepted chunks carry SHA-256 operation evidence without claiming device read-back verification
- no generic rollback is claimed for physical media

PR #46 implementation run #319 passed the expanded physical-media safety gate, all existing provider/intelligence/Explorer/native mount tests, the real query-only physical inventory/system-disk checks, the x64 Release build and clean-package verification.

The remaining 0.7 deliverable is deliberately hardware-gated: a Windows physical-device writer must be separately validated on dedicated **disposable test media** before it can be considered for exposure. **No physical-device write capability is user-visible today.**

See [`docs/PHYSICAL-MEDIA-SAFETY.md`](docs/PHYSICAL-MEDIA-SAFETY.md).

## 0.6 Create + Convert + Verify — COMPLETE ✅

All five top-level 0.6 engineering deliverables are implemented and validated.

### Verification ✅

- `ImageVerificationInfo` reports SHA-256, SHA-512 and exact hashed-byte count
- combined verification computes both digests in one bounded sequential pass
- existing SHA-256 callers remain compatible
- bounded progress and cancellation
- PR #41 implementation run #292 passed the complete Windows regression/build/package path

### Transactional output boundary ✅

- reusable `SafeOutputService`
- same-directory temporary output
- explicit `FailIfExists` and `ReplaceExisting` policies
- completed output is flushed before final publication
- cancellation/writer failure preserve existing committed destinations on the proven paths
- PR #42 implementation run #296 and synchronized run #298 passed full Windows CI

### RAW creation + conversion foundation ✅

- explicit-length blank RAW creation
- bounded generic `IGuestByteReader` → RAW materialization
- explicit QCOW2 → RAW and hosted-sparse VMDK → RAW entry points for the already-proven reader subsets
- exact guest-visible length preservation
- source/destination identity rejection
- progress reaches `1.0` only after commit
- PR #43 implementation run #300 passed full Windows regression/build/package verification

### Transactional split/join ✅

- `SplitImagePipelineService` publishes a split set only after all parts and its manifest are staged
- split sets use generated part names plus a versioned `dragon-split-manifest.json`
- every part has SHA-256 integrity metadata
- join validates manifest geometry, safe filenames, physical lengths and SHA-256 before final publication
- split destination conflicts fail closed
- split cancellation does not intentionally publish a partial set
- join writes through `SafeOutputService`, preserving an existing destination when validation fails

### Bounded gzip transport compression ✅

- `GzipImagePipelineService` provides whole-file gzip compression/decompression
- decompression requires an explicit caller-provided maximum output size
- truncated/invalid base headers fail before output staging
- source/destination identity is rejected
- cancellation and failed decompression use the safe output transaction boundary
- this is a whole-file transport wrapper, not a claim of format-internal compressed-cluster support

### Sparse-input scope ✅

The proven QCOW2 and hosted-sparse VMDK readers already translate their supported sparse/unallocated guest mappings read-only. `RawImagePipelineService` can materialize those proven mappings to flat RAW. Dragon DiskForge does **not** claim sparse QCOW2/VMDK container writing, QCOW2 compressed-cluster decoding, VMDK stream-optimized decoding or DMG `blkx` decompression.

PR #44 implementation run #304 passed the new split/join + gzip gate plus the complete provider/intelligence/Explorer/native Windows/Release/clean-package verification path.

See [`docs/OUTPUT-TRANSACTIONS.md`](docs/OUTPUT-TRANSACTIONS.md), [`docs/RAW-IMAGE-PIPELINES.md`](docs/RAW-IMAGE-PIPELINES.md) and [`docs/SPLIT-COMPRESSION-PIPELINES.md`](docs/SPLIT-COMPRESSION-PIPELINES.md).

## 0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅

The automated 0.5 engineering scope is complete. Proven areas include cross-provider partition intelligence, physical and guest filesystem recognition, boot/installer intelligence, identity/health aggregation, Windows Analyze/reporting, deeper exFAT/FAT32/UDF/NTFS evidence, bounded UDF root traversal, QCOW2/VMDK guest readers and final guest GPT/EBR integrity hardening.

Public beta publication remains a separate release decision gated by [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

## Beta readiness

The planned first public beta remains **`0.5.0-beta.1`** and is **not ready yet**. Automated engineering and clean-package candidate gates are green, but these manual/release gates remain:

- final beta suffix/package promotion
- clean supported Windows launch and basic open/mount/explore/verify/analyze regression
- confirm a normal user does not need Visual Studio/developer SDKs
- normal-user UAC validation
- real cross-process Explorer drag-out validation
- final public ZIP + SHA-256 + GitHub pre-release publication

No empty, symbolic or CI-only beta will be published.

## Tech

- C# / .NET 10 / WinUI 3 / Windows App SDK
- x64 + ARM64 project targets
- shared Core engine separated from WinUI
- isolated Windows native-storage layer
- generated smoke/integration fixtures rather than committed large images

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

CI validates Core, dual hashing, safe output transactions, RAW creation/export, split/join + gzip pipelines, physical-media planning/execution safety, provider invariants, physical and guest partition/filesystem intelligence, UDF/NTFS depth, boot/install intelligence, reporting, QCOW2/VMDK guest-byte translation, all proven providers, Explorer safety, direct ISO integration, query-only physical-disk inventory, native ISO/VHD/VHDX integration, the full Windows x64 Release build and the independently verified clean ZIP package candidate.

## Safety design

Inspection and verification are read-only-first. Native mounts default to read-only. Parsers validate offsets/lengths and reject contradictory or unsupported states instead of guessing.

0.6 file-producing Core APIs remain separate from user-visible Create/Convert UX. `SafeOutputService` publishes only completed single-file outputs. Split sets are staged in a sibling directory and published only after all parts plus their integrity manifest are complete. Gzip decompression requires an explicit output-size ceiling.

Physical-device writes are not part of the current product surface. The 0.7 foundation proves query-only inventory, system-disk identification, ambiguous-target refusal, capacity/source checks, destination-bound confirmation and bounded fail-safe execution semantics through an injected sink. A Windows physical-device writer still does not exist and cannot be inferred from the Core execution contract.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after its real backing path exists and is testable.

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md), with execution state in [`docs/STATUS.md`](docs/STATUS.md), [`docs/MILESTONES.md`](docs/MILESTONES.md) and [`CHANGELOG.md`](CHANGELOG.md).
