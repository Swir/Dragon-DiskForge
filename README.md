# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, extracting, verifying, creating and converting disk-image formats from one interface.

The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity. It is designed as a real disk-image tool first: unsupported actions stay disabled until their engine capability is implemented and tested.

## Current version — 0.4.0-alpha.1

## Project progress — 41% toward 1.0

`████████░░░░░░░░░░░░ 41%`

**Overall completion:** **41%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **~83%** 🚧
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

The provider foundation and **nine additional image families** are now implemented and proven.

### Provider architecture

- central Core `ProviderRegistry`
- explicit capabilities
- deterministic priority/extension-first resolution
- provider/signature fallback
- probe and inspection failure isolation
- cancellation as a hard stop
- diagnostics and duplicate-ID protection

### Proven provider slices

- **IMG / RAW** ✅ — MBR, bounded EBR and GPT inspection; `PartitionTable` capability
- **IMA / floppy** ✅ — standard geometry + FAT-style BPB validation; `MediaGeometry` capability
- **BIN / CUE** ✅ — bounded BINARY CUE AUDIO/MODE1/MODE2 track layout
- **MDF / MDS CD** ✅ — bounded descriptor/session/track parsing with explicit MDF byte offsets
- **NRG v1/v2** ✅ — NERO/NER5, CUES/CUEX and DAOI/DAOX track metadata
- **CCD / IMG / SUB** ✅ — CloneCD MODE/INDEX parsing, 2352-byte IMG alignment and optional 96-byte SUB validation
- **VMDK sparse v1** ✅ — VMware hosted sparse header + bounded embedded descriptor metadata
- **QCOW / QCOW2** ✅ — QCOW v1 and QCOW2 v2/v3 big-endian container metadata
- **DMG / UDIF** ✅ — bounded `koly` trailer and XML plist metadata

### DMG / UDIF metadata provider ✅

- dedicated `IDmgMetadataProvider` + `DmgMetadataInfo`
- reports the shared `VirtualDiskMetadata` capability
- validates the trailing 512-byte big-endian `koly` trailer and UDIF version 4/header size
- parses flags, running/data/resource fork metadata, segment metadata, checksum metadata, XML plist location, image variant and sector count
- physical data/resource/XML ranges are bounded before reads and cannot overlap the final trailer
- sector count provides the logical 512-byte-sector virtual size
- single-file UDIF images are supported; multi-segment images are explicitly rejected until companion-segment handling exists
- XML plist reads are bounded to 16 MiB with DTD and external entity resolution disabled
- `blkx` dictionary entries are counted without decoding `mish` block maps or touching compressed partition data
- malformed XML, invalid trailer/ranges, unsafe checksum-size metadata and foreign extensions are rejected
- block-map decompression, guest-sector translation, filesystem browsing, Mount and Convert remain disabled
- PR #24 / run #214 passed DMG tests, all previous provider tests, ISO/native Windows regression, Release x64 build and artifact publication ✅

### Next 0.4 provider

**WIM / ESD** — bounded read-only WIM container/header/resource metadata inspection first. File extraction, resource decompression and encrypted ESD handling stay disabled until independently implemented and tested.

The cross-process human drag gesture and normal-user UAC prompt remain manual QA gates in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md).

The planned first public GitHub beta is **`0.5.0-beta.1`** after required 0.4 provider work and the agreed 0.5 image-intelligence scope are proven. See [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md), with execution state in [`docs/STATUS.md`](docs/STATUS.md) and [`docs/MILESTONES.md`](docs/MILESTONES.md).

## Tech

- C# / .NET 10 / WinUI 3 / Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows native-storage layer
- provider registry with explicit capabilities/fallback/failure isolation
- bounded read-only partition, floppy, optical-layout and virtual-disk metadata parsers

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

CI validates Core, provider registry, RAW/IMG, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2 and DMG/UDIF; Explorer safety; direct ISO integration; native ISO/VHD/VHDX integration; and a full Windows x64 Release build. Green runs publish `DragonDiskForge-win-x64`.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

VMDK, QCOW and DMG currently expose only proven metadata. They do **not** translate guest data structures, expose virtual sectors, browse filesystems, mount these formats, or convert them. DMG XML parsing disables DTD/external resolution and does not decode `blkx` block maps yet.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
