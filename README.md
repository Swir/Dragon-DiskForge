# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current version — 0.4.0-alpha.1

## Project progress — 37% toward 1.0

`███████░░░░░░░░░░░░░ 37%`

**Overall completion:** **37%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **~59%** 🚧
- `0.5 → 1.0` — planned / future milestones

> This progress indicator is updated together with the roadmap, changelog and milestone status after meaningful project checkpoints. The percentage reflects completed roadmap milestones and proven functionality, not CI count alone.

### 0.1 Foundation + Dragon Visual Identity ✅

- WinUI 3 / .NET 10 desktop shell
- `DragonDiskForge.Core` separated from the GUI
- drag & drop and file picker
- broad image-format catalogue and signature detection
- shared Core SHA-256 verification with live progress and cancellation
- Dragon Forge dashboard, startup overlay, responsive layout, light/dark/High Contrast resources and final Windows icon
- Core smoke tests and Windows x64 CI

### 0.2 Native Mount + Unmount ✅

Implemented and proven on Windows CI:

- native Windows mount service for **ISO, VHD and VHDX**
- native unmount/eject
- read-only-first mount behavior
- drive-letter and attached-state detection
- Mount/Unmount progress and cancellation
- live **Mounted** dashboard backed by Windows state
- stale-state recovery by re-querying Windows
- friendly unsupported-format/native-operation error translation
- real Windows integration tests using disposable VHD/VHDX and IMAPI-generated ISO images

### 0.3 Dragon Explorer ✅

Dragon Explorer is complete for the 0.3 scope and includes five proven slices.

**Mounted-volume Explorer**

- real `IExplorerService` contract in Core
- in-app browsing of mounted ISO/VHD/VHDX volumes
- folders-first listing, Up navigation and breadcrumb/address display
- recursive search with cancellation and result limits
- safe **Copy out** to a user-selected destination
- overwrite protection and reparse-point/junction safety
- trust warning before opening executable/script content
- real Windows integration: mounted ISO → list → search → Copy out → content verification

**Preview + Image Library**

- bounded read-only text preview with truncation indication
- image preview rendered inside Dragon Explorer without shell execution
- PDF and media metadata-only preview modes
- binary/unsupported metadata fallback
- preview cancellation so stale selections cannot replace the newest selection
- local Recent Images + Favorites with atomic JSON persistence
- real Images view with Open / Favorite / Unfavorite / Remove actions

**Mounted history + multi-image workspace**

- local mounted-history service with atomic persistence
- distinct Mount / Unmount events and bounded newest-first retention
- live Windows mounted state kept separate from local history metadata
- Mounted dashboard with a dedicated history section and safe Clear History behavior
- multi-image Dragon Explorer workspace using WinUI tabs
- each mounted image opens in an independent read-only Explorer tab
- reopening the same image/root activates the existing tab instead of duplicating it
- closing a tab never unmounts the image
- successful unmount closes tabs backed by that image
- stale Explorer tabs are pruned if their Windows drive root disappears

**Safe drag-out to Windows Explorer**

- mounted Explorer files/folders can be dragged directly to Windows Explorer/Desktop as **Copy-only** storage items
- the source is revalidated against the mounted root immediately before transfer
- stale sources, path escapes, reparse points and junctions are blocked
- runtime filesystem attributes are rechecked so stale UI state cannot bypass the safety gate
- drag data is prepared asynchronously through the WinUI `DragStarting` deferral
- dedicated drag-out safety smoke tests run in CI

**Provider-backed ISO direct browsing**

- managed read-only ISO9660/Joliet provider
- list folders/files directly from ISO extents without `Mount-DiskImage`
- navigate nested folders and search recursively
- safe file/folder Copy out with progress and cancellation
- overwrite, path-traversal, extent-boundary and Windows filename safety checks
- dedicated WinUI **Direct ISO** tabs marked **NO MOUNT**
- `Explore directly` is enabled only after the provider positively recognizes the current ISO
- real IMAPI-generated ISO integration proves list/search/Copy out while the image remains detached

### 0.4 Extended Image Providers 🚧

The provider foundation and five additional image families are now implemented:

