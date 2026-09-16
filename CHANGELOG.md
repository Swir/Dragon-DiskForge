# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- read-only IMG/RAW partition inspection with MBR, bounded EBR and GPT parsing
- read-only IMA/FLP media geometry with FAT-style BPB validation
- read-only BIN/CUE, MDF/MDS CD, NRG v1/v2 and CCD/IMG/SUB optical track-layout providers
- read-only VMware hosted sparse VMDK v1 metadata provider
- read-only QCOW v1 and QCOW2 v2/v3 metadata provider
- read-only Apple DMG / UDIF metadata provider
- read-only WIM / ESD container metadata provider
- read-only FFU container metadata provider
- hardened provider registration/descriptor contract and deterministic equal-priority ordering

### Changed
- provider registrations now snapshot validated descriptors at creation time
- registry extension matching now uses immutable normalized descriptor extensions rather than mutable provider metadata
- invalid provider IDs, null/empty/invalid extension entries and duplicate normalized extensions fail fast
- blank capability-specific display names fall back to the stable provider ID
- project progress advances to **44% toward 1.0**
- **0.4 Extended Image Providers reaches 100% of its required engineering scope**
- next milestone becomes **0.5 Partitions + File Systems + Image Intelligence**

### Safety
- all provider paths remain read-only-first
- existing cancellation, failure isolation and truthful capability behavior remains intact
- hardening adds no new mount, extraction, conversion or write capability
- signature-only providers remain supported without requiring fake file extensions
- no stable public plugin API is declared by closing the internal 0.4 contract gate

### Verified
- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy + full regression/build/artifact
- PR #18 / run #172 — BIN/CUE + full regression/build/artifact
- PR #19 / run #182 — MDF/MDS + full regression/build/artifact
- PR #20 / run #185 — NRG + full regression/build/artifact
- PR #21 / run #198 — final CCD/IMG/SUB head + full regression/build/artifact
- PR #22 / run #205 — final VMDK head + full provider/native regression/build/artifact
- PR #23 / run #212 — final QCOW/QCOW2 head + full regression/build/artifact
- PR #24 / run #219 — final DMG/UDIF head + full regression/build/artifact
- PR #25 / run #222 — WIM/ESD + full regression/build/artifact
- PR #26 / run #226 — final FFU docs-synchronized head + full regression/build/artifact
- PR #27 / run #228 — provider-contract hardening + all provider/native/Explorer tests + Release x64 build and artifact

### Planned
- 0.5 partition/filesystem/image-intelligence work
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
