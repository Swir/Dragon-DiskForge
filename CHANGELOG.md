# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- read-only IMG/RAW partition inspection with MBR, bounded EBR and GPT parsing
- read-only IMA/FLP media geometry with FAT-style BPB validation
- read-only BIN/CUE, MDF/MDS CD, NRG v1/v2 and CCD/IMG/SUB optical track-layout providers
- read-only VMware hosted sparse VMDK v1 metadata provider
- `IVirtualDiskMetadataProvider`, `VirtualDiskMetadataInfo` and shared `VirtualDiskMetadata` capability
- read-only QCOW v1 and QCOW2 v2/v3 metadata provider
- `IQcowMetadataProvider` and `QcowMetadataInfo`, also mapped to `VirtualDiskMetadata`
- big-endian QCOW parsing for virtual size, cluster geometry, backing metadata, encryption and L1 metadata
- QCOW2 refcount/snapshot metadata and v3 feature-mask/refcount-order/header-length validation
- bounded Zstandard compression metadata validation for QCOW2 v3
- dedicated QCOW smoke tests for valid v1/v2/v3, backing metadata, malformed geometry/ranges/features, corrupt/external-data/autoclear states, compression mismatch, cancellation and registry capabilities

### Changed
- application registry now includes QCOW/QCOW2 metadata after VMDK
- backing-file names are treated strictly as image metadata and are never followed/opened
- project progress advances to **40% toward 1.0** after the eighth real additional 0.4 image family
- 0.4 milestone completion advances to approximately **77%**
- next 0.4 provider becomes **DMG**

### Safety
- all provider paths remain read-only-first
- QCOW/QCOW2 table offsets/ranges are checked against the physical file before use
- QCOW v1 cluster/L2 geometry, reserved padding, encryption metadata and derived L1 size are validated
- QCOW2 L1/refcount offsets must be cluster-aligned and bounded
- backing-file names are limited to 1023 bytes, strict UTF-8 and permitted header/cluster ranges
- QCOW2 v3 rejects unknown incompatible/compatible bits, corrupt state, external-data mode and non-zero autoclear state in this first slice
- QCOW2 header length/refcount order and compression feature/type consistency are validated
- no QCOW cluster translation, virtual-sector I/O, Direct Browse, Mount or Convert is exposed

### Verified
- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy + full regression/build/artifact
- PR #18 / run #172 — BIN/CUE + full regression/build/artifact
- PR #19 / run #182 — MDF/MDS + full regression/build/artifact
- PR #20 / run #185 — NRG + full regression/build/artifact
- PR #21 / run #198 — final CCD/IMG/SUB head + full regression/build/artifact
- PR #22 / run #205 — final VMDK head + full provider/native regression/build/artifact
- PR #23 / run #207 — QCOW/QCOW2 v1/v2/v3 tests, all previous provider tests, Explorer safety, ISO/native Windows integration, Restore, Release x64 build and artifact publication all passed before docs synchronization

### Planned
- remaining 0.4 image providers: DMG, WIM/ESD and FFU

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
