# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current version — 0.2.0

### 0.1 Foundation + Dragon Visual Identity ✅

- WinUI 3 / .NET 10 desktop shell
- `DragonDiskForge.Core` separated from the GUI
- drag & drop and file picker
- broad image-format catalogue
- signature detection for ISO, VHD, VHDX, QCOW2, DMG and WIM/ESD
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
- Refresh, Open drive and Unmount/Cancel actions in the Mounted view
- stale-state recovery by re-querying Windows
- friendly unsupported-format/native-operation error translation
- elevation policy for VHD/VHDX isolated to the native operation that needs it
- real Windows integration tests using disposable VHD/VHDX and IMAPI-generated ISO images
- cancellation-safety validation

### 0.3 Dragon Explorer 🚧

The first mounted-volume Explorer slice is already implemented and proven:

- real `IExplorerService` contract in Core
- in-app browsing of mounted ISO/VHD/VHDX volumes
- folders-first file listing with size and modified-time metadata
- Up navigation and address/breadcrumb display
- recursive search with cancellation and result limits
- safe **Copy out** to a user-selected destination
- non-destructive extraction behavior: existing destination names are never silently overwritten
- reparse-point/junction traversal blocked for recursive search and Copy out
- warning before opening executable/script content from an image
- Mounted dashboard → Dragon Explorer routing using current Windows state
- real Windows integration test: mounted ISO → list → search → Copy out → verify content
- full Windows x64 Release CI green on `main` through run #58

Remaining 0.3 work includes previews, recent images, favorites, mounted history, multi-image workspace/tabs, drag-out where technically safe and provider-backed direct browsing where supported.

The interactive UAC prompt itself cannot be faithfully exercised on GitHub-hosted administrator runners. A normal-user desktop checklist is maintained in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md) and remains a manual QA gate before public beta packaging.

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md). Execution work is tracked with GitHub Issues.

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

Automated validation runs Core smoke tests, real Windows ISO/VHD/VHDX mount integration tests, Explorer integration against a mounted ISO and a full Windows x64 Release build in GitHub Actions.

## Safety design

Inspection, hashing and mounted-volume browsing never write to the image. Native mount defaults to read-only. Copy out writes only to an explicit user-selected destination and refuses silent overwrite conflicts. Create/convert and future destructive physical-media operations are isolated behind explicit services and will require target validation and clear confirmation before execution.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
