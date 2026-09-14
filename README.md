# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Milestone 0.1 — Foundation + Dragon Visual Identity ✅

The foundation milestone is complete and validated on Windows x64 CI.

Implemented and tested:

- WinUI 3 / .NET 10 desktop shell
- `DragonDiskForge.Core` separated from the GUI
- drag & drop and file picker
- image-format catalogue for ISO, IMG/IMA/RAW, BIN/CUE, MDF/MDS, NRG, CCD/SUB, VHD, VHDX, VMDK, QCOW/QCOW2, DMG, WIM/ESD and FFU families
- signature detection for ISO, VHD, VHDX, QCOW2, DMG and WIM/ESD
- shared Core SHA-256 verification with live progress and cancellation
- Core smoke tests for signatures, extension fallback, missing files, hashing progress and cancellation
- read-only-first inspection architecture
- Dragon Forge dashboard, startup overlay, subtle scale geometry and reveal animations
- responsive compact/wide desktop layout
- dark Dragon theme plus completed light and system High Contrast resources
- final Windows application `.ico` used by the executable build
- GitHub Actions Windows x64 restore/build validation
- future navigation and actions remain disabled until their underlying engines are real

Mount, Explorer, Convert and other later modules are intentionally locked. Dragon DiskForge does not present roadmap placeholders as finished features.

## Next milestone — 0.2 Native Mount + Unmount

The next development stage is the real Windows mount engine for ISO/VHD/VHDX, including unmount/eject, mount-state detection, read-only behavior, UAC only when required, progress/cancellation, friendly error handling and stale-state recovery.

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md). Execution work is tracked with GitHub Issues.

Major milestones:

`0.1 Foundation + Dragon UI ✅` → `0.2 Native Mount` → `0.3 Dragon Explorer` → `0.4 Extended Providers` → `0.5 Filesystems + Image Intelligence` → `0.6 Create/Convert/Verify` → `0.7 Bootable USB` → `0.8 Windows Integration` → `0.9 Beta Hardening` → `1.0 Production`

The visual specification lives in [`docs/DRAGON-DESIGN.md`](docs/DRAGON-DESIGN.md), and the vector sigil is stored under [`docs/branding/dragon-sigil.svg`](docs/branding/dragon-sigil.svg).

## Tech

- C#
- .NET 10
- WinUI 3
- Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

Automated validation also runs Core smoke tests and a full Windows x64 Release build in GitHub Actions.

## Safety design

Inspection and hashing never write to the image. Mount/create/convert operations are isolated behind explicit services. Destructive physical-media operations will require target validation and clear confirmation before execution.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
