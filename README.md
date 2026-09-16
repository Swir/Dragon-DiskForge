# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current version — 0.4.0-alpha.2

## Project progress — 32% toward 1.0

`██████░░░░░░░░░░░░░░ 32%`

**Overall completion:** **32%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **~20%** 🚧
- `0.5 → 1.0` — planned / future milestones

> This progress indicator is updated together with the roadmap, changelog and milestone status after meaningful project checkpoints. The percentage reflects completed roadmap slices and proven functionality, not CI count alone.

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

Dragon Explorer is complete for the 0.3 scope and includes:

- mounted-volume Explorer for ISO/VHD/VHDX
- navigation, metadata, recursive search and safe Copy out
- Preview + Recent Images + Favorites
- mounted history kept separate from live Windows state
- multi-image WinUI tab workspace
- safe Copy-only drag-out to Windows Explorer/Desktop
- managed ISO9660/Joliet direct browsing without mounting
- direct list/navigation/search/Copy out with cancellation and overwrite/path/extent safety
- real IMAPI-generated ISO integration proving direct browsing leaves the image detached

### 0.4 Extended Image Providers 🚧

The provider foundation and the first new format family are now proven.

**Provider foundation — complete ✅**

- central Core `ProviderRegistry`
- explicit provider capability reporting
- deterministic priority and extension-first selection
- provider/signature fallback
- probe-failure and inspection-failure isolation
- cancellation remains a hard stop rather than being swallowed as a parser error
- provider diagnostics for future UI/CLI reporting
- duplicate provider-ID protection
- ISO9660/Joliet direct browsing resolved through the registry rather than hardcoded `.iso` UI logic
- dedicated provider-registry smoke tests
- PR #14 / run #146 full Windows regression + WinUI Release x64 + artifact ✅

**IMG / RAW provider — complete for 0.4 scope ✅**

- read-only `.img` / `.raw` provider
- minimum size and 512-byte sector-alignment validation
- structured-signature guard preventing renamed ISO/VHD/VHDX/QCOW2/WIM/DMG images from being misclaimed as RAW
- read-only inspection with source-byte SHA-256 before/after verification in tests
- registry fallback proving a valid ISO renamed to `.img` is rejected by RAW and picked up by the ISO provider
- cancellation coverage
- Browse/Mount/Convert intentionally remain disabled until real backends exist
- PR #15 / run #151 full Windows regression + WinUI Release x64 + artifact ✅

Next 0.4 work: **IMA / floppy images**. No format-specific capability is enabled before its real provider path and tests exist.

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

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

Automated validation runs Core smoke tests, provider-registry smoke tests, IMG/RAW provider smoke tests, mounted-history smoke tests, drag-out safety smoke tests, real ISO direct-browse integration, native Windows ISO/VHD/VHDX integration and a full Windows x64 Release build in GitHub Actions.

Green CI runs publish a `DragonDiskForge-win-x64` artifact for desktop/manual validation.

## Safety design

Inspection, hashing, preview, mounted-volume browsing and provider-backed direct ISO browsing are read-only-first. Native mount defaults to read-only. Copy out and drag-out are explicit copy operations; drag-out never advertises Move. Direct ISO browsing never mounts the image and refuses silent overwrite conflicts. Provider failures are isolated and do not silently turn into unsupported UI capabilities. IMG/RAW inspection is read-only and does not pretend to expose partitions or filesystems before milestone 0.5 implements those layers. Local history/workspace metadata never controls or substitutes for real Windows mount state. Create/convert and future destructive physical-media operations remain isolated behind explicit services and will require target validation and clear confirmation before execution.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
