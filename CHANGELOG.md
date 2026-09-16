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
- `IFfuMetadataProvider` + `FfuMetadataInfo`
- FFU `ContainerMetadata` capability integration
- bounded common FFU security/image/store metadata parsing
- FFU base-signature recognition through `SignedImage `
- dedicated FFU smoke tests covering signatures, header sizes, SHA-256 algorithm metadata, chunk alignment, catalog/hash/manifest bounds, PlatformID, store block size, descriptor counts/lengths, cancellation and registry capabilities

### Changed
- application registry now includes FFU metadata after WIM / ESD
- project progress advances to **43% toward 1.0** after the eleventh real additional 0.4 image family
- 0.4 milestone completion advances to approximately **95%**
- remaining 0.4 work becomes **provider-contract hardening** rather than another required image family

### Safety
- all provider paths remain read-only-first
- FFU catalog/hash, image/manifest and store metadata regions are bounded against the physical file before use
- FFU write-descriptor destinations are not interpreted
- FFU payload chunks are not mapped to physical media
- no physical-device access, sector writing, image application, FFU Direct Browse, Mount or Convert is exposed
- unknown or contradictory FFU structural metadata is rejected rather than guessed

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
- PR #25 / run #222 — WIM/ESD metadata tests, all previous provider tests, Explorer safety, ISO/native Windows integration, Restore, Release x64 build and artifact publication
- PR #26 / run #225 — FFU metadata tests, all previous provider tests, Explorer safety, ISO/native Windows integration, Restore, Release x64 build and artifact publication

### Planned
- provider-contract hardening before any public stability promise

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
