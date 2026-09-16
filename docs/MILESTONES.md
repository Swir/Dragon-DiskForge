# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines product direction. Pull requests and CI runs prove execution.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅
Windows x64 CI, Core smoke tests, Dragon UI, verification foundation, responsive/accessibility resources and the application icon are proven.

## 0.2 Native Mount + Unmount — COMPLETE ✅
Native Windows ISO/VHD/VHDX read-only-first Mount/Unmount, state detection and disposable integration tests are proven. Normal-user UAC remains a manual beta gate.

## 0.3 Dragon Explorer — COMPLETE ✅
Released version: **0.3.0**.

- mounted-volume Explorer ✅
- Preview + Image Library ✅
- mounted history + multi-image workspace ✅
- safe Copy-only drag-out ✅
- provider-backed direct ISO browsing ✅

## 0.4 Extended Image Providers — COMPLETE ✅
Required engineering scope: **100% complete**.

Provider/foundation slices: registry foundation, IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD, FFU and provider-contract hardening.

Final checkpoints: PR #26 / run #226 FFU; PR #27 / run #228 provider-contract hardening.

## 0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅

Required automated engineering scope: **100% complete**.

Completed slices include cross-provider partition intelligence, bounded filesystem recognition, boot/installer intelligence, unified identity/health intelligence, Windows Analyze/reporting, deeper exFAT/FAT32/UDF/NTFS evidence, architecture reconciliation, clean-package verification, bounded UDF root traversal, QCOW2/VMDK guest readers, common guest partition/filesystem intelligence and final guest GPT/EBR integrity hardening.

Public beta publication remains a separate release decision gated by `docs/BETA-RELEASE.md`.

## 0.6 Create + Convert + Verify — COMPLETE ✅

Required engineering scope: **100% complete**.

### 1. Dual SHA-256/SHA-512 verification ✅
- combined bounded sequential hashing
- exact hashed-byte count
- compatibility SHA-256 API + dedicated SHA-512 API
- progress/cancellation coverage
- PR #41 implementation run #292 passed full Windows regression/build/package validation

### 2. Safe output transaction boundary ✅
- `SafeOutputService`
- same-directory staging
- `FailIfExists` and `ReplaceExisting`
- commit-only publication after writer success
- failure/cancellation cleanup and destination preservation on proven paths
- PR #42 implementation run #296 and final run #298 passed full Windows validation

### 3. RAW creation + guest-to-RAW conversion ✅
- explicit-length blank RAW creation
- bounded generic `IGuestByteReader` → RAW export
- QCOW2 → RAW and hosted-sparse VMDK → RAW for the proven reader subsets
- exact output length and source/destination identity checks
- PR #43 implementation run #300 passed full Windows validation

### 4. Transactional split/join ✅
- atomic split-set directory publication after all parts + manifest are staged
- bounded generated part names and 10,000-part ceiling
- SHA-256 per part
- versioned integrity manifest
- join validates version, geometry, filenames, physical lengths and hashes
- join output uses `SafeOutputService`
- cancellation/conflict/hash-mismatch coverage

### 5. Bounded sparse-input/compression handling ✅
- whole-file gzip transport compression/decompression
- caller-required decompressed-size ceiling
- base gzip envelope/header validation
- malformed/truncated/cap/cancellation coverage
- proven sparse/unallocated QCOW2/VMDK guest mappings remain read-only and can be materialized to flat RAW
- sparse-container writing and unsupported format-internal compression are explicitly not claimed
- no Create/Convert WinUI capability is enabled by Core-only work
- PR #44 implementation run #304 passed the split/join + gzip gate plus full provider/intelligence/Explorer/native Windows/Release/clean-package validation

**0.6 exit:** final documentation-synchronized PR-head CI must be green before merge; after that the milestone is closed.

## 0.7 Physical Media Tools — PLANNED ⬜

The next milestone is safety-first. Initial work must be non-destructive:

- read-only physical-disk discovery and stable identity
- system/removable/bus/capacity evidence
- refusal rules for system disks and ambiguous targets
- write-plan preview and explicit confirmation design
- separately validated write path only after safety infrastructure is proven

No physical-device write capability is currently user-visible.

## Beta release track

The first planned public beta remains **`0.5.0-beta.1`**. Automated engineering and clean-package candidate gates are complete, but clean-machine runtime, normal-user UAC, real cross-process drag-out, final beta suffix/package verification and public Release/checksum publication remain open.

A capability becomes user-visible only after its real backing path and tests exist.
