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
- QCOW2 allocated, explicit-zero and unallocated/no-backing guest-byte semantics with strict virtual/physical bounds
- generated QCOW2 guest-reader tests covering cross-cluster reads, OOB mappings, reserved bits, backing/encryption/dirty/compression/extended-state refusal and cancellation
- bounded `VmdkSparseGuestByteReader` for clean single-extent hosted sparse v1 `monolithicSparse` images
- VMDK active grain-directory/grain-table translation with redundant-directory selection, sparse/no-parent zero semantics and cross-grain reads
- VMDK guest-reader tests covering active-directory selection, physical OOB pointers, metadata-overhead separation, unsupported parent/split/compressed/unclean states and cancellation

### Changed
- development version remains **0.5.0-alpha.1** until the beta gate is complete
- project progress advances to **59% toward 1.0** after validating the hosted-sparse VMDK guest-byte reader slice
- **0.5 Partitions + File Systems + Image Intelligence** advances to approximately **97%**
- CI now gates both QCOW2 and VMDK guest-byte translation in addition to the complete provider/intelligence/Explorer/native/package regression path
- next guest-byte focus is a common bounded guest-source integration into partition/filesystem intelligence

### Safety
- all provider and intelligence paths remain read-only-first
- no guest-byte reader enables Direct Browse or filesystem capability by itself
- QCOW2 backing-file chains, encryption, dirty active metadata, external-data mode, non-default compression metadata, extended L2 entries and compressed-cluster descriptors fail closed
- QCOW2 table entries are validated for reserved bits, alignment and physical bounds before use
- unallocated QCOW2 clusters are treated as zeroes only because the current reader refuses backing files
- VMDK parent chains, split create types, unclean images, compression, stream-optimized markers, zeroed-grain entry overloading and unknown flags fail closed
- VMDK grain directories/tables must remain inside declared metadata overhead, while allocated grain data must remain outside it and within the physical file
- unallocated VMDK grains are treated as zeroes only because the current reader refuses parent chains
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
- PR #37 / run #277 — bounded QCOW2 standard guest-byte reader + complete regression/build/package-verification/artifact path
- PR #38 — bounded hosted-sparse VMDK guest-byte reader; final documentation-synchronized run pending

### Planned
- common bounded guest-byte source integration into partition/filesystem intelligence
- final 0.5 beta-scope hardening and clean-machine/manual QA
- promote to `0.5.0-beta.1` only when the independent beta gate is complete

## [0.3.0] - 2026-09-15
- Dragon Explorer, search/Preview/Copy out, Recent/Favorites, mounted history, multi-image workspace, safe drag-out, ISO direct browsing and Windows x64 artifacts

## [0.2.0] - 2026-09-14
- native Windows ISO/VHD/VHDX mount/unmount with read-only-first state detection/progress/cancellation and integration tests

## [0.1.0] - 2026-09-14
- WinUI 3 / .NET 10 shell, Core split, image detection, SHA-256 verification, Dragon visual system/icon and x64 CI
