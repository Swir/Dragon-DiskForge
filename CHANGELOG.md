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
- `IDmgMetadataProvider` and `DmgMetadataInfo`, mapped to the shared `VirtualDiskMetadata` capability
- big-endian 512-byte `koly` trailer parsing for version, flags, fork metadata, segment metadata, checksums, XML plist location, image variant and sector count
- bounded XML plist parsing with DTD/external resolution disabled and `blkx` entry counting without `mish` block-map decoding
- dedicated DMG smoke tests covering valid UDIF metadata, XML/no-XML images, malformed trailer/ranges/XML, segmentation, checksum bounds, foreign extensions, cancellation and registry capabilities

### Changed
- application registry now includes DMG / UDIF metadata after QCOW/QCOW2
- project progress advances to **41% toward 1.0** after the ninth real additional 0.4 image family
- 0.4 milestone completion advances to approximately **83%**
- next 0.4 provider becomes **WIM/ESD**

### Safety
- all provider paths remain read-only-first
- DMG physical data/resource/XML ranges are bounded before reads and may not extend into the final `koly` trailer
- DMG XML metadata is limited to 16 MiB; DTD and external entity resolution are disabled
- multi-segment DMGs are rejected until companion-segment handling is implemented
- malformed/unsupported UDIF trailers, unsafe checksum sizes and malformed plist/blkx structure are rejected rather than guessed
- no DMG `blkx`/`mish` block decompression, guest-sector translation, Direct Browse, Mount or Convert is exposed

### Verified
- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy + full regression/build/artifact
- PR #18 / run #172 — BIN/CUE + full regression/build/artifact
- PR #19 / run #182 — MDF/MDS + full regression/build/artifact
- PR #20 / run #185 — NRG + full regression/build/artifact
- PR #21 / run #198 — final CCD/IMG/SUB head + full regression/build/artifact
- PR #22 / run #205 — final VMDK head + full provider/native regression/build/artifact
- PR #23 / run #212 — final QCOW/QCOW2 head + all previous provider tests, ISO/native Windows integration, Release x64 build and artifact publication
- PR #24 / run #214 — DMG/UDIF tests, all previous provider tests, Explorer safety, ISO/native Windows integration, Restore, Release x64 build and artifact publication all passed before docs synchronization

### Planned
- remaining 0.4 image providers: WIM/ESD and FFU

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
