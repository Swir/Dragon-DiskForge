# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current version — 0.4.0-alpha.1

## Project progress — 43% toward 1.0

`█████████░░░░░░░░░░░ 43%`

**Overall completion:** **43%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **~95%** 🚧
- `0.5 → 1.0` — planned / future milestones

> The progress indicator changes only after meaningful implementation and validation checkpoints. CI count alone never increases completion.

## Proven product foundation

- WinUI 3 / .NET 10 desktop shell with Dragon visual identity
- native Windows read-only-first ISO/VHD/VHDX Mount + Unmount
- mounted-volume Dragon Explorer with search, Preview and safe Copy out
- Recent Images, Favorites, mount history and multi-image workspace
- safe Copy-only drag-out to Windows Explorer/Desktop
- managed ISO9660/Joliet direct browsing without mounting
- shared Core SHA-256 verification with progress/cancellation

## 0.4 Extended Image Providers 🚧

The provider foundation and **eleven additional image families** are now implemented and proven.

### Provider architecture

- central Core `ProviderRegistry`
- explicit truthful capabilities
- deterministic priority/extension-first resolution
- provider/signature fallback
- probe and inspection failure isolation
- cancellation as a hard stop
- diagnostics and duplicate-ID protection

### Proven provider slices

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

### FFU metadata provider ✅

- dedicated `IFfuMetadataProvider` + `FfuMetadataInfo`
- truthful `ContainerMetadata` capability
- validates the common 32-byte `SignedImage ` security header
- validates the 24-byte `ImageFlash ` + NUL image header
- supports the proven SHA-256 metadata algorithm id `0x0000800C`
- validates chunk size, catalog/hash-table region, manifest region and chunk alignment
- parses the common 248-byte store header metadata used by the proven slice
- reads bounded PlatformID, block size, write-descriptor count/length and validation-descriptor count/length
- all declared metadata regions are bounded against the physical FFU file
- base Core signature detection recognizes `SignedImage `
- write-descriptor locations are not interpreted and payload chunks are never mapped to physical media
- Direct Browse, Mount, Convert, device access, sector writing and image application remain disabled
- PR #26 / run #225 passed FFU tests, all previous provider tests, Explorer safety, ISO/native Windows regression, Release x64 build and artifact publication ✅

### Remaining 0.4 gate

**Provider-contract hardening** — tighten registry/descriptor invariants and deterministic provider behavior before any public stability promise. This is hardening of the internal 0.4 contract, not a declaration of a stable public plugin API.

The cross-process human drag gesture and normal-user UAC prompt remain manual QA gates in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md).

The planned first public GitHub beta is **`0.5.0-beta.1`** after required 0.4 provider work and the agreed 0.5 image-intelligence scope are proven. See [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md), with execution state in [`docs/STATUS.md`](docs/STATUS.md) and [`docs/MILESTONES.md`](docs/MILESTONES.md).

## Tech

- C# / .NET 10 / WinUI 3 / Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows native-storage layer
- provider registry with explicit capabilities/fallback/failure isolation
- bounded read-only partition, floppy, optical-layout, virtual-disk and container metadata parsers

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

CI validates Core, provider registry, RAW/IMG, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU; Explorer safety; direct ISO integration; native ISO/VHD/VHDX integration; and a full Windows x64 Release build. Green runs publish `DragonDiskForge-win-x64`.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

VMDK, QCOW and DMG expose only proven metadata. WIM/ESD and FFU expose bounded container metadata only. FFU does not interpret write-descriptor destinations, access physical devices, write sectors or apply images.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
