# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- first non-ISO 0.4 image provider: read-only IMG/RAW partition inspection
- MBR primary-partition parsing with boot flags and common partition-type names
- bounded EBR traversal for logical partitions with loop and image-boundary protection
- GPT parsing with 512/4096-byte logical-sector probing, entry-count/size limits and partition-name/type decoding
- `IPartitionTableProvider` contract and `PartitionTable` provider capability
- `.dd` raw-image extension support
- dedicated RAW/IMG smoke tests covering valid MBR/EBR/GPT images, malformed images, cancellation and registry capability resolution
- read-only IMA/FLP media-geometry provider for standard floppy capacities from 160 KB through 2.88 MB
- `IMediaGeometryProvider` contract and `MediaGeometry` provider capability
- FAT-style BPB metadata parsing with capacity, sectors/track, head-count and CHS consistency validation
- safe blank/unformatted floppy recognition based on exact standard media size
- dedicated IMA/floppy smoke tests covering FAT12 metadata, blank media, bad capacity, bad CHS, foreign extension isolation and cancellation
- read-only BIN/CUE track-layout provider
- `ITrackLayoutProvider` contract and `TrackLayout` provider capability
- BINARY CUE parsing for AUDIO, MODE1/2048, MODE1/2352, MODE2/2336 and MODE2/2352 tracks
- single-file and multi-file BIN/CUE layout validation with INDEX 00/01 metadata and bounded track ranges
- same-name CUE companion resolution for `.bin` inputs
- dedicated BIN/CUE smoke tests covering mixed-mode, multi-file, missing payload, traversal, index ordering, unsupported FILE types, orphan BINs and cancellation
- read-only MDF/MDS CD track-layout provider using the shared `TrackLayout` capability
- MDS `MEDIA DESCRIPTOR` header/version/medium/session/track validation
- explicit MDF byte-offset handling for mixed-sector CD layouts
- same-name MDF and footer-based ASCII/UTF-16 payload resolution, including `*.mdf`
- dedicated MDF/MDS smoke tests covering mixed-sector tracks, corrupt signatures/offsets, missing payloads, unsupported sectors, traversal, wildcard/UTF-16 footers, DVD rejection, foreign extensions and cancellation
- read-only Nero NRG v1/v2 track-layout provider using the shared `TrackLayout` capability
- classic `NERO` v1 footer parsing with 32-bit chunk offsets and `NER5` v2 footer parsing with 64-bit chunk offsets
- CUES v1 BCD MSF-to-LBA decoding and CUEX v2 signed-LBA decoding
- DAOI v1 and DAOX v2 track-table parsing with explicit byte-range validation
- dedicated NRG smoke tests covering v1/v2, mixed audio/data, CUES MSF conversion, bad footer/offset, missing `END!`, missing cue metadata, unknown modes, cue/DAO mismatch, metadata overlap, foreign extensions and cancellation

### Changed
- the application provider registry now includes ISO9660/Joliet, RAW partition, IMA/floppy, BIN/CUE, MDF/MDS and NRG providers
- RAW/IMG images can be positively recognized by provider metadata without enabling fake Mount or Direct Browse actions
- IMA/FLP images can be positively recognized by media geometry without enabling fake filesystem browsing
- BIN/CUE, MDF/MDS and NRG images can be positively recognized by optical track layout without enabling fake filesystem/content browsing
- Direct Browse tooltip explains when a provider recognized only a non-browse inspection capability
- project progress advances to 37% toward 1.0 after the fifth real additional 0.4 image family
- 0.4 milestone completion advances to approximately 59%

### Safety
- RAW/IMG parsing is read-only and validates every referenced partition range against the image length
- EBR traversal is bounded and loop-protected
- GPT entry count and entry size are bounded before allocation/iteration
- invalid `.img`/`.raw` extensions alone never make an image supported
- IMA/FLP parsing is read-only and rejects unsupported sizes, inconsistent BPB capacity and contradictory CHS geometry
- BIN/CUE parsing is read-only, bounds CUE size/line/track counts and validates every referenced BIN against real file length and sector alignment
- CUE payload paths must remain under the CUE directory; absolute/path-traversal references are rejected
- mixed sector sizes inside one BIN are rejected rather than producing guessed byte offsets
- a standalone `.bin` is not claimed without a same-name CUE that actually references it
- MDF/MDS parsing bounds descriptor tables, footer strings, MDF start offsets and computed track ranges against the real files
- MDS footer payload paths are confined to the descriptor directory; rooted/traversal paths are rejected
- mixed-sector MDF layouts use explicit MDS byte offsets rather than inferred offsets
- DVD-style MDS media is deliberately rejected until its separate layout is implemented and tested
- NRG parsing bounds every chunk before the footer, requires a terminating empty `END!` chunk and rejects trailing metadata
- NRG DAO track ranges must end before the chunk table; unknown modes, malformed BCD/MSF positions and missing/ambiguous cue metadata are rejected
- NRG cue audio/data control must agree with the DAO track mode rather than being guessed
- RAW/IMG, IMA/FLP, BIN/CUE, MDF/MDS and NRG `DirectBrowse`, Mount and Convert remain disabled until their real backend capabilities exist

