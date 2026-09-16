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
- bounded filesystem recognition for FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS
- bounded El Torito boot/installer intelligence and architecture evidence
- unified identity/health intelligence with source provenance
- WinUI **Analyze** action backed by text/JSON `ImageReportService` and Save JSON
- required `by Swir` + GitHub navigation footer
- deeper exFAT/FAT32/UDF evidence and bounded NTFS `$MFT`/`$MFTMirr` hardening
- cross-source architecture reconciliation without guessed conflict resolution
- clean Windows x64 ZIP candidate, manifest, SHA-256 sidecar and independent package verification
- bounded non-recursive UDF Type 1 root-directory traversal
- generic read-only `IGuestByteReader` engine contract
- bounded `Qcow2GuestByteReader` for QCOW2 v2/v3 standard uncompressed active L1/L2 mappings
- bounded `VmdkSparseGuestByteReader` for clean single-extent hosted sparse v1 `monolithicSparse` images
- bounded `GuestPartitionTableReader` for guest-visible MBR/EBR/GPT metadata over proven `IGuestByteReader` sources
- bounded `GuestFileSystemRecognitionService` for guest FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS evidence
- dedicated guest-relative filesystem models and separate guest sections in text/JSON reports
- final guest GPT/EBR integrity hardening: GPT primary-header CRC32, partition-entry-array CRC32, GPT geometry/metadata placement validation, MBR/EBR status validation and extended-container containment
- generated final-hardening fixtures covering valid GPT, corrupt GPT header/table checksums, invalid MBR status, escaping logical partitions and escaping EBR links

### Changed
- development version remains **0.5.0-alpha.1** until the independent public-beta gate is complete
- project progress advances to **61% toward 1.0** after validating the final 0.5 engineering hardening slice
- **0.5 Partitions + File Systems + Image Intelligence** reaches **100% automated engineering completion**
- next engineering milestone becomes **0.6 Create + Convert + Verify** after PR #40 final docs-synchronized CI/merge
- beta publication remains blocked by final suffix/package promotion and clean-machine/UAC/cross-process drag-out/manual regression gates

### Safety
- all provider and intelligence paths remain read-only-first
- guest-byte readers do not enable Direct Browse, extraction, Mount or write capabilities
- common guest analysis consumes bytes only through proven bounded `IGuestByteReader` implementations
- guest partition/filesystem offsets are modeled separately from physical container offsets
- guest GPT metadata is not trusted until header and partition-entry checksums validate
- guest EBR links and logical partitions cannot escape the declared extended-partition container
- QCOW2 unsupported backing/encryption/dirty/external-data/compressed/extended-L2 states fail closed
- VMDK unsupported parent/split/unclean/compressed/stream-optimized states fail closed
- existing filesystem/UDF/NTFS bounds, cancellation, failure-isolation and truthful-capability rules remain intact

### Verified
- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy
- PR #18 / run #172 — BIN/CUE
- PR #19 / run #182 — MDF/MDS
- PR #20 / run #185 — NRG
- PR #21 / run #198 — CCD/IMG/SUB
- PR #22 / run #205 — VMDK metadata
- PR #23 / run #212 — QCOW/QCOW2 metadata
- PR #24 / run #219 — DMG/UDIF
- PR #25 / run #222 — WIM/ESD
- PR #26 / run #226 — FFU
- PR #27 / run #228 — provider-contract hardening
- PR #28 / run #232 — partition intelligence
- PR #29 / run #235 — filesystem recognition
- PR #30 / run #242 — boot/installer intelligence
- PR #31 / run #245 — unified identity/health implementation
- PR #32 / run #260 — Windows Analyze/report surface
- PR #33 / run #262 — deeper filesystem evidence
- PR #34 / run #266 — NTFS/architecture hardening + clean-package foundation
- PR #35 / run #270 — version metadata + strict package verification
- PR #36 / run #274 — bounded UDF root traversal
- PR #37 / run #277 — bounded QCOW2 standard guest-byte reader
- PR #38 / run #284 — bounded hosted-sparse VMDK guest-byte reader
- PR #39 / run #287 — common guest partition/filesystem intelligence
- PR #40 / implementation run #289 — final guest GPT/EBR integrity hardening + complete regression/build/package-verification/artifact path

### Planned
- finish independent `0.5.0-beta.1` manual/package release gates without weakening the completed 0.5 engineering scope
- begin 0.6 Create + Convert + Verify after PR #40 final CI/merge
- promote to `0.5.0-beta.1` only when the independent beta gate is complete

## [0.3.0] - 2026-09-15
- Dragon Explorer, search/Preview/Copy out, Recent/Favorites, mounted history, multi-image workspace, safe drag-out, ISO direct browsing and Windows x64 artifacts

## [0.2.0] - 2026-09-14
- native Windows ISO/VHD/VHDX mount/unmount with read-only-first state detection/progress/cancellation and integration tests

## [0.1.0] - 2026-09-14
- WinUI 3 / .NET 10 shell, Core split, image detection, SHA-256 verification, Dragon visual system/icon and x64 CI
