# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current version — 0.4.0-alpha.1

## Project progress — 42% toward 1.0

`████████░░░░░░░░░░░░ 42%`

**Overall completion:** **42%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **~89%** 🚧
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

The provider foundation and **ten additional image families** are now implemented and proven.

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

### WIM / ESD metadata provider ✅

- dedicated `IWimMetadataProvider` + `WimMetadataInfo`
- introduces the truthful `ContainerMetadata` capability instead of mislabeling WIM as a virtual disk
- validates the 208-byte little-endian `MSWIM\0\0\0` header
- supports standard WIM version `68864` and solid/ESD version `3584` metadata
- parses flags, chunk size, GUID, part number, total parts, image count and boot index
- parses lookup-table, XML, boot-metadata and integrity resource descriptors
- resource stored size uses the lower 56 bits while resource flags use the upper byte
- all resource offsets/ranges are bounded against the physical container before use
- standalone Part 1/1 images are supported; split/spanned WIM is rejected until companion-part handling exists
- `WRITE_IN_PROGRESS`, unknown flags, invalid chunk geometry, invalid BootIndex and out-of-file resources are rejected rather than guessed
- resource decompression, XML/file-tree parsing, extraction, encrypted ESD handling, Direct Browse, Mount and Convert remain disabled
- PR #25 / run #222 passed WIM/ESD tests, all previous provider tests, Explorer safety, ISO/native Windows regression, Release x64 build and artifact publication ✅

### Next 0.4 provider

**FFU** — bounded read-only Full Flash Update metadata inspection first. Payload writing, physical-device flashing and destructive operations stay disabled until separately implemented and tested.

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

CI validates Core, provider registry, RAW/IMG, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF and WIM/ESD; Explorer safety; direct ISO integration; native ISO/VHD/VHDX integration; and a full Windows x64 Release build. Green runs publish `DragonDiskForge-win-x64`.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

VMDK, QCOW and DMG expose only proven metadata. WIM/ESD exposes container metadata only; it does not decompress resources, traverse image file trees, extract payloads, browse directly, mount or convert them.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
