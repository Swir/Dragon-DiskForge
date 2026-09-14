# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The goal is not a generic disk utility. Dragon DiskForge has its own **Dragon visual identity**: obsidian/forged-metal surfaces, ember accents, subtle scale geometry and a custom dragon sigil, while retaining Windows-native usability and accessibility.

## Current milestone — 0.1 Foundation + Dragon Visual Identity

Already present:

- WinUI 3 / .NET 10 desktop shell
- `DragonDiskForge.Core` separated from the GUI
- drag & drop and file picker
- initial format catalogue for ISO, IMG/IMA/RAW, BIN/CUE, MDF/MDS, NRG, CCD/SUB, VHD, VHDX, VMDK, QCOW/QCOW2, DMG, WIM/ESD and FFU
- signature detection for ISO, VHD, VHDX, QCOW2, DMG and WIM/ESD
- SHA-256 verification
- read-only-first architecture
- GitHub roadmap and architecture documentation

In progress:

- complete Dragon visual system
- UI polish and responsive states
- error/cancellation states
- foundation testing

Mounting and the internal image explorer are intentionally implemented as real milestone features rather than fake enabled buttons.

## Product plan

The complete development plan is maintained in [`docs/ROADMAP.md`](docs/ROADMAP.md).

Major stages:

1. Foundation + Dragon visual identity
2. Native ISO/VHD/VHDX mount/unmount
3. Dragon Explorer
4. Extended image providers
5. Partition/filesystem intelligence
6. Create/convert/verify
7. Bootable USB and physical-media tools
8. Windows integration + CLI
9. Quality/security/beta hardening
10. Production 1.0

## Dragon design

The visual language is documented in [`docs/DRAGON-DESIGN.md`](docs/DRAGON-DESIGN.md).

Core direction: **obsidian + forged metal + controlled ember heat**. Dragon styling must remain distinctive without reducing clarity, performance or accessibility.

## Tech

- C#
- .NET 10
- WinUI 3
- Windows App SDK
- shared Core engine for GUI and future CLI

## Safety design

Inspection and hashing never write to the image. Destructive media operations will require explicit target identification and confirmation. Read-only inspection is the default wherever possible.

## Project rule

A feature is marked complete only after the underlying operation actually works, failure paths are handled, and the roadmap/changelog are updated.