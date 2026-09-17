# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines product direction. Pull requests and CI runs prove execution. A milestone remains open until its real validation gate is satisfied; passing host-side tests does not replace hardware/manual evidence.

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

Completed slices include cross-provider partition intelligence, bounded filesystem recognition, boot/installer intelligence, unified identity/health intelligence, Windows Analyze/reporting, deeper exFAT/FAT32/UDF/NTFS evidence, architecture reconciliation, bounded UDF traversal, QCOW2/VMDK guest readers, common guest partition/filesystem intelligence and guest GPT/EBR integrity hardening.

Public beta publication remains a separate release decision gated by `docs/BETA-RELEASE.md`.

## 0.6 Create + Convert + Verify — COMPLETE ✅

Required engineering scope: **100% complete**.

- dual SHA-256/SHA-512 bounded verification ✅
- safe output transaction boundary ✅
- blank RAW creation + proven guest-to-RAW materialization ✅
- transactional split/join with versioned SHA-256 manifest ✅
- bounded whole-file gzip transport compression/decompression ✅

Sparse-container writing and unsupported format-internal compression decoding are not claimed.

## 0.7 Physical Media Tools — IN PROGRESS 🚧

Current required engineering scope: **6/7 (~86%)**.

### 1. Read-only physical-disk inventory ✅
- canonical Windows `\\.\PhysicalDriveN` enumeration
- query-only inventory handles
- serial-backed stable identity where Windows exposes sufficient evidence

### 2. Physical-device evidence ✅
- capacity, bus/removable/vendor/product/revision/serial evidence
- Windows system volume mapped to backing physical-disk extents

### 3. Hard refusal policy ✅
- system disks, ambiguous identity and unknown capacity fail closed
- refused plans receive no destructive confirmation token

### 4. Write-plan preview ✅
- source/destination intent is modeled before mutation
- physical-device sources, same-device source/destination and oversized images are refused

### 5. Destination-bound confirmation contract ✅
- exact token binds physical disk number and stable identity material
- another disk's token cannot confirm a plan

### 6. Bounded execution + hard-gated Windows writer candidate ✅
- Core `PhysicalMediaWriteExecutionService` revalidates identity, confirmation and source state
- Windows preflight resolves source backing disks and logical-sector geometry read-only
- unprovable source topology and source-on-target fail closed
- target volumes must be enumerated, locked and dismounted before write access
- raw writes are sequential, bounded and sector aligned
- final device flush is explicit
- independent read-back SHA-256 is available for the source-length region
- disposable-media harness requires explicit destructive opt-in, exact destination identity/source/confirmation and an extra fixed-media gate
- no destructive UI action is exposed
- PR #47 / full Windows run #338 + Disposable Media Guard #10 passed

### 7. Dedicated disposable-media validation ⬜
- exercise the real Windows writer on dedicated disposable hardware
- prove target binding, aligned transfer, flush and claimed read-back behavior on real media
- record the exact hardware/test evidence before considering user-facing exposure

**0.7 remains open.** CI cannot substitute for the final real-media destructive test.

See `docs/PHYSICAL-MEDIA-SAFETY.md`.

## 0.8 Windows Integration + Power Tools — IN PROGRESS 🚧

Current required engineering scope: **2/4 (50%)**. Safe read-only work advances here while 0.7 waits on hardware evidence.

### 1. Windows file associations + context menu ⬜
Not yet implemented.

### 2. Shared-Core CLI ✅
- new `DragonDiskForge.Cli` project
- `analyze`, `verify` and `formats` commands
- same canonical `ProviderRegistryFactory` as the desktop application
- read-only automation surface; no create/convert/physical mutation commands

### 3. PowerShell-friendly output ✅
- deterministic text/JSON stdout
- stderr-only errors/diagnostics
- stable automation exit codes
- expected SHA-256/SHA-512 matching with a dedicated mismatch exit code
- CLI smoke tests in the full Windows CI
- self-contained x64 `cli/dragon-diskforge.exe` in the clean package
- manifest SHA-256 for the CLI and runtime launch/provider verification after ZIP extraction
- PR #48 implementation run #343 + Disposable Media Guard #15 passed

### 4. Session/settings/diagnostic portability ⬜
- session restore
- settings import/export
- diagnostic export/support bundle

See `docs/CLI.md`.

## 0.9 Quality, Security + Beta Hardening — PLANNED

Clean-machine runtime, normal-user UAC, real cross-process drag-out, accessibility, performance and security/release-hardening work remains planned.

## 1.0 Production Release — PLANNED

Production release requires all final capability, package, documentation, checksum, support-matrix and post-release verification gates.

## Beta release track

The first planned public beta remains **`0.5.0-beta.1`**. Automated engineering and clean-package candidate gates are green, but clean-machine runtime, normal-user UAC, real cross-process drag-out, final beta suffix/package verification and public Release/checksum publication remain open.

A capability becomes user-visible only after its real backing path and tests exist.
