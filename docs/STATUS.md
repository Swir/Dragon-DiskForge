# Dragon DiskForge — Current Status

## Completed milestones

- **0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**
- **0.2 Native Mount + Unmount — COMPLETE ✅**
- **0.3 Dragon Explorer — COMPLETE ✅**
- **0.4 Extended Image Providers — COMPLETE ✅**
- **0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅**
- **0.6 Create + Convert + Verify — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

The public beta suffix is intentionally not promoted until the independent `0.5.0-beta.1` release gate passes. Engineering work beyond the 0.5 beta scope may continue on `main` without weakening that gate.

## Overall project progress

**74% toward 1.0.** Milestones 0.1 through 0.6 are complete, and 6 of 7 current 0.7 Physical Media Tools deliverables are implemented and validated.

## Current roadmap position

**0.7 Physical Media Tools — IN PROGRESS 🚧 — 6/7 (~86%)**

PR #45 established query-only physical inventory and fail-closed planning. PR #46 implementation run #319 then passed the expanded execution-contract tests plus the complete existing regression suite, Windows integration, Release x64 build and clean-package verification.

### Read-only physical-disk inventory ✅
- canonical `\\.\PhysicalDriveN` discovery
- query-only handles (`dwDesiredAccess = 0`)
- capacity evidence
- bus/removable/vendor/product/revision/serial evidence where Windows reports it
- system-volume to physical-disk extent evidence
- serial-backed stable identity when sufficient identity material exists
- explicit ambiguous identity when stable identity cannot be proven

### Physical-media safety planning ✅
- source/destination plan preview without writes
- hard refusal of system-disk targets
- hard refusal of ambiguous identity and unknown capacity
- hard refusal of physical-device sources and same-device source/destination
- hard refusal when image length exceeds destination capacity
- exact destination-bound destructive confirmation token only for otherwise eligible plans
- refused plans receive no confirmation token

### Physical write execution contract ✅
- injected `IPhysicalMediaWriteSink`; no platform writer is exposed by the Core contract
- destination identity and exact confirmation are revalidated before the first write attempt
- source file length is rechecked after opening
- bounded sequential buffer with monotonic byte progress
- SHA-256 evidence covers only chunks accepted successfully by the sink and is not presented as read-back verification
- progress callbacks are observational and isolated from the destructive I/O path
- safe pre-write cancellation/refusal is distinguished from cancellation/failure after mutation may have started
- every abnormal exit after a destination write attempt reports that the destination may be modified and requires recovery/rewrite
- no generic rollback is claimed for physical media

### Remaining 0.7 scope ⬜
- separately validated Windows physical write path only after the safety contract is exercised under dedicated disposable-media conditions

No physical-device write capability is currently user-visible. See `docs/PHYSICAL-MEDIA-SAFETY.md`.

## 0.6 closing scope

### Dual verification ✅
- SHA-256 + SHA-512 in one bounded sequential pass
- exact hashed-byte count
- bounded progress and cancellation
- PR #41 implementation run #292

### Safe output transaction boundary ✅
- `SafeOutputService`
- same-directory temporary files
- `FailIfExists` / `ReplaceExisting`
- failure/cancellation cleanup and destination preservation on proven paths
- PR #42 implementation run #296 and final run #298

### RAW creation + guest-to-RAW conversion ✅
- explicit-length blank RAW creation
- bounded `IGuestByteReader` → RAW materialization
- QCOW2 → RAW and hosted-sparse VMDK → RAW for proven reader subsets
- source/destination identity checks and exact committed length
- PR #43 implementation run #300

### Transactional split/join ✅
- `SplitImagePipelineService`
- sibling-directory staging before final set publication
- generated bounded part names
- versioned `dragon-split-manifest.json`
- SHA-256 per part
- manifest geometry/path/length/hash validation before successful join publication
- cancellation/conflict/hash-mismatch coverage

### Bounded gzip transport compression ✅
- `GzipImagePipelineService`
- transactional whole-file gzip compression/decompression
- caller-required maximum decompressed byte count
- minimum envelope, magic, method and reserved-header-bit checks
- malformed/truncated/cap/cancellation coverage

