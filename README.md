# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, verifying and analyzing disk-image formats from one interface. The project combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** visual identity and follows one strict rule: unsupported actions stay disabled until their engine path is implemented and tested.

## Current development version — 0.5.0-alpha.1

## Project progress — 57% toward 1.0

`███████████░░░░░░░░░ 57%`

**Overall completion:** **57%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **100%** ✅
- `0.5 Partitions + File Systems + Image Intelligence` — **~93%** 🚧
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

## 0.5 Partitions + File Systems + Image Intelligence 🚧

Ten substantial 0.5 execution slices are now implemented and validated.

### Cross-provider partition intelligence ✅

- provider-agnostic `PartitionIntelligenceService`
- resolves metadata through `ProviderRegistry` + truthful `PartitionTable`
- duplicate-index, zero-length, LBA-overflow, geometry, bounds and overlap checks
- stable finding codes/severities
- PR #28 / final run #232 passed the docs-synchronized Windows regression/build/artifact path

### Bounded filesystem recognition ✅

- provider-integrated `FileSystemRecognitionService`
- FAT12/FAT16/FAT32, exFAT, supported NTFS boot metadata and ext2/ext3/ext4 recognition
- ISO9660/Joliet descriptor recognition and UDF Volume Recognition Sequence detection
- scans only truthful physical mappings or structurally validated partition byte ranges
- no invented guest-sector access inside sparse/compressed VMDK, QCOW/QCOW2 or DMG
- PR #29 / final run #235 passed the full Windows regression/build/artifact path

### Boot + installer intelligence ✅

- bounded El Torito boot-record/catalog validation
- BIOS/UEFI evidence only from valid boot catalog entries
- bounded provider-backed Direct Browse traversal
- conservative Windows and Linux installer recognition
- EFI fallback architecture hints for x86, x86_64, ARM, ARM64 and RISC-V 64
- PR #30 / final run #242 passed full Windows CI

### Unified identity + health intelligence ✅

- `ImageIntelligenceService` aggregates only proven capabilities
- partition names, filesystem labels/IDs, WIM GUIDs and FFU PlatformIDs
- partition-structure findings plus evidence-backed exFAT/ext/NTFS/FAT32 health checks
- metadata-only virtual/container providers remain excluded from guest filesystem probing
- PR #31 / implementation run #245 passed the complete code/test/native/build/artifact path

### User-facing analysis/report surface ✅

- bounded **Analyze** action in the WinUI image result card
- shared `ImageReportService` text/JSON output over truthful provider resolution
- Save JSON without enabling unsupported mutation paths
- dedicated report tests for unknown input, serialization and cancellation
- `by Swir` + GitHub navigation footer
- PR #32 / run #260 passed complete Windows CI and Release x64 artifact publication

### Deeper filesystem evidence ✅

- bounded `FileSystemDepthService` layered on already-recognized physical filesystem regions
- exFAT main/backup boot-region checksums and redundant-copy comparison
- FAT32 FSInfo placement, signature, free-count and next-free validation
- UDF primary anchor validation at logical block 256
- UDF descriptor-tag checksum/location/CRC validation with a 16 MiB descriptor-sequence inspection cap
- validated UDF Primary/Logical Volume Descriptor d-strings become identity evidence
- PR #33 / implementation run #262 passed the depth gate plus the full prior regression/build/artifact path

### NTFS metadata + architecture reconciliation hardening ✅

- bounded `NtfsMetadataDepthService` validates `$MFT` / `$MFTMirr` cluster locations and FILE-record sizing
- NTFS FILE-record Update Sequence Array geometry and sector-trailer fixups are validated before mirror comparison
- validated first `$MFT` / `$MFTMirr` records are compared after fixup normalization; corrupt or divergent evidence is reported without repair
- `ArchitectureReconciliationService` preserves both boot-path and installer-path hints instead of silently choosing one
- direct one-to-one architecture disagreement becomes an explicit warning while intentional multi-architecture boot evidence stays multi-architecture
- generated fixtures cover healthy/corrupt NTFS metadata, out-of-range `$MFTMirr`, architecture conflicts and cancellation
- PR #34 / implementation run #266 passed the new hardening gate plus the complete provider/Explorer/native Windows/Release x64 path

### Clean Windows beta-package candidate ✅

