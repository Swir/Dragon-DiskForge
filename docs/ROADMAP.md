# Dragon DiskForge — Product Roadmap

This file is the source of truth for product progress. Meaningful feature changes must update tests, `CHANGELOG.md`, status and progress documentation.

## Status legend
- ✅ complete
- 🚧 in progress
- ⬜ planned

---

## 0.1 Foundation + Dragon Visual Identity — ✅ complete
WinUI 3/.NET 10 shell, Core separation, image detection, SHA-256 verification, read-only-first architecture, Dragon visual system, responsive/accessibility resources, Windows icon and x64 CI are proven.

## 0.2 Native Mount + Unmount — ✅ complete
Native Windows ISO/VHD/VHDX read-only-first Mount/Unmount, state detection, progress/cancellation and disposable Windows integration tests are proven.

## 0.3 Dragon Explorer — ✅ complete
- ✅ mounted-volume list/navigation/search/Copy out
- ✅ bounded Preview modes
- ✅ Recent Images + Favorites
- ✅ mounted history + multi-image workspace
- ✅ safe Copy-only drag-out
- ✅ managed ISO9660/Joliet direct browsing without mount

## 0.4 Extended Image Providers — ✅ complete
**Required 0.4 engineering scope: 100%.** Provider foundation, eleven additional image families and provider-contract hardening are real and tested.

- ✅ truthful capability reporting and deterministic provider resolution
- ✅ failure isolation, cancellation and descriptor hardening
- ✅ IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB
- ✅ VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU metadata

**0.4 exit criteria: PASSED.** Closing this milestone hardens the internal provider contract; it does not promise a stable public plugin API.

---

## 0.5 Partitions + File Systems + Image Intelligence — ✅ complete

**Required 0.5 automated engineering scope: 100%.** Cross-provider partition intelligence, bounded physical and guest filesystem recognition, boot/installer intelligence, unified identity/health intelligence, Windows analysis/reporting, deeper filesystem evidence, bounded UDF traversal, truthful QCOW2/VMDK guest readers, guest partition/filesystem integration and final guest partition integrity hardening are implemented and validated. Public beta release still has independent manual/package gates in `docs/BETA-RELEASE.md`.

### Provider-independent partition intelligence — ✅ complete
- ✅ capability-driven `PartitionIntelligenceService`
- ✅ geometry/bounds/overlap/overflow findings
- ✅ PR #28 / run #232 full regression/build/artifact

### Bounded filesystem-recognition foundation — ✅ complete
- ✅ FAT12/FAT16/FAT32, exFAT, supported NTFS, ext2/ext3/ext4
- ✅ ISO9660/Joliet and UDF VRS recognition
- ✅ scans only truthful physical mappings
- ✅ PR #29 / run #235 full regression/build/artifact

### Boot + installer intelligence — ✅ complete
- ✅ bounded El Torito validation
- ✅ evidence-backed BIOS/UEFI bootability
- ✅ Windows/Linux installer evidence and EFI architecture hints
- ✅ PR #30 / run #242 full Windows CI

### Unified identity + health intelligence — ✅ complete
- ✅ identity aggregation with source/provenance
- ✅ selected evidence-backed filesystem/partition health findings
- ✅ no fake guest probing for metadata-only providers
- ✅ PR #31 / run #245 implementation regression/build/artifact

### Windows Analyze + reporting — ✅ complete
- ✅ bounded Analyze UI action
- ✅ text/JSON reports + Save JSON
- ✅ `by Swir` + GitHub footer
- ✅ PR #32 / run #260 full Windows CI

### Deeper filesystem evidence — ✅ complete
- ✅ exFAT redundant boot/checksum evidence
- ✅ FAT32 FSInfo validation
- ✅ bounded UDF anchor/descriptor CRC/checksum/identity evidence
- ✅ PR #33 / run #262 implementation regression/build/artifact

### NTFS metadata + architecture reconciliation — ✅ complete
- ✅ bounded `$MFT`/`$MFTMirr` geometry and FILE record validation
- ✅ Update Sequence Array/fixup validation
- ✅ cross-source architecture reconciliation with explicit conflicts
- ✅ PR #34 / run #266 full Windows regression/build

### Beta-package preparation — ✅ complete candidate path
- ✅ clean versioned Windows x64 ZIP candidate
- ✅ manifest + SHA-256 sidecar + icon
- ✅ PDB/test-only content rejection
- ✅ independent ZIP reopening/verification
- ✅ run #270 full package gate and independent artifact review
- ⬜ final beta suffix, clean-machine/manual QA and public Release remain independent release gates

