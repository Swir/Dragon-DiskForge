# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, verifying and analyzing disk-image formats from one interface. It combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** identity and follows one strict rule: unsupported actions stay disabled until their engine path is implemented and tested.

## Current development version — 0.5.0-alpha.1

## Project progress — 58% toward 1.0

`████████████░░░░░░░░ 58%`

**Overall completion:** **58%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **100%** ✅
- `0.5 Partitions + File Systems + Image Intelligence` — **~95%** 🚧
- `0.6 → 1.0` — planned / future milestones

> Progress changes only after meaningful implementation and validation checkpoints. CI count alone never increases completion.

## Proven product foundation

- WinUI 3 / .NET 10 desktop shell with Dragon visual identity and application icon
- native Windows read-only-first ISO/VHD/VHDX Mount + Unmount
- mounted-volume Dragon Explorer with search, Preview and safe Copy out
- Recent Images, Favorites, mount history and multi-image workspace
- safe Copy-only drag-out to Windows Explorer/Desktop
- managed ISO9660/Joliet direct browsing without mounting
- shared Core SHA-256 verification with progress/cancellation
- hardened provider registry with truthful capabilities and deterministic resolution
- bounded read-only partition, filesystem, boot/install and image-intelligence services
- user-facing **Analyze** action with text/JSON reporting and Save JSON
- required **by Swir** + GitHub footer in the Windows UI
- versioned, checksum-verified clean Windows x64 package candidate pipeline
- first truthful sparse-container guest-byte translation path for standard QCOW2 clusters

## 0.5 Partitions + File Systems + Image Intelligence 🚧

Eleven substantial 0.5 execution slices are implemented and validated.

### Cross-provider partition intelligence ✅
- provider-agnostic `PartitionIntelligenceService`
- duplicate-index, zero-length, arithmetic-overflow, geometry, physical-bound and overlap findings
- capability-driven tests; no writes or repair
- PR #28 / final run #232 passed full Windows regression/build/artifact

### Bounded filesystem recognition ✅
- `FileSystemRecognitionService` scans only truthful physical mappings
- FAT12/FAT16/FAT32, exFAT, supported NTFS, ext2/ext3/ext4, ISO9660/Joliet and UDF VRS recognition
- structurally invalid partition maps stop before probing
- PR #29 / final run #235 passed full Windows regression/build/artifact

### Boot + installer intelligence ✅
- bounded El Torito boot-record/catalog validation
- evidence-backed BIOS/UEFI reporting
- bounded provider-backed Direct Browse traversal
- conservative Windows/Linux installer recognition and EFI architecture hints
- PR #30 / final run #242 passed full Windows CI

### Unified identity + health intelligence ✅
- `ImageIntelligenceService` aggregates only proven capabilities
- partition/filesystem/container/platform identity evidence
- selected exFAT/ext/NTFS/FAT32 health checks
- metadata-only providers do not gain invented guest-filesystem access
- PR #31 / implementation run #245 passed the complete regression/build/artifact path

### User-facing Analyze + reporting ✅
- bounded **Analyze** action in WinUI
- shared `ImageReportService` text/JSON reporting and Save JSON
- unknown-input, serialization and cancellation tests
- `by Swir` + GitHub navigation footer
- PR #32 / run #260 passed complete Windows CI and Release x64 artifact publication

### Deeper filesystem evidence ✅
- exFAT main/backup boot-region checksum and redundancy validation
- FAT32 FSInfo placement/signature/range validation
- bounded UDF anchor/descriptor checksum/location/CRC validation
- validated UDF volume identity evidence
- PR #33 / implementation run #262 passed the new gate plus full regression/build/artifact

### NTFS metadata + architecture reconciliation ✅
- bounded `$MFT` / `$MFTMirr` geometry and FILE-record size validation
- Update Sequence Array validation and fixup-normalized mirror comparison
- architecture evidence from independent boot/installer sources is preserved and conflicts are surfaced
- PR #34 / implementation run #266 passed complete Windows regression/build

### Clean Windows beta-package candidate ✅
- clean `DragonDiskForge-win-x64.zip` package candidate
- exactly one application EXE; PDB/test-only content rejected
- deterministic manifest + SHA-256 sidecar
- canonical Dragon icon at package root
- independent `verify-package.ps1` verification
- run #270 passed build/package verification and independently downloaded artifact checks

