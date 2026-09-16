# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a modern Windows application for inspecting, mounting, exploring, verifying and analyzing disk-image formats from one interface. It combines a native WinUI 3 experience with a distinctive **Dragon / forged-metal / ember** identity and follows one strict rule: unsupported actions stay disabled until their engine path is implemented and tested.

## Current development version — 0.5.0-alpha.1

## Project progress — 66% toward 1.0

`█████████████░░░░░░░ 66%`

**Overall completion:** **66%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **100%** ✅
- `0.5 Partitions + File Systems + Image Intelligence` — **100%** ✅
- `0.6 Create + Convert + Verify` — **~80%** 🚧
- `0.7 → 1.0` — planned / future milestones

> Progress changes only after meaningful implementation and validation checkpoints. CI count alone never increases completion.

## Proven product foundation

- WinUI 3 / .NET 10 desktop shell with Dragon visual identity and application icon
- native Windows read-only-first ISO/VHD/VHDX Mount + Unmount
- mounted-volume Dragon Explorer with search, Preview and safe Copy out
- Recent Images, Favorites, mount history and multi-image workspace
- safe Copy-only drag-out to Windows Explorer/Desktop
- managed ISO9660/Joliet direct browsing without mounting
- shared Core verification with SHA-256, SHA-512, progress/cancellation and exact hashed-byte reporting
- reusable `SafeOutputService` transaction boundary with temporary output, explicit overwrite policy, same-directory finalization and failure/cancellation cleanup
- first real Core image-producing pipeline: blank RAW creation plus proven QCOW2/VMDK guest-byte materialization to RAW
- pipeline-level cancellation/reader-failure rollback tests that preserve existing destinations and do not publish partial output
- hardened provider registry with truthful capabilities and deterministic resolution
- bounded read-only partition, filesystem, boot/install and image-intelligence services
- user-facing **Analyze** action with text/JSON reporting and Save JSON
- required **by Swir** + GitHub footer in the Windows UI
- versioned, checksum-verified clean Windows x64 package candidate pipeline
- truthful sparse-container guest-byte translation for standard QCOW2 and hosted sparse VMDK mappings
- bounded guest-relative MBR/EBR/GPT and filesystem intelligence over those proven guest-byte readers

## 0.6 Create + Convert + Verify — IN PROGRESS 🚧

Four of five top-level 0.6 engineering deliverables are now implemented and validated. The remaining top-level scope is split/join plus broader sparse/compression handling. Mutating Create/Convert UI capabilities remain disabled until their product UX and format-specific release gates are separately implemented and tested.

### Dual SHA-256/SHA-512 verification foundation ✅

- `ImageVerificationInfo` reports SHA-256, SHA-512 and the exact number of hashed bytes
- combined verification computes both digests in one bounded sequential file pass
- existing `ComputeSha256Async` callers remain compatible
- dedicated `ComputeSha512Async` API
- bounded monotonic progress and cancellation propagation
- generated smoke coverage for multi-buffer input, empty files, missing files and pre-cancellation
- explicit Windows CI gate
- PR #41 implementation run #292 passed the verification gate plus the complete Windows regression/build/package path

### Safe output transaction foundation ✅

- `SafeOutputService` writes to a unique temporary file in the destination directory
- `FailIfExists` refuses both pre-existing and racing destinations
- `ReplaceExisting` promotes only a completed temporary output
- temporary output is flushed before finalization
- cancellation/writer failure/failed commit preserve an existing destination and clean the temporary output when possible
- missing destination directories and unknown overwrite policies fail closed
- result metadata reports normalized destination path, committed byte count and whether replacement occurred
- PR #42 implementation run #296 and final synchronized run #298 passed the safe-output gate plus the complete Windows regression/build/clean-package/artifact path

### RAW creation + guest-to-RAW conversion pipeline ✅

