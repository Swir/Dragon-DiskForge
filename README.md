# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current development version — 0.5.0-alpha.1

## Project progress — 50% toward 1.0

`██████████░░░░░░░░░░ 50%`

**Overall completion:** **50%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **100%** ✅
- `0.5 Partitions + File Systems + Image Intelligence` — **~55%** 🚧
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

Three substantial 0.5 slices are now implemented and validated.

### Cross-provider partition intelligence ✅

- provider-agnostic `PartitionIntelligenceService`
- resolves metadata through `ProviderRegistry` + truthful `PartitionTable`
- stable structural findings and severity levels
- duplicate-index, zero-length, LBA-overflow, geometry, bounds and overlap checks
- capability-driven tests prove the service is not hard-wired to RAW/IMG
- PR #28 / run #232 passed the docs-synchronized full Windows regression/build/artifact path ✅

### Bounded filesystem recognition foundation ✅

- provider-integrated `FileSystemRecognitionService`
- whole-file recognition only where physical-byte mapping is truthful
- partition-scoped recognition only after structural partition validation
- FAT12/FAT16/FAT32, exFAT, supported NTFS boot metadata and ext2/ext3/ext4 recognition
- ISO9660/Joliet descriptor recognition and UDF Volume Recognition Sequence detection
- generated sparse fixtures, false-positive checks and cancellation coverage
- no fake guest-sector access inside sparse/compressed VMDK, QCOW/QCOW2, DMG or container formats
- PR #29 / run #235 completed the docs-synchronized full Windows regression/build/artifact path ✅

See [`docs/FILESYSTEM-RECOGNITION.md`](docs/FILESYSTEM-RECOGNITION.md).

### Boot + installer intelligence foundation ✅

- new `BootInstallerIntelligenceService` with explicit evidence models
- bounded El Torito boot-record/catalog parsing and validation
- validation-entry checksum, section headers, boot indicators and physical load ranges are checked before use
- BIOS (`0x00`) and UEFI (`0xEF`) boot support is reported only from real boot-catalog entries
- installer evidence is collected only through a truthful provider-backed Direct Browse path
- traversal is bounded by directory, entry and depth limits and ignores reparse-point evidence
- Windows install media recognition requires `setup.exe`, `sources/boot.wim` and a real install payload (`install.wim`, `install.esd` or `install.swm`)
- Linux evidence covers casper, Debian-style and Anaconda-style installer/live layouts
- EFI fallback filenames provide bounded architecture hints for x86, x86_64, ARM, ARM64 and RISC-V 64
- filesystem marker files never fabricate bootability
- PR #30 / run #240 passed the new intelligence gate plus all prior providers, Explorer/native Windows integration, Release x64 build and artifact publication before documentation synchronization ✅

See [`docs/BOOT-INSTALLER-INTELLIGENCE.md`](docs/BOOT-INSTALLER-INTELLIGENCE.md).

### Remaining 0.5 work

- richer ISO9660/UDF metadata and UDF traversal where proven
- FAT/FAT32/exFAT reader depth beyond recognition
- deeper supported NTFS metadata
- cross-source architecture, labels and UUID/GUID aggregation beyond current bounded hints
- health/corruption warnings backed by real metadata validation
- virtual guest-sector reader paths before filesystems inside sparse/compressed virtual disks can be analyzed

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
- provider-agnostic partition, filesystem and boot/install intelligence services

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

CI validates Core, provider-registry invariants, partition intelligence, filesystem recognition, boot/install intelligence, all proven image providers, Explorer safety, direct ISO integration, native ISO/VHD/VHDX integration and a full Windows x64 Release build. Green runs publish `DragonDiskForge-win-x64`.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

Partition intelligence reports structural layout findings only. Filesystem recognition reports bounded evidence only. Boot/install intelligence reports bootability only from validated boot metadata and installer families only from bounded file evidence. These services do not repair, execute, mount or mutate images.

VMDK, QCOW and DMG expose only proven metadata. WIM/ESD and FFU expose bounded container metadata only. FFU does not interpret write-descriptor destinations, access physical devices, write sectors or apply images.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
