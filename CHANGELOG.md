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
- `BootInstallerIntelligenceService` with bounded El Torito and installer evidence
- truthful BIOS/UEFI boot reporting from El Torito platform entries
- conservative Windows/Linux installer/live-media recognition and EFI fallback architecture hints
- `ImageIntelligenceService` plus shared identity and health-evidence models
- cross-source partition-name, filesystem-label/identifier, WIM/ESD container-GUID and FFU PlatformID aggregation
- bounded exFAT dirty/media-failure, ext state, NTFS backup-boot and FAT32 backup-boot health evidence
- WinUI **Analyze** action backed by shared `ImageReportService`
- human-readable and JSON analysis reporting plus Save JSON
- image-report smoke tests for unknown-input truthfulness, serialization and cancellation
- required `by Swir` + GitHub navigation footer in the Windows UI
- bounded `FileSystemDepthService` for deeper evidence inside already-recognized physical filesystem regions
- exFAT main/backup boot-region checksum validation and redundant-copy comparison
- FAT32 FSInfo placement, signature, free-cluster count and next-free-hint validation
- UDF primary anchor validation at logical block 256
- UDF descriptor-tag checksum, tag-location and descriptor-CRC validation
- bounded UDF main volume-descriptor sequence inspection with a 16 MiB ceiling
- validated UDF primary/logical volume d-string identity evidence
- dedicated filesystem-depth generated-fixture and cancellation smoke tests
- bounded `NtfsMetadataDepthService` for `$MFT` / `$MFTMirr` cluster/range, FILE-record size and Update Sequence Array validation
- fixup-normalized first-record `$MFT` / `$MFTMirr` consistency evidence without repair or traversal
- `ArchitectureReconciliationService` preserving independent boot-path and installer-path architecture hints
- explicit architecture-conflict warnings for direct one-to-one disagreement without mislabeling multi-architecture media
- dedicated intelligence-hardening fixtures for healthy/corrupt/out-of-range NTFS metadata, architecture reconciliation and cancellation
- clean Windows x64 ZIP candidate packaging with package manifest, debug/test-content rejection and SHA-256 sidecar
- centralized semantic product version metadata in `Directory.Build.props`
- executable ProductVersion/FileVersion plus package-manifest version/hash evidence
- independent `verify-package.ps1` clean-package gate for checksum, manifest, version, architecture, entry-point SHA-256, icon and payload policy
- canonical Dragon icon inclusion in the clean package root
- bounded `UdfTraversalService` for non-recursive root-directory traversal inside already-recognized physical UDF regions
- validated Type 1 UDF partition-map translation through matching Partition Descriptors
- bounded UDF File Set Descriptor, root File Entry and File Identifier Descriptor validation before root names are accepted
- UDF root traversal limits of 8 MiB and 4096 entries, with one recorded short allocation extent in the first slice
- validated UDF File Set Identifier identity evidence merged into the Analyze/report path
- generated UDF traversal fixtures for valid, corrupt-FID, out-of-range root ICB, unsupported Type 2 map and cancellation cases

### Changed
- development version remains **0.5.0-alpha.1**, sourced from the central build-version contract
- project progress advances to **57% toward 1.0** after validating the bounded UDF root-directory traversal slice
- **0.5 Partitions + File Systems + Image Intelligence** advances to approximately **93%**
- CI now gates provider registry, partition intelligence, filesystem recognition/depth, bounded UDF traversal, NTFS/architecture hardening, boot/installer intelligence, unified image intelligence and image reporting before provider/native/build regression
- the Windows analysis report merges bounded UDF traversal identity/health evidence, bounded NTFS depth findings and reconciled boot/installer architecture evidence
- green Windows CI builds, independently verifies and publishes a versioned clean ZIP candidate with SHA-256 in addition to the engineering artifact

### Safety
- all provider and intelligence paths remain read-only-first
- partition intelligence does not mount images, write bytes or repair partition tables
- filesystem recognition scans only bounded physical file ranges and refuses structurally invalid partition layouts
- sparse/compressed virtual/container formats do not gain fake guest-filesystem access without a guest-sector reader
- deeper filesystem checks cannot leave an already-recognized physical filesystem region
- exFAT redundant boot-region checks ignore only the mutable VolumeFlags and PercentInUse bytes when comparing copies
- UDF descriptor-sequence inspection is capped at 16 MiB and rejects overflowing/out-of-range extents
- UDF descriptor tags are validated before identity evidence is accepted
- UDF root traversal follows only validated Type 1 physical partition maps, is non-recursive and does not enable Direct Browse, extraction, repair or writes
- UDF Type 2 virtual/sparable/metadata partition maps remain unsupported rather than being translated heuristically
- NTFS depth checks validate bounded metadata only; they never repair records, follow attributes or traverse directories
- architecture reconciliation preserves conflicting evidence instead of guessing a winner
- bootability is reported only from structurally valid El Torito evidence
- health findings are evidence-backed checks, not a whole-filesystem “healthy” guarantee
- package hardening changes release metadata/verification only and enable no disk mutation capability
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
- PR #28 / run #232 — partition intelligence + full regression/build/artifact
- PR #29 / run #235 — filesystem recognition + full regression/build/artifact
- PR #30 / run #242 — boot/installer intelligence + full regression/build/artifact
- PR #31 / run #245 — unified identity/health implementation + full regression/build/artifact before docs synchronization
- PR #32 / run #260 — Windows Analyze/report surface + full regression/build/artifact
- PR #33 / run #262 — deeper filesystem evidence implementation + complete regression/build/artifact before docs synchronization
- PR #34 / run #266 — NTFS/architecture hardening, full regression/build, clean Windows ZIP candidate and SHA-256 before docs synchronization
- PR #34 / run #266 package review — sidecar SHA-256 matched the downloaded candidate ZIP; one app EXE; zero PDB/test-only files
- PR #35 / run #270 — central version metadata, complete regression/build, clean-package build, independent package verification and both artifact uploads before docs synchronization
- PR #35 / run #270 package review — downloaded sidecar SHA-256 matched the nested ZIP; manifest version `0.5.0-alpha.1`; executable SHA matched; one app EXE; zero PDB files
- PR #36 / run #274 — bounded UDF root traversal implementation + complete provider/Explorer/native Windows/Release/package-verification/artifact path before docs synchronization

### Planned
- truthful guest-sector readers before filesystem intelligence inside sparse/compressed virtual disks
- final 0.5 beta-scope hardening and clean-machine/manual QA
- promote the verified version pipeline to `0.5.0-beta.1` only when the beta gate is complete
- public beta `0.5.0-beta.1` only after the agreed 0.5 scope and independent package/manual QA gates are proven

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