- `RawImagePipelineService.CreateBlankAsync` creates a zero-filled logical RAW image at an explicit byte length through the safe transaction boundary
- `ExportGuestToRawAsync` materializes any proven `IGuestByteReader` source through bounded sequential 1 MiB transfers
- explicit QCOW2 → RAW and hosted-sparse VMDK → RAW entry points reuse the already-proven guest-byte readers rather than reinterpreting container metadata
- the committed output length must exactly match the captured guest-visible source length
- source and destination paths must differ for file-backed conversions
- virtual/output lengths beyond the current `FileStream` domain fail before mutation
- progress remains below `1.0` while data is staged and reaches `1.0` only after transaction commit
- generated tests prove blank RAW semantics, multi-buffer export, replacement/refusal, exact bytes, QCOW2 materialization and VMDK materialization
- PR #43 implementation run #300 passed the new RAW pipeline gate plus the complete Windows regression/build/clean-package/artifact path

### Pipeline-level cancellation + rollback ✅

- cancellation after a real guest read aborts before final commit and leaves no partial destination
- guest-reader failure during replacement preserves the previous destination
- transaction temporary files are cleaned when possible on cancellation/failure
- pre-existing destination refusal happens before source consumption under `FailIfExists`
- unsupported QCOW2/VMDK semantics still fail closed in their existing guest readers and do not gain a write path

**Current 0.6 engineering completion:** approximately **80%** based on 4 of 5 top-level roadmap deliverables.

**Next 0.6 priority:** implement safe split/join and explicitly bounded sparse/compression handling without weakening transactional rollback or truthful format support. No Create/Convert UI action should be enabled merely because the Core foundation exists.

See [`docs/OUTPUT-TRANSACTIONS.md`](docs/OUTPUT-TRANSACTIONS.md) and [`docs/RAW-IMAGE-PIPELINES.md`](docs/RAW-IMAGE-PIPELINES.md).

## 0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅

