# Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern WinUI 3 application for inspecting, mounting, exploring, verifying and eventually converting disk-image formats from one interface.

## Current milestone: 0.1 Foundation

Already present in this starter build:

- modern WinUI 3 shell
- drag & drop and file picker
- core engine separated from the GUI
- extension catalogue for ISO, IMG/IMA/RAW, BIN/CUE, MDF/MDS, NRG, CCD/SUB, VHD, VHDX, VMDK, QCOW/QCOW2, DMG, WIM/ESD and FFU
- signature detection for ISO, VHD, VHDX, QCOW2, DMG and WIM/ESD
- SHA-256 verification
- read-only inspection path

Mounting and the internal image explorer are intentionally scheduled for milestone 0.2 rather than being faked in the UI.

## Tech

- C#
- .NET 10
- WinUI 3
- Windows App SDK 2.4 stable line
- self-contained unpackaged development build initially; installer/MSIX is planned for release milestones

## Build on Windows

Requirements:

- Windows 10 1809+ (Windows 11 recommended)
- Visual Studio 2026 with WinUI application development workload
- .NET 10 SDK
- Developer Mode recommended

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

## Safety design

Inspection and hashing never write to the image. Mount/create/convert operations will be isolated behind explicit services and destructive USB/image actions will require clear confirmation.

See `docs/ROADMAP.md` for the development plan.
