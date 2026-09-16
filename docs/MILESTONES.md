# Dragon DiskForge — Milestone Execution

## Current delivery work — 0.6.0-alpha.1

Implemented: image-analysis UI / JSON export, shared registry factory, shared-Core CLI, SHA-512 and hash comparison,
atomic RAW/VHD/VHDX creation, folder-to-ISO creation, standalone RAW/VHD/VHDX conversion, GZip and checksum-protected split/join.
Dedicated regression suites cover source preservation, round trips, tampering, traversal, cancellation and output cleanup.
Portable packaging includes runtime dependencies, CLI/GUI startup gates, ZIP/checksum creation and explicit release publication.
The remaining filesystem-depth, physical-media, localization, signing and manual desktop items below are **not** marked complete.
This is an alpha delivery; prior milestone percentage checkpoints are retained rather than inflated.


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

Current milestone completion is approximately **70%**.

### Completed execution slices

1. **Cross-provider partition intelligence** ✅
2. **Bounded filesystem-recognition foundation** ✅
3. **Boot + installer intelligence foundation** ✅
4. **Unified identity + health intelligence foundation** ✅

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
- FAT12/FAT16/FAT32, exFAT, supported NTFS, ext2/ext3/ext4 recognition
- ISO9660/Joliet and UDF VRS recognition
- generated sparse fixtures, false-positive and cancellation coverage
- no sparse/compressed virtual guest-sector translation
- no mount, traversal, extraction, repair or writes
- PR #29 / run #235 docs-synchronized full regression/build/artifact ✅

### Boot + installer intelligence foundation ✅

- provider-integrated `BootInstallerIntelligenceService`
- El Torito boot record and catalog discovery bounded to physical ISO sectors
- validation-entry key/checksum and catalog section validation
- bootable entry load-range validation
- explicit BIOS/UEFI/other platform evidence
- bounded Direct Browse tree traversal with cycle/depth/entry controls
- only non-reparse files can become installer evidence
- Windows setup/boot/install payload evidence
- Linux casper, Debian-style and Anaconda-style evidence
- standard EFI fallback architecture hints
- bootability remains independent from installer file markers
- no execution, extraction, mount, repair or writes
- PR #30 / run #242 docs-synchronized full regression/build/artifact ✅

### Unified identity + health intelligence foundation ✅

- provider-integrated `ImageIntelligenceService`
- composes partition, filesystem and boot/installer intelligence without bypassing provider capabilities
- aggregates partition names, filesystem labels/identifiers, WIM/ESD GUIDs and FFU PlatformIDs
- carries bounded architecture hints from proven boot/installer evidence
- maps structural partition findings into one shared health result
- exFAT dirty/media-failure state checks
- ext clean/error state checks
- bounded NTFS and FAT32 primary/backup boot-metadata consistency checks
- health reads bounded to already recognized filesystem regions and the physical file
- metadata-only sparse/compressed/container formats remain outside guest filesystem probing
- generated fixture coverage for exFAT, NTFS, ext, WIM, FFU, mapping refusal and cancellation
- no repair, mount, extraction or writes
- PR #31 / run #245 code/test head passed the dedicated gate plus full provider/native/build/artifact regression before docs synchronization

### Remaining execution slices

- richer UDF/FAT/exFAT/NTFS metadata and reader depth
- additional evidence-backed health findings without broad “healthy” claims
- stronger cross-source architecture reconciliation where multiple proven sources exist
- virtual guest-sector readers before inspecting filesystems inside sparse/compressed virtual disks

A capability becomes user-visible only after its real backing path and tests exist.