Fourteen substantial 0.5 execution slices are implemented and validated. The automated engineering scope is complete; public beta publication still has independent package/manual gates in [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

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

### Bounded hosted-sparse VMDK guest-byte reader ✅
- `VmdkSparseGuestByteReader` implements the same read-only guest-byte contract for the proven hosted sparse v1 subset
- clean single-extent `monolithicSparse` images with no parent chain and no compression are translated through active grain directory/grain tables
- redundant-vs-primary grain-directory selection follows the sparse-header flag
- unallocated grains become zeroes only because parent chains are refused
- guest bounds, directory/table/grain physical bounds and metadata-overhead separation are enforced before reads
- compressed/stream-optimized/zeroed-entry/unknown-flag states, split create types, parent chains and unclean images fail closed
- generated tests cover allocated/sparse/cross-grain reads, active-directory selection, metadata/data separation, OOB pointers and cancellation
- PR #38 / run #284 passed the dedicated VMDK reader gate plus the complete Windows regression/build/package path

### Common guest partition + filesystem intelligence ✅
- `GuestPartitionTableReader` parses bounded guest-visible MBR, EBR and GPT structures without falling back to physical container offsets
- `GuestFileSystemRecognitionService` recognizes FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS in the guest address space
- QCOW2 and hosted-sparse VMDK readers feed the same partition-layout safety checks used by physical images
- guest-relative offsets use dedicated models so they cannot be confused with physical container offsets
- `ImageReportService` exposes a separate structured `GuestAnalysis` section in JSON and explicit **GUEST ADDRESS SPACE** sections in text reports
- generated QCOW2/VMDK fixtures prove guest MBR + FAT12 recognition, out-of-range partition refusal and cancellation
- PR #39 / run #287 passed the guest-intelligence gate plus complete provider/intelligence/Explorer/native Windows/Release/clean-package verification and artifact publication

### Final guest partition structure hardening ✅
- guest GPT primary-header CRC32 is validated before metadata is trusted
- the declared GPT partition-entry-array CRC32 is verified with bounded streaming reads
- GPT usable ranges, backup-header placement and primary entry-array placement are cross-checked against guest geometry
- MBR/EBR boot-status bytes must be valid
- EBR chains and logical partitions must remain inside the declared extended-partition container
- generated fixtures cover valid GPT, corrupt header/table checksums, invalid MBR status and escaping EBR/logical ranges
- PR #40 implementation run #289 passed the complete Windows regression/build/clean-package path

Guest intelligence remains analysis-only. It does not enable Direct Browse, extraction, Mount or writes for QCOW2/VMDK; unsupported container semantics continue to fail closed at the guest-byte reader boundary.

See [`docs/GUEST-BYTE-READERS.md`](docs/GUEST-BYTE-READERS.md), [`docs/FILESYSTEM-DEPTH.md`](docs/FILESYSTEM-DEPTH.md), [`docs/FILESYSTEM-RECOGNITION.md`](docs/FILESYSTEM-RECOGNITION.md), [`docs/BOOT-INSTALLER-INTELLIGENCE.md`](docs/BOOT-INSTALLER-INTELLIGENCE.md) and [`docs/IMAGE-INTELLIGENCE.md`](docs/IMAGE-INTELLIGENCE.md).

### Remaining release gates after 0.5 engineering completion

- clean-machine launch/open/mount/explore/verify/analyze regression
- normal-user UAC and real cross-process drag-out manual QA
- promote the verified version/package pipeline to `0.5.0-beta.1` only when every beta gate is satisfied

## 0.4 Extended Image Providers — COMPLETE ✅

The provider foundation, **eleven additional image families**, and provider-contract hardening are proven: IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU.

The planned first public GitHub beta remains **`0.5.0-beta.1`**. The 0.5 automated engineering scope is complete, but the independent manual/package gates in [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md) must pass before publication.

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

CI validates Core, SHA-256/SHA-512 verification, safe output transactions, RAW creation/guest export pipelines, provider-registry invariants, physical and guest partition/filesystem intelligence, GPT/EBR integrity hardening, filesystem depth, bounded UDF traversal, NTFS/architecture hardening, boot/install intelligence, unified image intelligence, image reporting, QCOW2 and VMDK guest-byte translation, all proven image providers, Explorer safety, direct ISO integration, native ISO/VHD/VHDX integration and a full Windows x64 Release build. Green runs also build and independently verify a clean versioned ZIP candidate before publishing its SHA-256 sidecar and engineering artifact.

## Safety design

Inspection and verification are read-only-first. Native mounts default to read-only. Metadata parsers validate offsets and lengths before reading and reject contradictory structures rather than inventing an interpretation.

0.6 file-producing Core operations are deliberately not user-visible yet. `SafeOutputService` writes through a temporary file in the destination directory and commits only completed output according to an explicit overwrite policy. `RawImagePipelineService` uses that boundary for blank RAW creation and proven guest-byte-to-RAW materialization. Source containers remain read-only; pipeline cancellation and reader failures are tested to preserve existing destinations and avoid intentionally publishing partial output.

Partition/filesystem intelligence operates only on proven byte mappings. Physical and guest-relative offsets are modeled separately. Guest GPT checksums and EBR containment are validated before filesystem probing. UDF root traversal follows only validated Type 1 physical mappings and remains non-recursive. NTFS depth checks validate metadata only and never repair or traverse directories. Architecture reconciliation preserves conflicting evidence rather than guessing a winner.

The QCOW2 guest reader currently translates only standard uncompressed v2/v3 cluster mappings with no backing file or encryption. The VMDK guest reader currently translates only clean hosted sparse v1 `monolithicSparse` mappings with one extent, no parent chain and no compressed/stream-optimized semantics. Unsupported states fail closed. The proven guest readers may feed bounded partition/filesystem analysis and the new flat-RAW export pipeline, but they still do not advertise Direct Browse, extraction or Mount.

VMDK, QCOW and DMG provider surfaces remain capability-limited to what their tested engine paths actually support. WIM/ESD and FFU expose bounded container metadata only. FFU does not interpret write-descriptor destinations, access physical devices, write sectors or apply images.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after the underlying engine path exists and is testable.
