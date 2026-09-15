# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting and verifying disk-image formats from one interface. The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** identity and follows one strict rule: unsupported actions stay disabled until their engine path exists and is tested.

## Current version — 0.3.0

### 0.1 Foundation + Dragon Visual Identity ✅

- WinUI 3 / .NET 10 desktop shell
- `DragonDiskForge.Core` separated from the GUI
- drag & drop and file picker
- broad image-format catalogue and signature detection
- shared Core SHA-256 verification with progress/cancellation
- Dragon Forge UI, responsive layout, light/dark/High Contrast resources and Windows icon
- Core smoke tests and Windows x64 CI

### 0.2 Native Mount + Unmount ✅

- native Windows mount/unmount for **ISO, VHD and VHDX**
- read-only-first mount behavior
- drive-letter/attached-state detection
- progress/cancellation and stale-state recovery
- live **Mounted** dashboard backed by Windows state
- friendly native-operation errors
- disposable VHD/VHDX and IMAPI ISO integration tests

### 0.3 Dragon Explorer ✅

Dragon Explorer now has both mounted-volume and provider-backed read-only workflows.

**Mounted-volume Explorer**

- browse mounted ISO/VHD/VHDX volumes
- folders-first navigation, breadcrumbs and metadata
- recursive search with cancellation
- safe **Copy out** with overwrite and reparse-point protection
- read-only text/image Preview plus PDF/media metadata modes
- trust warning before shell-opening executable/script content

**Image Library + workspace**

- local Recent Images + Favorites with atomic JSON persistence
- Mounted history kept separate from live Windows state
- multi-image WinUI tab workspace
- duplicate-tab prevention and stale-tab pruning
- closing a tab never unmounts an image

**Safe drag-out**

- mounted files/folders can be dragged to Windows Explorer/Desktop
- transfer is **Copy-only**, never Move
- mounted-root containment, existence and reparse attributes are revalidated immediately before transfer
- dedicated safety smoke tests plus manual cross-process gesture QA

**Direct ISO browsing without mounting**

- `IDirectBrowseProvider` + `IDirectImageExplorer` Core contracts
- tested ISO9660/Joliet provider
- virtual list/navigation/search while the ISO remains detached
- Joliet Unicode filenames
- safe file/folder **Copy out** with cancellation and overwrite protection
- virtual parent-traversal protection
- dedicated direct-provider tabs coexist with mounted tabs
- shell Open, Preview and drag-out intentionally remain disabled for virtual provider entries until materialized as real Windows files
- real IMAPI integration proves browse/search/extract with `Get-DiskImage` still reporting `Attached=False`

The feature implementation passed PR #11 / GitHub Actions run #115 through direct-provider integration, the existing ISO/VHD/VHDX mount regression, restore, full WinUI `Release|x64` build and artifact publication. The final `0.3.0` docs/version head and post-merge main regression remain the formal closure gates.

Manual desktop cases that hosted CI cannot faithfully emulate remain in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md), including normal-user UAC prompts and the human cross-process drag gesture.

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md). Execution work is tracked with GitHub Issues and pull requests.

Major milestones:

`0.1 Foundation + Dragon UI ✅` → `0.2 Native Mount ✅` → `0.3 Dragon Explorer ✅` → `0.4 Extended Providers` → `0.5 Filesystems + Image Intelligence` → `0.6 Create/Convert/Verify` → `0.7 Bootable USB` → `0.8 Windows Integration` → `0.9 Beta Hardening` → `1.0 Production`

## Tech

- C#
- .NET 10
- WinUI 3
- Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows service layer for native Storage operations
- capability-driven direct-browse providers

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

Automated validation currently runs Core smoke tests, mounted-history tests, drag-out safety tests, real direct ISO browse integration, real Windows ISO/VHD/VHDX mount/Explorer/Preview integration, full Windows x64 Release build and artifact publication.

Green CI runs publish a temporary `DragonDiskForge-win-x64` artifact for desktop/manual validation.

## Safety design

Inspection, hashing, Preview, mounted browsing and direct-provider browsing are read-only-first. Native mount defaults to read-only. Copy out and drag-out are explicit copy operations; drag-out never advertises Move. Local metadata never controls or substitutes for real Windows mount state. Direct-provider virtual entries never masquerade as shell files.

Future create/convert and destructive physical-media operations remain isolated behind explicit services and will require target validation and clear confirmation before execution.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
