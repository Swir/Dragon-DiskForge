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
- `IWimMetadataProvider` + `WimMetadataInfo`
- truthful `ContainerMetadata` capability for archive/container formats such as WIM/ESD
- little-endian 208-byte `MSWIM\0\0\0` header parsing for version, flags, chunk size, GUID, part/image counts and boot index
- bounded lookup-table, XML, boot-metadata and integrity resource-descriptor parsing
- dedicated WIM/ESD smoke tests covering standard WIM, solid/ESD, invalid magic/header/version, split/spanned images, write-in-progress state, BootIndex, resource bounds/flags, chunk size, cancellation and registry capabilities

### Changed
- application registry now includes WIM / ESD metadata after DMG / UDIF
- project progress advances to **42% toward 1.0** after the tenth real additional 0.4 image family
- 0.4 milestone completion advances to approximately **89%**
- next 0.4 provider becomes **FFU**

### Safety
- all provider paths remain read-only-first
- WIM/ESD resource descriptors are bounded against the physical file before use
- split/spanned WIM is rejected until companion-part handling exists
- `WRITE_IN_PROGRESS`, unknown flags, invalid chunk geometry and invalid BootIndex are rejected rather than guessed
- WIM/ESD resources are not decompressed and embedded image file trees are not traversed or extracted
- encrypted ESD payloads are not decrypted
- no WIM/ESD Direct Browse, Mount or Convert is exposed

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

### Planned
- remaining 0.4 image provider: FFU
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