### Verified
- RAW/IMG provider smoke tests passed in PR #16 / run #153 before the documentation/UI synchronization pass
- PR #17 / run #165 passed Core, provider-registry, RAW/IMG, IMA/floppy, mounted-history and drag-out smoke tests, ISO direct-browse integration, native ISO/VHD/VHDX integration, restore, full WinUI Release x64 build and artifact publication
- PR #18 / run #172 passed Core, provider-registry, RAW/IMG, IMA/floppy, BIN/CUE, mounted-history and drag-out smoke tests, ISO direct-browse integration, native ISO/VHD/VHDX integration, restore, full WinUI Release x64 build and artifact publication
- PR #19 / run #182 passed the final MDF/MDS branch head with all provider smoke tests, ISO/native regression, WinUI Release x64 build and artifact publication
- PR #20 / run #185 passed NRG v1/v2 smoke tests, all previous provider tests, mounted-history and drag-out tests, ISO direct-browse integration, native ISO/VHD/VHDX integration, restore, full WinUI Release x64 build and artifact publication before the documentation synchronization pass

### Planned
- remaining 0.4 image providers: CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG, WIM/ESD and FFU

## [0.3.0] - 2026-09-15

### Added
- Dragon Explorer for mounted ISO/VHD/VHDX volumes
- folder navigation, metadata, search and safe Copy out
- bounded text/image/PDF/media Preview modes
- Recent Images and Favorites with local atomic persistence
- Mounted history kept separate from live Windows state
- multi-image Explorer workspace with WinUI tabs
- safe Copy-only drag-out to Windows Explorer/Desktop
- provider-backed direct browsing contract
- managed read-only ISO9660/Joliet direct-browse provider
- direct ISO navigation, search and Copy out without mounting
- dedicated `Direct ISO` tabs marked `NO MOUNT`
- direct ISO integration tests using a disposable Windows IMAPI image
- Windows x64 artifact publishing
- README project progress bar synchronized with the roadmap

### Changed
- project version advanced to `0.3.0`
- milestone 0.3 is complete
- 0.4 Extended Image Providers is the next milestone
- direct ISO tabs are independent from Mount/Unmount state
- Explorer actions remain enabled only when their real backend exists

### Fixed
- WinUI namespace/path naming conflicts found by CI
- Mounted history remains independent from live Windows inventory
- direct ISO parser compilation issue found by CI
- direct ISO IMAPI test fixture compatibility issue found by CI
- deterministic UTF-8 test fixture output

### Verified
- PR #5 / run #56 — mounted Explorer slice
- PR #6 / run #72 — Preview + Image Library
- PR #7 / run #86 — Mounted history + multi-image workspace
- PR #10 / run #103 — safe drag-out + Windows x64 artifact
- PR #10 / run #111 — final drag-out docs/version regression
- PR #12 / run #128 — direct ISO browsing + native mount regression + WinUI build + artifact
- PR #12 / run #131 — full regression remained green after README progress synchronization

### Manual QA notes
- normal-user UAC interaction remains documented in `docs/MANUAL-VALIDATION.md`
- the real cross-process pointer drag gesture remains a manual desktop QA case

## [0.2.0] - 2026-09-14

### Added
- `IMountService` contract in Core
- isolated Windows service layer
- native ISO, VHD and VHDX mount/unmount
- read-only-first mount requests
- drive-letter and attached-state detection
- Mount/Unmount progress and cancellation
- live Mounted dashboard backed by Windows state
- stale-state recovery by re-querying Windows
- friendly native-operation errors
- disposable VHD/VHDX and IMAPI ISO integration tests
- manual non-admin/UAC checklist

### Changed
- Mount controls are enabled only for proven native paths
- Mounted navigation is a real working view
- Windows owns mounted state; the app does not trust stale session cache
- project version advanced to `0.2.0`

### Fixed
- replaced unreliable direct mounted-image class enumeration with the Windows Storage volume pipeline
- corrected integration-test imports

### Verified
- ISO, VHD and VHDX mount/unmount paths pass Windows CI
- read-only state, drive detection and mounted inventory are validated
- cancellation safety and unsupported-format errors are validated

## [0.1.0] - 2026-09-14

### Added
- initial WinUI 3 / .NET 10 application shell
- separate Core project
- drag-and-drop image loading and file picker
- supported-format catalogue and signature detection
- SHA-256 verification with progress and cancellation
- Dragon visual design system and custom sigil
- responsive dashboard and startup overlay
- light/dark/High Contrast resources
- final Windows application icon
- Core smoke-test harness
- GitHub Actions Windows x64 validation
- roadmap, milestone, status and testing documentation

### Fixed
- solution platform mappings for `Release|x64`
- verification smoke-test delegate
- startup overlay stacking
- Windows icon resource packaging

### Verified
- Core smoke tests pass
- full WinUI `Release|x64` CI passes
- milestone 0.1 exit criteria passed on `main`