### Bounded UDF root-directory traversal — ✅ complete
- ✅ validated physical Type 1 UDF partition maps only
- ✅ bounded File Set Descriptor/root File Entry/FID validation
- ✅ one recorded short root extent, max 8 MiB / 4096 entries, non-recursive
- ✅ Type 2 virtual/sparable/metadata maps fail closed
- ✅ PR #36 / run #274 full regression/build/package path

### Guest-byte reader + intelligence foundation — ✅ complete

#### QCOW2 standard uncompressed mappings — ✅ complete
- ✅ generic read-only `IGuestByteReader` contract
- ✅ QCOW2 v2/v3 active L1/L2 translation
- ✅ standard allocated cluster reads
- ✅ explicit v3 zero clusters and unallocated/no-backing zero semantics
- ✅ guest and physical bounds, reserved-bit and alignment validation
- ✅ unsupported backing/encryption/dirty/external-data/compressed/extended-L2 states fail closed
- ✅ PR #37 / run #277 complete regression/build/package path

#### VMDK hosted sparse standard uncompressed mappings — ✅ complete
- ✅ `VmdkSparseGuestByteReader` implements `IGuestByteReader`
- ✅ clean hosted sparse v1 `monolithicSparse`, one embedded extent, no parent chain
- ✅ active redundant/primary grain-directory selection and grain-table translation
- ✅ allocated reads, cross-grain reads and unallocated/no-parent zero semantics
- ✅ guest/physical bounds plus directory/table/grain metadata-overhead separation
- ✅ unsupported parent/split/unclean/compressed/stream-optimized states fail closed
- ✅ PR #38 / run #284 complete Windows regression/build/package path

#### Common guest partition/filesystem intelligence — ✅ complete
- ✅ `GuestPartitionTableReader` parses MBR/EBR/GPT only from proven guest-visible bytes
- ✅ guest partition layouts reuse geometry/bounds/overlap hardening
- ✅ `GuestFileSystemRecognitionService` recognizes FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS in bounded guest regions
- ✅ dedicated guest-relative detection models prevent physical/guest offset ambiguity
- ✅ QCOW2 and hosted-sparse VMDK feed the common guest-intelligence service
- ✅ text/JSON reports expose guest analysis separately from physical-container evidence
- ✅ PR #39 / run #287 complete Windows regression/build/package path

#### Final guest partition structure hardening — ✅ complete
- ✅ GPT primary-header CRC32 validation
- ✅ bounded streaming validation of the declared GPT partition-entry-array CRC32
- ✅ GPT backup/header/usable/table placement cross-checks against virtual guest geometry
- ✅ MBR/EBR boot-status validation
- ✅ EBR chain and logical-partition containment inside the declared extended partition
- ✅ generated valid/corrupt GPT and escaping EBR/logical fixtures
- ✅ PR #40 implementation run #289 complete Windows regression/build/package path

### Filesystem family depth at 0.5 exit
- ✅ ISO9660/UDF beta scope — ISO direct browse proven; bounded physical UDF root traversal proven; generalized UDF Direct Browse and Type 2 mapping intentionally unsupported
- ✅ FAT/FAT32/exFAT beta scope — physical and proven guest recognition plus selected metadata-health evidence proven; deeper traversal deferred
- ✅ NTFS metadata beta scope — boot + first-record metadata hardening proven; broader NTFS traversal intentionally unsupported
- ✅ ext-family beta scope — ext2/ext3/ext4 recognition/basic metadata and bounded superblock state evidence proven

### Image-intelligence depth at 0.5 exit
- ✅ bootability + BIOS/UEFI foundation
- ✅ Windows/Linux installer-recognition foundation
- ✅ cross-source identity aggregation
- ✅ architecture reconciliation
- ✅ evidence-backed health/corruption foundation
- ✅ user-facing text/JSON analysis reports
- ✅ bounded non-recursive UDF Type 1 root traversal
- ✅ QCOW2/VMDK guest-byte readers integrated into bounded partition/filesystem analysis
- ✅ guest GPT/EBR integrity hardening before filesystem probing

**0.5 exit criteria: PASSED for automated engineering scope.** Beta publication remains blocked until the independent release/manual gates in `docs/BETA-RELEASE.md` are satisfied.

---

## 0.6 Create + Convert + Verify — 🚧 in progress

**Current 0.6 engineering completion: approximately 80%.** Four of five top-level roadmap deliverables are implemented and validated.

- ✅ image creation and conversion pipeline
- ⬜ split/join and sparse/compression handling
- ✅ SHA-256/SHA-512 verification
- ✅ temporary output + atomic finalization
- ✅ cancellation/rollback safety

