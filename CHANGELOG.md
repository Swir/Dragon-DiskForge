# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- read-only IMG/RAW partition inspection with MBR, bounded EBR and GPT parsing
- read-only IMA/FLP media geometry with FAT-style BPB validation
- read-only BIN/CUE, MDF/MDS CD, NRG v1/v2 and CCD/IMG/SUB optical track-layout providers
- read-only VMware hosted sparse VMDK v1, QCOW/QCOW2 and DMG/UDIF metadata providers
- read-only WIM/ESD and FFU container metadata providers
- hardened provider registration/descriptor contract and deterministic equal-priority ordering
- provider-agnostic cross-provider partition intelligence through `PartitionTable`
- stable structural partition findings for duplicate indexes, zero-length entries, LBA overflow, byte-geometry mismatches, physical image-bound violations and overlapping ranges
- bounded `FileSystemRecognitionService` and filesystem evidence models
- FAT12/FAT16/FAT32, exFAT, supported NTFS, ext2/ext3/ext4, ISO9660/Joliet and UDF VRS recognition
- `BootInstallerIntelligenceService`, boot catalog evidence models and installer evidence models
- bounded El Torito boot-record/catalog parsing with validation checksum, section and load-range validation
- truthful BIOS/UEFI boot reporting from El Torito platform entries
- bounded provider-backed Direct Browse evidence traversal for installer intelligence
- Windows installation-media recognition from setup + boot WIM + install payload evidence
- Linux casper, Debian-style and Anaconda-style installer/live-media recognition
- EFI fallback filename architecture hints for x86, x86_64, ARM, ARM64 and RISC-V 64
- dedicated partition, filesystem and boot/installer intelligence CI gates

### Changed
- development version is **0.5.0-alpha.1**
- project progress advances to **50% toward 1.0** after the validated boot/installer intelligence foundation
- **0.5 Partitions + File Systems + Image Intelligence** advances to approximately **55%**
- CI now gates provider registry, partition intelligence, filesystem recognition and boot/installer intelligence before provider/native/build regression
- installer evidence traversal stores only real files, ignores reparse-point evidence, rejects traversal-style virtual paths and enforces directory/entry/depth limits

### Safety
- all provider and intelligence paths remain read-only-first
- partition intelligence does not mount images, write bytes or repair partition tables
- filesystem recognition scans only bounded physical file ranges and refuses structurally invalid partition layouts
- sparse/compressed virtual/container formats do not gain fake guest-filesystem access without a guest-sector reader
- bootability is reported only from a structurally valid El Torito catalog; installer file markers alone never fabricate boot support
- installer evidence requires a truthful Direct Browse provider and never executes, extracts, mounts or modifies content
- El Torito catalog and boot-image ranges are bounded against the physical image
- existing cancellation, failure isolation and truthful capability behavior remain intact

### Verified
- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy + full regression/build/artifact
- PR #18 / run #172 — BIN/CUE + full regression/build/artifact
- PR #19 / run #182 — MDF/MDS + full regression/build/artifact
- PR #20 / run #185 — NRG + full regression/build/artifact
- PR #21 / run #198 — CCD/IMG/SUB + full regression/build/artifact
- PR #22 / run #205 — VMDK + full regression/build/artifact
- PR #23 / run #212 — QCOW/QCOW2 + full regression/build/artifact
- PR #24 / run #219 — DMG/UDIF + full regression/build/artifact
- PR #25 / run #222 — WIM/ESD + full regression/build/artifact
- PR #26 / run #226 — FFU + full regression/build/artifact
- PR #27 / run #228 — provider-contract hardening + full regression/build/artifact
- PR #28 / run #232 — docs-synchronized cross-provider partition intelligence + full regression/build/artifact
- PR #29 / run #235 — docs-synchronized bounded filesystem recognition + full regression/build/artifact
- PR #30 / run #240 — boot/installer intelligence code head + new gate + complete provider/Explorer/native Windows/Release x64/artifact regression before documentation synchronization

### Planned
- richer UDF/FAT/NTFS reader depth
- cross-source architecture and label/UUID/GUID aggregation beyond current bounded hints
- corruption/health warnings only where backed by proven metadata checks
- guest-sector readers before filesystem intelligence inside sparse/compressed virtual disks
- public beta `0.5.0-beta.1` only after the agreed 0.5 scope and beta gates are proven

## [0.3.0] - 2026-09-15

### Added
- Dragon Explorer for mounted ISO/VHD/VHDX
- search, Preview, safe Copy out and drag-out
- Recent Images/Favorites, mounted history and multi-image workspace
- managed ISO9660/Joliet direct browsing
- Windows x64 artifact publishing

## [0.2.0] - 2026-09-14

### Added
- native Windows ISO/VHD/VHDX mount/unmount
- read-only-first state detection/progress/cancellation
- Windows integration tests

## [0.1.0] - 2026-09-14

### Added
- WinUI 3 / .NET 10 shell and Core split
- image-format catalogue/signature detection
- SHA-256 verification
- Dragon visual system and icon
- Core smoke tests and Windows x64 CI