- central Core `ProviderRegistry`
- explicit provider capability reporting
- deterministic priority and extension-first selection
- signature/provider fallback when an extension candidate does not accept the image
- probe-failure and inspection-failure isolation so one parser cannot break the provider chain
- cancellation remains a hard stop rather than being swallowed as a parser error
- provider diagnostics for future UI/CLI reporting
- duplicate provider-ID protection
- existing ISO9660/Joliet direct browsing resolves through the registry instead of hardcoded `.iso` UI logic
- read-only **IMG / RAW partition provider** for `.img`, `.raw` and `.dd`
- MBR primary partitions plus bounded EBR logical-partition traversal
- GPT parsing with 512/4096-byte logical-sector probing
- strict partition/image boundary validation and corrupt-image rejection
- common MBR and GPT partition types plus GPT partition names
- dedicated `PartitionTable` capability; RAW does **not** advertise Direct Browse, Mount or Convert
- read-only **IMA / floppy provider** for `.ima` and `.flp`
- exact standard floppy geometry recognition from 160 KB through 2.88 MB
- FAT-style BIOS Parameter Block validation when present, including capacity and CHS consistency checks
- blank/unformatted standard-size floppy recognition without inventing filesystem metadata
- dedicated `MediaGeometry` capability; floppy images do **not** advertise Direct Browse, Mount or Convert
- read-only **BIN / CUE track-layout provider** with explicit `TrackLayout` capability
- BINARY CUE parsing for AUDIO, MODE1/2048, MODE1/2352, MODE2/2336 and MODE2/2352 tracks
- single-file and multi-file CUE layouts with BIN existence/alignment and INDEX 00/01 range validation
- standalone `.bin` accepted only when a same-name `.cue` exists and references that BIN
- absolute/path-traversal payload references, unsupported FILE types and ambiguous mixed-sector tracks in one BIN are rejected
- read-only **MDF / MDS CD track-layout provider** reusing the same `TrackLayout` capability
- `MEDIA DESCRIPTOR` header, version, medium type, session and track-block validation
- explicit per-track MDF byte offsets allow safe mixed-sector layouts without guessed offsets
- same-name MDF plus footer-based ASCII/UTF-16 payload resolution, including `*.mdf`
- descriptor and payload ranges are bounded against real file sizes; payload paths stay inside the descriptor directory
- DVD-style MDS media is explicitly rejected until its distinct layout is implemented and tested
- read-only **NRG track-layout provider** for classic `NERO` v1 and `NER5` v2 images
- v1 CUES BCD `MM:SS:FF` metadata is decoded to real LBA; v2 CUEX uses signed LBA directly
- DAOI 32-bit and DAOX 64-bit byte offsets are bounded against the image and metadata table
- cue audio/data control is cross-checked against DAO track mode; unknown modes and ambiguous/missing cue metadata are rejected
- NRG requires a terminating empty `END!` chunk and rejects trailing metadata or track ranges overlapping the chunk table
- BIN/CUE, MDF/MDS and NRG do **not** advertise Direct Browse, Mount or Convert
- dedicated provider-registry, RAW/IMG, IMA/floppy, BIN/CUE, MDF/MDS and NRG smoke tests in CI
- PR #20 / run #185: NRG v1/v2 tests + full prior regression + WinUI Release x64 + artifact ✅

The next 0.4 work is **CCD/IMG/SUB**. Filesystem-level browsing for RAW, floppy and optical provider families remains intentionally disabled until the corresponding filesystem/content layers are implemented and proven.

The cross-process human drag gesture itself remains in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md), because GitHub Actions cannot reliably emulate a person dragging an item into Windows Explorer.

The interactive UAC prompt itself cannot be faithfully exercised on GitHub-hosted administrator runners. A normal-user desktop checklist is maintained in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md) and remains a manual QA gate before public beta packaging.

The planned first public GitHub beta is **`0.5.0-beta.1`** after the required 0.4 provider work and agreed 0.5 image-intelligence scope are proven. See [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md). Execution work is tracked with GitHub Issues and pull requests.

Major milestones:

`0.1 Foundation + Dragon UI ✅` → `0.2 Native Mount ✅` → `0.3 Dragon Explorer ✅` → `0.4 Extended Providers 🚧` → `0.5 Filesystems + Image Intelligence` → `0.6 Create/Convert/Verify` → `0.7 Bootable USB` → `0.8 Windows Integration` → `0.9 Beta Hardening` → `1.0 Production`

The visual specification lives in [`docs/DRAGON-DESIGN.md`](docs/DRAGON-DESIGN.md), and the vector sigil is stored under [`docs/branding/dragon-sigil.svg`](docs/branding/dragon-sigil.svg).

## Tech

- C#
- .NET 10
- WinUI 3
- Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows service layer for native Storage operations
- provider registry with explicit capabilities, fallback and failure isolation
- provider-backed direct-browse architecture for formats that can be safely parsed without mounting
- bounded read-only partition-table parsing for RAW disk images
- bounded read-only floppy geometry/BPB inspection
- bounded read-only optical track-layout parsing for BIN/CUE, MDF/MDS CD and NRG v1/v2 images

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

Automated validation runs Core smoke tests, provider-registry smoke tests, RAW/IMG partition-provider smoke tests, IMA/floppy provider smoke tests, BIN/CUE provider smoke tests, MDF/MDS provider smoke tests, NRG provider smoke tests, mounted-history smoke tests, drag-out safety smoke tests, real ISO direct-browse integration, native Windows ISO/VHD/VHDX integration, Explorer/Preview integration and a full Windows x64 Release build in GitHub Actions.

Green CI runs publish a `DragonDiskForge-win-x64` artifact for desktop/manual validation.

## Safety design

Inspection, hashing, preview, mounted-volume browsing, provider-backed direct ISO browsing, RAW partition-table inspection, floppy geometry/BPB inspection and optical track-layout inspection are read-only-first. Native mount defaults to read-only. Copy out and drag-out are explicit copy operations; drag-out never advertises Move. Direct ISO browsing never mounts the image and refuses silent overwrite conflicts. RAW parsing validates partition metadata against the image boundary. Floppy inspection validates known capacity/geometry and rejects inconsistent BPB metadata. BIN/CUE inspection contains referenced payloads to the CUE directory and refuses ambiguous mixed-sector offsets rather than guessing them. MDF/MDS inspection validates every descriptor and payload range, confines footer paths to the descriptor directory, uses explicit byte offsets for mixed-sector CD layouts and rejects DVD-style media until separately supported. NRG inspection bounds every metadata chunk and DAO track range, decodes v1/v2 cue positions according to their actual on-disk representation, requires `END!`, and rejects inconsistent cue/DAO metadata rather than guessing. These providers do not expose filesystem browsing until those capabilities are real and tested. Provider failures are isolated and do not silently turn into unsupported UI capabilities. Local history/workspace metadata never controls or substitutes for real Windows mount state. Create/convert and future destructive physical-media operations remain isolated behind explicit services and will require target validation and clear confirmation before execution.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
