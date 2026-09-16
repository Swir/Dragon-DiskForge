# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- hardened provider registry with deterministic resolution, failure isolation and truthful capabilities
- read-only providers for IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU
- provider-agnostic partition intelligence and bounded physical filesystem recognition
- bounded boot/installer intelligence, identity/health aggregation and architecture reconciliation
- Windows **Analyze** action with text/JSON reports and Save JSON
- required `by Swir` + GitHub footer
- deeper exFAT/FAT32/UDF/NTFS metadata evidence and bounded physical UDF root traversal
- generic read-only `IGuestByteReader` contract
- bounded QCOW2 standard-uncompressed and hosted-sparse VMDK guest-byte readers
- guest-relative MBR/EBR/GPT and filesystem intelligence with GPT CRC/geometry and EBR containment hardening
- clean Windows x64 package candidate, deterministic manifest, SHA-256 sidecar, icon and independent ZIP verification
- combined SHA-256 + SHA-512 verification with exact hashed-byte reporting
- reusable `SafeOutputService` with explicit `FailIfExists` / `ReplaceExisting` policies
- transactional blank RAW creation and generic guest-byte → RAW materialization
- explicit QCOW2 → RAW and hosted-sparse VMDK → RAW conversion entry points for the proven reader subsets
- `SplitImagePipelineService` for transactional split-set creation and validated join
- versioned `dragon-split-manifest.json` with SHA-256 for every split part
- bounded generated split-part naming and a 10,000-part safety ceiling
- `GzipImagePipelineService` for transactional whole-file gzip transport compression/decompression
- caller-required decompressed output ceiling
- gzip minimum-envelope, magic, compression-method and reserved-header-bit validation before output staging
- `docs/OUTPUT-TRANSACTIONS.md`, `docs/RAW-IMAGE-PIPELINES.md` and `docs/SPLIT-COMPRESSION-PIPELINES.md`
- dedicated smoke tests for dual verification, safe output, RAW pipelines and split/join + gzip pipelines

### Changed
- project progress advances to **68% toward 1.0** after the complete automated 0.6 engineering scope is validated
- **0.4 Extended Image Providers** remains **100% complete**
- **0.5 Partitions + File Systems + Image Intelligence** remains **100% automated engineering complete**
- **0.6 Create + Convert + Verify** reaches **100% automated engineering complete** after all five top-level deliverables pass
- current development metadata remains **0.5.0-alpha.1** until the independent public-beta release gate decides final `0.5.0-beta.1` promotion
- the next roadmap milestone is **0.7 Physical Media Tools**, beginning with non-destructive discovery and safety infrastructure
- file-producing progress reserves `1.0` for successful publication/commit

### Safety
- inspection/provider/intelligence paths remain read-only-first
- unsupported UI actions remain disabled rather than simulated
- source containers and guest address spaces remain read-only during RAW export
- single-file outputs publish through `SafeOutputService`
- split sets stage every part plus integrity metadata in a sibling temporary directory before final directory publication
- join validates manifest version, geometry, safe filenames, physical lengths and SHA-256 before successful final output publication
- gzip decompression requires an explicit maximum output byte count to bound expansion
- malformed/truncated gzip input fails closed on the tested paths
- split/join + gzip work does not claim sparse-container writing or unsupported format-internal compression decoding
- QCOW2 backing/encryption/dirty/external-data/compressed/extended-L2 states remain unsupported on the proven guest-reader path
- VMDK parent/split/unclean/compressed/stream-optimized states remain unsupported on the proven guest-reader path
- physical-device writes are not exposed; future 0.7 work remains behind dedicated safety design and validation

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
- PR #31 / run #245 — unified identity/health
- PR #32 / run #260 — Windows Analyze/reporting
- PR #33 / run #262 — deeper filesystem evidence
- PR #34 / run #266 — NTFS/architecture hardening + clean-package foundation
- PR #35 / run #270 — version metadata + strict package verification
- PR #36 / run #274 — bounded UDF root traversal
- PR #37 / run #277 — QCOW2 guest reader
- PR #38 / run #284 — hosted-sparse VMDK guest reader
- PR #39 / run #287 — guest partition/filesystem intelligence
- PR #40 / implementation run #289 — guest GPT/EBR integrity hardening
- PR #41 / implementation run #292 — dual SHA-256/SHA-512 verification
- PR #42 / implementation run #296 and final run #298 — safe output transaction foundation
- PR #43 / implementation run #300 — RAW creation + guest-to-RAW conversion
- PR #44 / implementation run #304 — transactional split/join + bounded gzip transport pipelines; final documentation-synchronized CI required before merge

### Planned
- complete the independent `0.5.0-beta.1` clean-machine/UAC/cross-process drag-out/final-package release gates
- begin 0.7 with read-only physical-device discovery and explicit safety/refusal contracts
- keep destructive physical operations disabled until separately designed, implemented and independently validated

## [0.3.0] - 2026-09-15
- Dragon Explorer, search/Preview/Copy out, Recent/Favorites, mounted history, multi-image workspace, safe drag-out, ISO direct browsing and Windows x64 artifacts

## [0.2.0] - 2026-09-14
- native Windows ISO/VHD/VHDX mount/unmount with read-only-first state detection/progress/cancellation and integration tests

## [0.1.0] - 2026-09-14
- WinUI 3 / .NET 10 shell, Core split, image detection, SHA-256 verification, Dragon visual system/icon and x64 CI