### Bounded UDF root-directory traversal ✅
- `UdfTraversalService` operates only on recognized physical UDF regions
- validated Type 1 partition maps only; Type 2 virtual/sparable/metadata maps fail closed
- bounded File Set Descriptor, root File Entry and File Identifier Descriptor validation
- one recorded short root extent, max 8 MiB / 4096 entries, non-recursive
- validated File Set Identifier becomes analysis identity evidence
- PR #36 / implementation run #274 passed the complete regression/build/package path

### Bounded QCOW2 guest-byte reader ✅
- generic read-only `IGuestByteReader` contract for truthful guest-visible byte access
- `Qcow2GuestByteReader` supports QCOW2 v2/v3 standard uncompressed active L1/L2 mappings
- allocated clusters, explicit QCOW2 v3 zero clusters and unallocated/no-backing clusters are handled without guessing
- reserved bits, cluster alignment, virtual bounds and physical-file bounds are validated before reads
- backing chains, encryption, dirty images, external data files, non-default compression metadata, extended L2 entries and compressed-cluster descriptors fail closed
- generated smoke tests cover cross-cluster reads, zero/unallocated clusters, OOB mappings, reserved bits, unsupported states and cancellation
- PR #37 / implementation run #277 passed the new reader gate plus the complete provider/Explorer/native Windows/Release/clean-package verification and artifact path
- this does **not** enable Direct Browse or filesystem analysis inside QCOW2 yet; integration remains a separate truthful capability step

See [`docs/GUEST-BYTE-READERS.md`](docs/GUEST-BYTE-READERS.md), [`docs/FILESYSTEM-DEPTH.md`](docs/FILESYSTEM-DEPTH.md), [`docs/FILESYSTEM-RECOGNITION.md`](docs/FILESYSTEM-RECOGNITION.md), [`docs/BOOT-INSTALLER-INTELLIGENCE.md`](docs/BOOT-INSTALLER-INTELLIGENCE.md) and [`docs/IMAGE-INTELLIGENCE.md`](docs/IMAGE-INTELLIGENCE.md).

### Remaining 0.5 work

- VMDK sparse guest-byte reader plus a common guest-reader integration path into bounded filesystem intelligence
- final 0.5 beta-scope hardening/documentation
- clean-machine launch/open/mount/explore/verify/analyze, normal-user UAC and real cross-process drag-out manual QA
- promote the verified version pipeline to `0.5.0-beta.1` only when the beta gate is complete

## 0.4 Extended Image Providers — COMPLETE ✅

The provider foundation, **eleven additional image families**, and provider-contract hardening are proven: IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU.

The planned first public GitHub beta remains **`0.5.0-beta.1`**. It will not be published until the agreed 0.5 scope and the independent beta gates in [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md) are complete.

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md), with execution state in [`docs/STATUS.md`](docs/STATUS.md) and [`docs/MILESTONES.md`](docs/MILESTONES.md).

## Tech

- C# / .NET 10 / WinUI 3 / Windows App SDK
- x64 + ARM64 targets
- shared Core engine for GUI and future CLI
- isolated Windows native-storage layer
- hardened provider registry with explicit capabilities/fallback/failure isolation
- bounded read-only partition, filesystem, optical-layout, virtual-disk and container metadata parsers

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

CI validates Core, provider-registry invariants, partition intelligence, filesystem recognition/depth, bounded UDF traversal, NTFS/architecture hardening, boot/install intelligence, unified image intelligence, image reporting, QCOW2 guest-byte translation, all proven image providers, Explorer safety, direct ISO integration, native ISO/VHD/VHDX integration and a full Windows x64 Release build. Green runs also build and independently verify a clean versioned ZIP candidate before publishing its SHA-256 sidecar and engineering artifact.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

Partition/filesystem intelligence operates only on proven byte mappings. UDF root traversal follows only validated Type 1 physical mappings and remains non-recursive. NTFS depth checks validate metadata only and never repair or traverse directories. Architecture reconciliation preserves conflicting evidence rather than guessing a winner.

The QCOW2 guest reader currently translates only standard uncompressed v2/v3 cluster mappings with no backing file or encryption. Unsupported compressed/extended/external states fail closed and no Direct Browse capability is advertised from that reader.

VMDK, QCOW and DMG provider surfaces remain capability-limited to what their tested engine paths actually support. WIM/ESD and FFU expose bounded container metadata only. FFU does not interpret write-descriptor destinations, access physical devices, write sectors or apply images.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
