# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current version — 0.3.0-alpha.2

## Project progress — 30% toward 1.0

`██████░░░░░░░░░░░░░░ 30%`

**Overall completion:** **30%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **~98%** 🚧
- `0.4 → 1.0` — planned / future milestones

> This progress indicator is updated together with the roadmap, changelog and milestone status after meaningful project checkpoints.

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

### 0.3 Dragon Explorer 🚧

The current 0.3 alpha contains four proven slices.

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
- dedicated mounted-history smoke tests

**Safe drag-out to Windows Explorer**

- mounted Explorer files/folders can be dragged directly to Windows Explorer/Desktop as **Copy-only** storage items
- the source is revalidated against the mounted root immediately before transfer
- stale sources, path escapes, reparse points and junctions are blocked
- runtime filesystem attributes are rechecked so stale UI state cannot bypass the safety gate
- drag data is prepared asynchronously through the WinUI `DragStarting` deferral
- dedicated drag-out safety smoke tests run in CI
- PR #10 / GitHub Actions run #103 passed Core, mounted-history, drag-out safety, real Windows mount/Explorer/Preview integration, restore, full WinUI `Release|x64` build and Windows artifact publishing before this documentation pass

Remaining 0.3 scope:

- provider-backed direct browsing without mounting where technically supported
- final 0.3 regression/documentation closure

The cross-process human drag gesture itself is also retained in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md), because GitHub Actions cannot reliably emulate a person dragging an item into Windows Explorer.

The interactive UAC prompt itself cannot be faithfully exercised on GitHub-hosted administrator runners. A normal-user desktop checklist is maintained in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md) and remains a manual QA gate before public beta packaging.

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md). Execution work is tracked with GitHub Issues and pull requests.

Major milestones:

`0.1 Foundation + Dragon UI ✅` → `0.2 Native Mount ✅` → `0.3 Dragon Explorer 🚧` → `0.4 Extended Providers` → `0.5 Filesystems + Image Intelligence` → `0.6 Create/Convert/Verify` → `0.7 Bootable USB` → `0.8 Windows Integration` → `0.9 Beta Hardening` → `1.0 Production`

The visual specification lives in [`docs/DRAGON-DESIGN.md`](docs/DRAGON-DESIGN.md), and the vector sigil is stored under [`docs/branding/dragon-sigil.svg`](docs/branding/dragon-sigil.svg).

## Tech

- C#
- .NET 10
- WinUI 3
- Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows service layer for native Storage operations

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

Automated validation runs Core smoke tests, mounted-history smoke tests, drag-out safety smoke tests, real Windows ISO/VHD/VHDX mount integration, Explorer/Preview integration against a mounted ISO and a full Windows x64 Release build in GitHub Actions.

Green CI runs publish a `DragonDiskForge-win-x64` artifact for desktop/manual validation.

## Safety design

Inspection, hashing, preview and mounted-volume browsing never write to the image. Native mount defaults to read-only. Copy out and drag-out are explicit copy operations; drag-out never advertises Move. Local history/workspace metadata never controls or substitutes for real Windows mount state. Create/convert and future destructive physical-media operations remain isolated behind explicit services and will require target validation and clear confirmation before execution.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