### SHA-256/SHA-512 verification — ✅ complete foundation
- ✅ `ImageVerificationInfo` returns SHA-256, SHA-512 and exact hashed byte count
- ✅ combined verification computes both digests in one bounded sequential file pass
- ✅ existing `ComputeSha256Async` API remains compatible
- ✅ dedicated `ComputeSha512Async` API
- ✅ monotonic bounded progress and cancellation propagation
- ✅ generated multi-buffer, empty-file, missing-file and cancellation smoke coverage
- ✅ explicit Windows CI verification gate
- ✅ PR #41 implementation run #292 passed the complete Windows regression/build/package path

### Temporary output + atomic finalization — ✅ complete foundation
- ✅ reusable Core `SafeOutputService`
- ✅ unique temporary output created in the destination directory
- ✅ explicit `FailIfExists` and `ReplaceExisting` overwrite policies
- ✅ completed temporary output is flushed before finalization
- ✅ same-directory move for new destinations and replace semantics for existing destinations
- ✅ pre-existing and racing destination cases fail closed according to policy
- ✅ writer failure/cancellation/failed commit cleanup preserves existing destinations when possible
- ✅ missing parent directories and unknown overwrite policies fail closed
- ✅ generated rollback/race/cancellation/temp-cleanup coverage
- ✅ PR #42 implementation run #296 and final run #298 passed the complete Windows regression/build/package path

### Image creation + conversion pipeline — ✅ complete foundation
- ✅ `RawImagePipelineService.CreateBlankAsync` creates an explicit-length logical RAW image through the safe transaction boundary
- ✅ generic `ExportGuestToRawAsync` materializes a proven `IGuestByteReader` source with a bounded 1 MiB transfer buffer
- ✅ explicit QCOW2 → RAW conversion using the proven QCOW2 standard-uncompressed reader subset
- ✅ explicit hosted-sparse VMDK → RAW conversion using the proven clean `monolithicSparse` reader subset
- ✅ committed output length must equal captured guest-visible source length
- ✅ source/destination identity is rejected for file-backed conversion
- ✅ progress reaches `1.0` only after transaction commit
- ✅ lengths beyond the current output file domain fail before mutation
- ✅ generated real-format fixtures verify allocated and proven zero/unallocated materialization
- ✅ PR #43 implementation run #300 passed the new RAW pipeline gate plus the complete Windows regression/build/package path

### Cancellation + rollback safety — ✅ complete for current mutating pipelines
- ✅ cancellation after a real guest read aborts before final commit
- ✅ reader failure during replacement preserves the existing destination
- ✅ temporary transaction files are cleaned when possible after failure/cancellation
- ✅ `FailIfExists` refuses an existing destination before consuming the source
- ✅ unsupported QCOW2/VMDK states retain their fail-closed reader behavior
- ✅ no Create/Convert WinUI capability is enabled by this Core-only work

### Remaining 0.6 engineering priority

Implement split/join plus explicitly bounded sparse/compression handling. This remains open: materializing already-proven sparse guest mappings into flat RAW does **not** count as writing sparse container metadata or decoding unsupported compressed payloads.

See `docs/OUTPUT-TRANSACTIONS.md` and `docs/RAW-IMAGE-PIPELINES.md`.

## 0.7 Physical Media Tools — ⬜ planned
Future physical-media functionality remains gated behind dedicated safety design, explicit user confirmation, device identity checks and independent validation before it becomes user-visible.

## 0.8 Windows Integration + Power Tools — ⬜ planned
- ⬜ file associations/context menu
- ⬜ shared-Core CLI
- ⬜ PowerShell-friendly output
- ⬜ session restore/settings import-export/diagnostic export

## 0.9 Quality, Security + Beta Hardening — ⬜ planned
- ⬜ expanded provider/integration tests
- ⬜ non-admin UAC/manual drag validation
- ⬜ large/corrupt/truncated/fuzz-style image tests
- ⬜ keyboard/screen-reader/High-DPI/theme review
- ⬜ localization architecture and EN/PL baseline
- ⬜ crash diagnostics/performance profiling
- ⬜ beta regression checklist

## 1.0 Production Release — ⬜ planned
- ⬜ final UI/UX
- ⬜ signed installer / portable build where appropriate
- ⬜ release pipeline and update strategy
- ⬜ stable provider API/config migration
- ⬜ full documentation/capability matrix/troubleshooting
- ⬜ regression suite green
- ⬜ GitHub Release with binaries/checksums

---

## Non-negotiable project rules
1. Never enable a fake UI capability.
2. Read-only inspection is the default.
3. Sensitive operations require explicit validation and confirmation.
4. UI stays separate from Core.
5. New formats use providers/capabilities, not one monolithic parser.
6. README, ROADMAP, STATUS, MILESTONES and CHANGELOG stay synchronized.
7. Large image fixtures are generated, not committed.
8. Accessibility outranks decoration.
9. High-impact operations remain gated until independently validated.
10. A milestone completes only after its exit criteria pass.
