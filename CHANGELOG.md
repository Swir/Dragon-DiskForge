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
- FAT12/FAT16/FAT32 BPB/cluster-count recognition with label/serial and allocation geometry
- exFAT bounded boot metadata and serial/geometry recognition
- supported NTFS bounded boot metadata and serial/cluster recognition
- ext2/ext3/ext4 superblock recognition with feature-based variant, label, UUID and block size
- ISO9660/Joliet descriptor recognition with volume label
- UDF Volume Recognition Sequence detection (`BEA01`, `NSR02|NSR03`, `TEA01`)
- dedicated generated-fixture filesystem-recognition CI gate
- dedicated filesystem-recognition architecture/safety documentation

### Changed
- development version is **0.5.0-alpha.1**
- project progress advances to **47% toward 1.0** after the validated bounded filesystem-recognition foundation
- **0.5 Partitions + File Systems + Image Intelligence** advances to approximately **30%**
- provider registrations snapshot validated descriptors at creation time
- registry extension matching uses immutable normalized descriptor extensions rather than mutable provider metadata
- CI now gates provider registry, partition intelligence and filesystem recognition before the existing provider/native/build path
- architecture/testing docs are synchronized with the real 0.1–0.5 milestone history and current service boundaries

### Safety
- all provider and intelligence paths remain read-only-first
- partition intelligence does not mount images, write bytes or repair partition tables
- filesystem recognition scans only bounded physical file ranges
- provider-reported partition ranges must pass structural validation before filesystem probing
- blank/unrecognized metadata is not converted into a guessed filesystem
- sparse/compressed VMDK, QCOW/QCOW2, DMG and other virtual/container formats do not gain fake guest-filesystem access; guest-sector translation remains a future explicit capability
- no filesystem traversal, extraction, repair, mount or write path is added by recognition
- existing cancellation, failure isolation and truthful capability behavior remains intact

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
- PR #29 / run #234 — bounded filesystem-recognition code head + generated fixtures + complete provider/Explorer/native Windows/Release x64/artifact regression before documentation synchronization

### Planned
- richer UDF/FAT/NTFS reader depth
- bootability + BIOS/UEFI detection
- Windows/Linux installer recognition
- architecture and label/UUID/GUID aggregation
- corruption warnings only where backed by proven metadata checks
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