- Windows CI creates a clean `DragonDiskForge-win-x64.zip` candidate in addition to the engineering artifact
- public-package staging excludes `.pdb` and test-only files and requires exactly one `DragonDiskForge.App.exe`
- the package contains a deterministic manifest and generated SHA-256 sidecar
- the canonical Dragon icon is guaranteed in the package root
- run #270 passed the clean-package build, independent package-verification gate and both artifact uploads

### Release version + package verification ✅

- product version metadata is centralized in `Directory.Build.props`
- Release builds embed numeric assembly/file versions plus the semantic informational version
- package manifests record repository version, executable ProductVersion/FileVersion, architecture, entry point, icon and entry-point SHA-256
- `verify-package.ps1` independently reopens the ZIP and rejects checksum, manifest, version, architecture, EXE hash, icon, PDB or test-content mismatches
- the run #270 clean artifact was independently downloaded after CI: ZIP sidecar SHA-256 matched, the manifest reported `0.5.0-alpha.1`, the executable hash matched the manifest, one application EXE was present, and no PDB files were present

### Bounded UDF root-directory traversal ✅

- dedicated `UdfTraversalService` operates only after UDF recognition supplies a truthful physical byte region
- follows only validated Type 1 partition maps; virtual, sparable and metadata partition maps remain unsupported rather than guessed
- validates Partition Descriptors, File Set Descriptor, root File Entry and File Identifier Descriptor tags/checksums/CRCs/locations before accepting evidence
- root-directory reads are capped at **8 MiB** and **4096 entries**
- first slice accepts exactly one recorded short allocation extent and never recursively walks subdirectories
- validated File Set Identifier flows into analysis/report identity evidence
- generated smoke tests cover a valid root entry, corrupt FID checksum, out-of-range root ICB, unsupported Type 2 map and cancellation
- implementation run #274 passed the new UDF traversal gate plus the complete provider/Explorer/native Windows/Release/package-verification path

See [`docs/FILESYSTEM-DEPTH.md`](docs/FILESYSTEM-DEPTH.md), [`docs/FILESYSTEM-RECOGNITION.md`](docs/FILESYSTEM-RECOGNITION.md), [`docs/BOOT-INSTALLER-INTELLIGENCE.md`](docs/BOOT-INSTALLER-INTELLIGENCE.md) and [`docs/IMAGE-INTELLIGENCE.md`](docs/IMAGE-INTELLIGENCE.md).

### Remaining 0.5 work

- truthful guest-sector reader paths before filesystems inside sparse/compressed virtual disks can be analyzed
- final 0.5 beta-scope hardening/documentation
- clean-machine launch/open/mount/explore/verify/analyze, normal-user UAC and real cross-process drag-out manual QA
- promote the verified version pipeline to the final `0.5.0-beta.1` suffix only when the beta gate is actually complete

## 0.4 Extended Image Providers — COMPLETE ✅

The provider foundation, **eleven additional image families**, and provider-contract hardening are proven: IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU.

The cross-process human drag gesture and normal-user UAC prompt remain manual QA gates in [`docs/MANUAL-VALIDATION.md`](docs/MANUAL-VALIDATION.md).

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

CI validates Core, provider-registry invariants, partition intelligence, filesystem recognition/depth, bounded UDF traversal, NTFS/architecture hardening, boot/install intelligence, unified image intelligence, image reporting, all proven image providers, Explorer safety, direct ISO integration, native ISO/VHD/VHDX integration and a full Windows x64 Release build. Green runs also build and independently verify a clean versioned ZIP candidate before publishing its SHA-256 sidecar and the engineering artifact.

## Safety design

Inspection is read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

Partition intelligence reports structural layout findings only. Filesystem recognition reports bounded evidence only. Deeper filesystem checks never leave already-recognized physical regions. UDF root traversal follows only validated physical Type 1 mappings, is non-recursive and does not enable Direct Browse or extraction. NTFS depth checks validate metadata only and never repair, follow attributes or traverse directories. Architecture reconciliation preserves conflicting proven clues rather than guessing a winner. Health findings are evidence-backed checks, not a whole-filesystem “healthy” guarantee.

VMDK, QCOW and DMG expose only proven metadata. WIM/ESD and FFU expose bounded container metadata only. FFU does not interpret write-descriptor destinations, access physical devices, write sectors or apply images.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
