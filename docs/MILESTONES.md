# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines product direction. Pull requests and CI runs prove execution.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅

Windows x64 CI, Core smoke tests, Dragon UI, SHA-256 verification, responsive/accessibility resources and application icon are proven.

## 0.2 Native Mount + Unmount — COMPLETE ✅

Native Windows ISO/VHD/VHDX read-only-first Mount/Unmount, state detection and disposable integration tests are proven. Normal-user UAC remains a manual QA gate.

## 0.3 Dragon Explorer — COMPLETE ✅

Released version: **0.3.0**.

- Mounted-volume Explorer ✅
- Preview + Image Library ✅
- Mounted history + multi-image workspace ✅
- Safe Copy-only drag-out ✅
- Provider-backed direct ISO browsing ✅

## 0.4 Extended Image Providers — COMPLETE ✅

Required 0.4 engineering scope: **100% complete**.

Completed provider/foundation slices: registry foundation, IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD, FFU and provider-contract hardening.

Final 0.4 checkpoints:
- PR #26 / run #226 — final FFU docs-synchronized head
- PR #27 / run #228 — provider-contract hardening + full provider/native/build/artifact regression

Closing 0.4 completes the internal engineering contract only. It does not declare a stable public plugin API.

## 0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧

Development version: **0.5.0-alpha.1**.

Current milestone completion is approximately **30%**.

### Completed execution slices

1. **Cross-provider partition intelligence** ✅
2. **Bounded filesystem-recognition foundation** ✅

### Cross-provider partition intelligence ✅

- capability-driven `PartitionIntelligenceService`
- provider resolution through `ProviderRegistry` + `PartitionTable`
- stable finding codes/severities
- duplicate index, zero length, LBA overflow, byte-geometry, bounds and overlap checks
- capability-driven fake providers
- read-only analysis only
- PR #28 / run #232 docs-synchronized full regression/build/artifact ✅

### Bounded filesystem-recognition foundation ✅

- provider-integrated `FileSystemRecognitionService`
- physical whole-file scanning only where byte mapping is truthful
- partition-scoped scanning after structural validation of provider-reported physical ranges
- FAT12/FAT16/FAT32 recognition and basic metadata
- exFAT boot metadata
- supported NTFS boot metadata
- ext2/ext3/ext4 superblock recognition and metadata
- ISO9660/Joliet descriptors and volume label
- UDF VRS recognition
- generated sparse test fixtures rather than committed binary images
- false-positive, invalid-layout and cancellation coverage
- no sparse/compressed virtual guest-sector translation
- no mount, traversal, extraction, repair or writes
- PR #29 / run #234 code head passed the new filesystem gate plus all existing provider, Explorer, native Windows, Release x64 and artifact checks before documentation synchronization

### Remaining execution slices

- richer UDF/FAT/NTFS metadata and reader depth
- bootability + BIOS/UEFI intelligence
- Windows/Linux installer recognition
- architecture + cross-source label/UUID/GUID aggregation
- health/corruption warnings backed by proven metadata checks
- virtual guest-sector readers before inspecting filesystems inside sparse/compressed virtual disks

A capability becomes user-visible only after its real backing path and tests exist.
