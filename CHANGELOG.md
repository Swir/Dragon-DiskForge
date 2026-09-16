# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- read-only IMG/RAW partition inspection with MBR, bounded EBR and GPT parsing
- `IPartitionTableProvider` and explicit `PartitionTable` capability
- read-only IMA/FLP media-geometry inspection with FAT-style BPB validation
- `IMediaGeometryProvider` and explicit `MediaGeometry` capability
- read-only BIN/CUE, MDF/MDS CD, NRG v1/v2 and CCD/IMG/SUB optical track-layout providers
- `ITrackLayoutProvider` and explicit `TrackLayout` capability
- read-only VMware hosted sparse VMDK v1 metadata provider
- `IVirtualDiskMetadataProvider`, `VirtualDiskMetadataInfo` and explicit `VirtualDiskMetadata` capability
- VMDK sparse-header parsing for magic, version, flags, virtual capacity, grain size, embedded descriptor location, grain-table entry count, redundant grain-directory offset, grain-directory offset, metadata overhead, unclean-shutdown state, newline metadata and compression algorithm
- bounded embedded VMDK descriptor parsing for descriptor `version`, `createType`, `CID`, `parentCID` and extent declaration count
- dedicated VMDK smoke tests covering valid sparse v1 metadata, absent embedded descriptor, bad magic/version, invalid grain/capacity metadata, descriptor bounds, grain-directory bounds, newline/compression inconsistency, malformed descriptor metadata, text-only descriptors, foreign extensions, cancellation and provider capability resolution

### Changed
- application provider registry now includes ISO9660/Joliet, CCD/IMG/SUB, RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG and VMDK metadata providers
- Direct Browse capability messaging now distinguishes virtual-disk metadata inspection from filesystem browsing
- project progress advances to **39% toward 1.0** after the seventh real additional 0.4 image family
- 0.4 milestone completion advances to approximately **71%**
- the next planned 0.4 provider is **QCOW/QCOW2**

### Safety
- provider parsing remains read-only and bounded against real file sizes
- RAW validates partition ranges and protects EBR traversal from loops
- floppy rejects unsupported sizes and contradictory BPB/CHS metadata
- BIN/CUE confines payload paths and rejects ambiguous mixed-sector offsets
- MDF/MDS bounds descriptor/payload structures and explicitly rejects unproven DVD-style handling
- NRG requires consistent cue/DAO metadata and a terminating empty `END!` chunk
- CCD/IMG/SUB validates IMG sector alignment, SUB length and CloneCD MODE/INDEX metadata; generic `.img` fallback remains available when CCD validation fails
- VMDK claims only `.vmdk` files with the supported sparse-header magic/version and structurally valid metadata
- VMDK descriptor, redundant grain-directory, grain-directory and overhead offsets are overflow-checked and bounded against the physical file
- VMDK capacity/grain metadata, newline bytes and compression state must be internally consistent
- embedded VMDK descriptors are limited to 1 MiB with bounded lines/line lengths; conflicting duplicate metadata is rejected
- text-only descriptor VMDKs, unproven sparse-header versions, grain-table translation, virtual-sector reads, Direct Browse, Mount and Convert remain outside this slice

### Verified
- PR #16 / run #153 — RAW/IMG provider smoke tests passed before final UI/docs synchronization
- PR #17 / run #165 — IMA/floppy provider plus full regression, Release x64 build and artifact publication passed
- PR #18 / run #172 — BIN/CUE provider plus full prior-provider regression, Release x64 build and artifact publication passed
- PR #19 / run #182 — final MDF/MDS CD branch head plus full provider/native regression, Release x64 build and artifact publication passed
- PR #20 / run #185 — NRG v1/v2 provider plus full prior-provider regression, Release x64 build and artifact publication passed before documentation synchronization
- PR #21 / run #198 — final CCD/IMG/SUB branch head passed all provider tests, ISO/native Windows integration, Release x64 build and artifact publication
- PR #22 / run #200 — VMDK sparse metadata tests, all previous provider tests, mounted-history/drag-out tests, ISO direct-browse integration, native ISO/VHD/VHDX integration, restore, full WinUI Release x64 build and artifact publication all passed before documentation synchronization

### Planned
- remaining 0.4 image providers: QCOW/QCOW2, DMG, WIM/ESD and FFU

## [0.3.0] - 2026-09-15

### Added
- Dragon Explorer for mounted ISO/VHD/VHDX volumes
- folder navigation, metadata, search and safe Copy out
- bounded text/image/PDF/media Preview modes
- Recent Images and Favorites with local atomic persistence
- Mounted history kept separate from live Windows state
- multi-image Explorer workspace with WinUI tabs
- safe Copy-only drag-out to Windows Explorer/Desktop
- provider-backed managed ISO9660/Joliet direct browsing without mounting
- Windows x64 artifact publishing

### Changed
- project version advanced to `0.3.0`
- milestone 0.3 completed and 0.4 became active

### Verified
- PR #5 / run #56 — mounted Explorer
- PR #6 / run #72 — Preview + Image Library
- PR #7 / run #86 — Mounted history + multi-image workspace
- PR #10 / run #103 — safe drag-out + x64 artifact
- PR #12 / run #128 and #131 — direct ISO browsing, native regression, WinUI Release build and progress synchronization

## [0.2.0] - 2026-09-14

### Added
- native Windows ISO/VHD/VHDX mount/unmount service
- read-only-first mount requests
- drive-letter and attached-state detection
- progress/cancellation and live Mounted state
- VHD/VHDX/ISO integration tests

## [0.1.0] - 2026-09-14

### Added
- initial WinUI 3 / .NET 10 application shell
- separate Core project
- disk-image catalogue and signature detection
- SHA-256 verification with progress/cancellation
- Dragon visual system, responsive UI and final Windows icon
- Core smoke tests and Windows x64 CI
