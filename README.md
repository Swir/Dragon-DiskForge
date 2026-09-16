# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current version — 0.4.0-alpha.1

## Project progress — 39% toward 1.0

`████████░░░░░░░░░░░░ 39%`

**Overall completion:** **39%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **~71%** 🚧
- `0.5 → 1.0` — planned / future milestones

> The progress indicator is updated only after meaningful implementation and validation checkpoints. CI count alone never increases completion.

## 0.1 Foundation + Dragon Visual Identity ✅

- WinUI 3 / .NET 10 desktop shell
- `DragonDiskForge.Core` separated from the GUI
- drag & drop and file picker
- broad image-format catalogue and signature detection
- shared SHA-256 verification with progress and cancellation
- Dragon Forge dashboard, startup overlay, responsive layout and accessibility-aware themes
- final Windows application icon
- Core smoke tests and Windows x64 CI

## 0.2 Native Mount + Unmount ✅

Implemented and proven on Windows CI:

- native Windows mount/unmount for **ISO, VHD and VHDX**
- read-only-first behavior
- drive-letter and attached-state detection
- progress and cancellation
- live Mounted dashboard backed by Windows state
- stale-state recovery by re-querying Windows
- integration tests using disposable VHD/VHDX and IMAPI-generated ISO images

## 0.3 Dragon Explorer ✅

- mounted ISO/VHD/VHDX browsing
- folders-first navigation, breadcrumbs, metadata and recursive search
- safe Copy out with overwrite and reparse-point protection
- bounded text/image/PDF/media Preview modes
- Recent Images + Favorites with atomic persistence
- mounted-history metadata kept separate from live Windows state
- multi-image WinUI tab workspace
- safe Copy-only drag-out to Windows Explorer/Desktop
- provider-backed managed ISO9660/Joliet direct browsing without mounting
- direct ISO list/search/Copy out with cancellation and extent/path validation

## 0.4 Extended Image Providers 🚧

The provider foundation and **seven additional image families** are now implemented and proven.

### Provider architecture

- central Core `ProviderRegistry`
- explicit provider capability reporting
- deterministic priority and extension-first resolution
- signature/provider fallback when an extension candidate rejects an image
- probe and inspection failure isolation
- cancellation preserved as a hard stop
- provider diagnostics and duplicate-ID protection
- existing ISO9660/Joliet direct browsing resolved through the registry

### IMG / RAW partition provider ✅

- `.img`, `.raw`, `.dd`
- MBR primary partitions
- bounded EBR logical-partition traversal
- GPT with 512/4096-byte logical-sector probing
- common partition type/name decoding
- strict image-boundary validation
- explicit `PartitionTable` capability
- no fake Direct Browse, Mount or Convert

### IMA / floppy provider ✅

- `.ima`, `.flp`
- standard raw floppy geometry recognition from 160 KB through 2.88 MB
- optional FAT-style BPB parsing
- capacity and CHS consistency validation
- blank/unformatted exact-size recognition without fabricated filesystem metadata
- explicit `MediaGeometry` capability
- no fake Direct Browse, Mount or Convert

### BIN / CUE track-layout provider ✅

- BINARY CUE parsing
- AUDIO, MODE1/2048, MODE1/2352, MODE2/2336 and MODE2/2352
- single-file and multi-file layouts
- INDEX 00/01 validation
- payload existence/alignment and directory-containment checks
- ambiguous mixed-sector tracks in one BIN rejected rather than guessed
- explicit `TrackLayout` capability

### MDF / MDS CD track-layout provider ✅

- `MEDIA DESCRIPTOR` signature/version/medium/session/track validation
- explicit MDF byte offsets for mixed-sector CD layouts
- same-name and descriptor-footer MDF resolution
- ASCII/UTF-16 footer names, including `*.mdf`
- descriptor/payload ranges bounded against real file sizes
- DVD-style MDS intentionally rejected until separately implemented and tested

### NRG v1/v2 track-layout provider ✅

- classic `NERO` v1 and `NER5` v2 footers
- CUES BCD MSF-to-LBA decoding and CUEX signed-LBA decoding
- DAOI 32-bit and DAOX 64-bit byte-range parsing
- cue audio/data control cross-checked against DAO mode
- terminating empty `END!` required
- malformed, contradictory or overlapping metadata rejected

### CCD / IMG / SUB track-layout provider ✅

- CloneCD `.ccd`, same-name `.img` and validated `.sub`
- MODE 0/1/2 → AUDIO, MODE1/2352, MODE2/2352
- INDEX 0/1 bounded track positions
- IMG must be non-empty and aligned to 2352-byte raw sectors
- optional SUB must be exactly 96 bytes per IMG sector
- CCD outranks generic RAW for `.img` only after the same-name CCD descriptor validates
- unsupported descriptor versions/modes and invalid INDEX metadata rejected

### VMDK sparse metadata provider ✅

- read-only **VMware hosted sparse VMDK v1** inspection
- dedicated `IVirtualDiskMetadataProvider` contract and `VirtualDiskMetadata` capability
- validates the 512-byte sparse header magic `0x564D444B` and version 1
- parses virtual capacity, grain size, descriptor offset/size, grain-table entry count, redundant grain-directory offset, grain-directory offset and metadata overhead
- sector-based physical offsets are overflow-checked and bounded against the real `.vmdk` file
- validates unclean-shutdown, newline-detection and compression metadata consistency
- optional embedded descriptor is bounded to 1 MiB and parsed for `version`, `createType`, `CID`, `parentCID` and extent count
- conflicting or malformed descriptor metadata is rejected rather than guessed
- text-only descriptor VMDKs and unproven sparse-header versions are intentionally outside this first slice
- grain-table translation, virtual-sector reads, filesystem browsing, Mount and Convert remain disabled until implemented and tested
- PR #22 / run #200 passed VMDK tests, all previous provider tests, ISO/native Windows regressions, WinUI Release x64 build and artifact publication ✅

### Next 0.4 provider

**QCOW / QCOW2** — bounded read-only container metadata inspection first. Direct virtual-disk I/O and filesystem browsing will remain disabled until their real translation layers are implemented and proven.

The cross-process human drag gesture and normal-user UAC prompt remain manual QA gates in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md), because GitHub-hosted runners cannot faithfully reproduce those user interactions.

The planned first public GitHub beta is **`0.5.0-beta.1`** after the required 0.4 provider work and agreed 0.5 image-intelligence scope are proven. See [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md), with current execution state in [`docs/STATUS.md`](docs/STATUS.md) and [`docs/MILESTONES.md`](docs/MILESTONES.md).

## Tech

- C#
- .NET 10
- WinUI 3
- Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows service layer for native Storage operations
- provider registry with explicit capabilities, fallback and failure isolation
- bounded read-only parsers for partition, floppy, optical-layout and virtual-disk metadata

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

Automated validation runs Core and provider-registry tests; RAW/IMG, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB and VMDK provider tests; mounted-history and drag-out safety tests; real ISO direct-browse integration; native Windows ISO/VHD/VHDX integration; and a full Windows x64 Release build. Green runs publish a `DragonDiskForge-win-x64` artifact.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Copy out and drag-out are explicit copy operations; drag-out never advertises Move. Every metadata parser validates offsets/ranges before using them and rejects contradictory structures rather than guessing.

VMDK inspection currently reads only proven sparse-v1 metadata and a bounded embedded descriptor. It does **not** translate grain tables, expose virtual sectors, browse filesystems, mount VMDK, or convert it. Unsupported capabilities remain disabled.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