### Sparse-input scope ✅
- proven QCOW2 and hosted-sparse VMDK sparse/unallocated guest mappings remain read-only
- those mappings can be materialized to flat RAW
- sparse-container writing is **not** claimed
- format-internal QCOW2/VMDK/DMG compression decoding is **not** claimed

PR #44 implementation run #304 and its final synchronized CI closed the required 0.6 engineering scope.

## Proven validation checkpoints

### 0.4
- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy
- PR #18 / run #172 — BIN/CUE
- PR #19 / run #182 — MDF/MDS
- PR #20 / run #185 — NRG
- PR #21 / run #198 — CCD/IMG/SUB
- PR #22 / run #205 — VMDK
- PR #23 / run #212 — QCOW/QCOW2
- PR #24 / run #219 — DMG/UDIF
- PR #25 / run #222 — WIM/ESD
- PR #26 / run #226 — FFU
- PR #27 / run #228 — provider-contract hardening

### 0.5
- PR #28 / run #232 — partition intelligence
- PR #29 / run #235 — filesystem recognition
- PR #30 / run #242 — boot/installer intelligence
- PR #31 / run #245 — unified identity/health
- PR #32 / run #260 — Analyze/reporting surface
- PR #33 / run #262 — deeper filesystem evidence
- PR #34 / run #266 — NTFS/architecture hardening + package foundation
- PR #35 / run #270 — version metadata + independent package verification
- PR #36 / run #274 — bounded UDF root traversal
- PR #37 / run #277 — QCOW2 guest reader
- PR #38 / run #284 — hosted-sparse VMDK guest reader
- PR #39 / run #287 — guest partition/filesystem intelligence
- PR #40 / implementation run #289 — guest GPT/EBR integrity hardening

### 0.6
- PR #41 / implementation run #292 — dual SHA-256/SHA-512 verification
- PR #42 / implementation run #296 and final run #298 — safe output transaction boundary
- PR #43 / implementation run #300 — RAW creation + guest-to-RAW conversion
- PR #44 / implementation run #304 — transactional split/join + bounded gzip transport pipelines

### 0.7
- PR #45 / implementation run #310 — query-only physical-disk inventory, system-disk evidence, fail-closed write-plan preview and destination-bound confirmation contract
- PR #46 / implementation run #319 — bounded physical-write execution contract, destination revalidation, cancellation/failure recovery semantics and full Windows regression/build/package validation

## Beta readiness

The planned first public beta remains **`0.5.0-beta.1`** and is **NOT READY YET**. Automated engineering and clean-package candidate gates are complete. Remaining blockers are independent release gates:

- promote version/package metadata to the final beta suffix only at release time
- launch and exercise the package on a clean supported Windows machine
- confirm no developer SDK/Visual Studio requirement for a normal user
- complete normal-user UAC validation
- complete real cross-process Explorer drag-out validation
- complete basic clean-machine launch/open/mount/explore/verify/analyze regression
- publish the final ZIP, SHA-256 and GitHub pre-release only after those checks pass

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Metadata parsers, guest readers and intelligence services validate offsets/ranges and reject unsupported or contradictory states rather than guessing.

The 0.7 physical inventory path requests query-only physical-disk handles. A missing stable identity, unknown capacity or system-disk target fails closed in the write-plan preview. The new execution coordinator remains platform-writer-agnostic: it validates identity/confirmation/source state, bounds sequential transfer and makes partial-mutation recovery state explicit, but it does not open `PhysicalDriveN` for write access and is not wired to a destructive UI action.

0.6 file-producing Core operations are not automatically exposed as Create/Convert UI actions. Single-file output uses the safe transaction boundary; split sets stage in a sibling directory and publish only after all parts plus integrity metadata are complete. Gzip decompression requires an explicit maximum output size.

QCOW2 and VMDK guest readers remain deliberately limited to their proven uncompressed subsets. No backing chains, encryption, unsupported compressed mappings, stream-optimized VMDK semantics or sparse-container writes are claimed.

Interactive UAC, clean-machine runtime and the real cross-process Explorer drag gesture remain manual beta QA gates.
