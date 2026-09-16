# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current development version — 0.5.0-alpha.1

## Project progress — 47% toward 1.0

`█████████░░░░░░░░░░░ 47%`

**Overall completion:** **47%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **100%** ✅
- `0.5 Partitions + File Systems + Image Intelligence` — **~30%** 🚧
- `0.6 → 1.0` — planned / future milestones

> Progress changes only after meaningful implementation and validation checkpoints. CI count alone never increases completion.

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

Two substantial 0.5 slices are now implemented and validated.

### Cross-provider partition intelligence ✅

- provider-agnostic `PartitionIntelligenceService`
- resolves metadata through `ProviderRegistry` + truthful `PartitionTable`
- stable structural findings and severity levels
- duplicate-index, zero-length, LBA-overflow, geometry, bounds and overlap checks
- fake capability-driven providers prove the service is not hard-wired to RAW/IMG
- no writes, repairs, mount side effects or fake filesystem-health claims
- PR #28 / run #232 passed the docs-synchronized full Windows regression/build/artifact path ✅

### Bounded filesystem recognition foundation ✅

- provider-integrated `FileSystemRecognitionService`
- whole-file recognition for providers whose image bytes map directly to the physical file
- partition-scoped recognition only after `PartitionIntelligenceService` validates provider-reported physical partition ranges
- FAT12 / FAT16 / FAT32 classification from bounded BPB geometry and cluster counts
- exFAT boot metadata, serial and sector/cluster geometry
- NTFS boot metadata, volume serial and cluster geometry
- ext2 / ext3 / ext4 recognition from superblock magic and feature flags, plus label/UUID/block size
- ISO9660 primary descriptor recognition with Joliet variant/label metadata
- UDF Volume Recognition Sequence (`BEA01` → `NSR02/NSR03` → `TEA01`)
- blank recognized images remain “no filesystem detected” rather than receiving a guessed result
- structurally invalid partition layouts are rejected before probing filesystem bytes
- pre-cancelled analysis remains a hard stop
- generated sparse fixtures keep large binary test images out of Git
- PR #29 / run #234 passed the filesystem gate plus every prior provider, Explorer/native Windows, Release x64 and artifact check before documentation synchronization ✅

This slice deliberately does **not** translate guest sectors inside sparse/compressed VMDK, QCOW/QCOW2, DMG or other virtual/container formats. Those formats keep only their already-proven metadata capabilities until real virtual-sector readers exist.

See [`docs/FILESYSTEM-RECOGNITION.md`](docs/FILESYSTEM-RECOGNITION.md) for the recognition contract and safety boundaries.

### Remaining 0.5 work

- richer ISO9660/UDF metadata and UDF traversal where proven
- FAT/FAT32/exFAT reader depth beyond recognition
- deeper supported NTFS metadata
- bootability + BIOS/UEFI intelligence
- Windows/Linux installer recognition
- architecture, labels and UUID/GUID aggregation
- health/corruption warnings backed by real metadata validation

## 0.4 Extended Image Providers — COMPLETE ✅

The provider foundation, **eleven additional image families**, and provider-contract hardening are proven: IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU.

The cross-process human drag gesture and normal-user UAC prompt remain manual QA gates in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md).

The planned first public GitHub beta remains **`0.5.0-beta.1`** after the agreed 0.5 image-intelligence scope is proven. See [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md), with execution state in [`docs/STATUS.md`](docs/STATUS.md) and [`docs/MILESTONES.md`](docs/MILESTONES.md).

## Tech

- C# / .NET 10 / WinUI 3 / Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows native-storage layer
- hardened provider registry with explicit capabilities/fallback/failure isolation
- bounded read-only partition, filesystem, optical-layout, virtual-disk and container metadata parsers
- provider-agnostic partition and filesystem intelligence services

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

CI validates Core, provider-registry invariants, partition intelligence, filesystem recognition, all proven image providers, Explorer safety, direct ISO integration, native ISO/VHD/VHDX integration and a full Windows x64 Release build. Green runs publish `DragonDiskForge-win-x64`.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

Partition intelligence reports structural layout findings only. Filesystem recognition reports bounded evidence only. Neither service repairs, mounts or mutates images.

VMDK, QCOW and DMG expose only proven metadata. WIM/ESD and FFU expose bounded container metadata only. FFU does not interpret write-descriptor destinations, access physical devices, write sectors or apply images.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
