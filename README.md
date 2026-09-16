# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current development version — 0.5.0-alpha.1

## Project progress — 45% toward 1.0

`█████████░░░░░░░░░░░ 45%`

**Overall completion:** **45%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **100%** ✅
- `0.5 Partitions + File Systems + Image Intelligence` — **~11%** 🚧
- `0.6 → 1.0` — planned / future milestones

> The progress indicator changes only after meaningful implementation and validation checkpoints. CI count alone never increases completion.

## Proven product foundation

- WinUI 3 / .NET 10 desktop shell with Dragon visual identity
- native Windows read-only-first ISO/VHD/VHDX Mount + Unmount
- mounted-volume Dragon Explorer with search, Preview and safe Copy out
- Recent Images, Favorites, mount history and multi-image workspace
- safe Copy-only drag-out to Windows Explorer/Desktop
- managed ISO9660/Joliet direct browsing without mounting
- shared Core SHA-256 verification with progress/cancellation
- hardened provider registry with truthful capabilities and deterministic resolution

## 0.5 Partitions + File Systems + Image Intelligence 🚧

The first 0.5 slice is implemented and validated: **cross-provider partition intelligence** built on the existing `PartitionTable` capability.

### Cross-provider partition intelligence ✅

- provider-agnostic `PartitionIntelligenceService`
- resolves partition metadata through `ProviderRegistry` and the truthful `PartitionTable` capability
- preserves table type, sector size, partition count and bootable-partition count
- structural findings with stable codes and severities
- detects duplicate partition indexes
- detects zero-length entries
- detects start/end LBA arithmetic overflow
- validates byte offsets and sizes against the provider-reported LBA geometry
- detects partition ranges that exceed the physical image
- detects overlapping partition byte ranges
- fake capability-driven providers in smoke tests prove the service is not hard-wired to RAW/IMG
- read-only analysis only: no mount, no writes and no filesystem-health claims
- PR #28 / run #231 passed partition-intelligence tests plus the complete existing provider, Explorer, native Windows, Release x64 and artifact regression path before documentation synchronization ✅

### Next 0.5 engineering slices

Filesystem recognition comes next, beginning with bounded signatures/metadata and continuing to richer filesystem intelligence only after the underlying reader paths are proven. Planned scope includes ISO9660/UDF, FAT/FAT32/exFAT, NTFS metadata where supported, ext-family recognition, boot/install recognition, architecture/labels/UUIDs and corruption warnings.

## 0.4 Extended Image Providers — COMPLETE ✅

The provider foundation, **eleven additional image families**, and the provider-contract hardening gate are implemented and proven.

- **IMG / RAW** ✅ — MBR, bounded EBR and GPT inspection; `PartitionTable`
- **IMA / floppy** ✅ — standard geometry + FAT-style BPB validation; `MediaGeometry`
- **BIN / CUE** ✅ — bounded BINARY CUE AUDIO/MODE1/MODE2 track layout
- **MDF / MDS CD** ✅ — bounded descriptor/session/track parsing with explicit MDF byte offsets
- **NRG v1/v2** ✅ — NERO/NER5, CUES/CUEX and DAOI/DAOX track metadata
- **CCD / IMG / SUB** ✅ — CloneCD MODE/INDEX parsing, 2352-byte IMG alignment and optional 96-byte SUB validation
- **VMDK sparse v1** ✅ — VMware hosted sparse header + bounded embedded descriptor metadata
- **QCOW / QCOW2** ✅ — QCOW v1 and QCOW2 v2/v3 big-endian container metadata
- **DMG / UDIF** ✅ — bounded `koly` trailer and XML plist metadata
- **WIM / ESD** ✅ — bounded 208-byte header and resource metadata; `ContainerMetadata`
- **FFU** ✅ — bounded common Full Flash Update security/image/store metadata; `ContainerMetadata`

The cross-process human drag gesture and normal-user UAC prompt remain manual QA gates in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md).

The planned first public GitHub beta is **`0.5.0-beta.1`** after the agreed 0.5 image-intelligence scope is proven. See [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md), with execution state in [`docs/STATUS.md`](docs/STATUS.md) and [`docs/MILESTONES.md`](docs/MILESTONES.md).

## Tech

- C# / .NET 10 / WinUI 3 / Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows native-storage layer
- hardened provider registry with explicit capabilities/fallback/failure isolation
- bounded read-only partition, floppy, optical-layout, virtual-disk and container metadata parsers
- provider-agnostic partition intelligence layered over `PartitionTable`

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

CI validates Core, hardened provider-registry invariants, partition intelligence, RAW/IMG, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU; Explorer safety; direct ISO integration; native ISO/VHD/VHDX integration; and a full Windows x64 Release build. Green runs publish `DragonDiskForge-win-x64`.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

Partition intelligence reports structural metadata inconsistencies only. It does not claim filesystem health, repair partitions, mount images or alter bytes.

VMDK, QCOW and DMG expose only proven metadata. WIM/ESD and FFU expose bounded container metadata only. FFU does not interpret write-descriptor destinations, access physical devices, write sectors or apply images.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
