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

## 0.5 Partitions + File Systems + Image Intelligence — 🚧 in progress

**Current 0.5 completion: approximately 99%.** Cross-provider partition intelligence, bounded physical and guest filesystem recognition, boot/installer intelligence, unified identity/health intelligence, the Windows analysis/report surface, deeper exFAT/FAT32/UDF evidence, bounded NTFS metadata depth, architecture reconciliation, bounded UDF root traversal, a versioned clean Windows package-candidate path and truthful QCOW2 plus hosted-sparse VMDK guest-byte readers are implemented and validated. Final beta hardening/manual QA remains.

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
- 🚧 final beta suffix, clean-machine/manual QA and public Release remain separate gates

### Bounded UDF root-directory traversal — ✅ complete
- ✅ validated physical Type 1 UDF partition maps only
- ✅ bounded File Set Descriptor/root File Entry/FID validation
- ✅ one recorded short root extent, max 8 MiB / 4096 entries, non-recursive
- ✅ Type 2 virtual/sparable/metadata maps fail closed
- ✅ PR #36 / implementation run #274 full regression/build/package path

### Guest-byte reader + intelligence foundation — ✅ complete for 0.5 scope

#### QCOW2 standard uncompressed mappings — ✅ complete
- ✅ generic read-only `IGuestByteReader` contract
- ✅ QCOW2 v2/v3 active L1/L2 translation
- ✅ standard allocated cluster reads
- ✅ explicit v3 zero clusters and unallocated/no-backing zero semantics
- ✅ guest and physical bounds, reserved-bit and alignment validation
- ✅ backing-file chains, encryption, dirty images, external-data mode, non-default compression metadata, extended L2 and compressed cluster descriptors fail closed
- ✅ cross-cluster/OOB/reserved-state/cancellation fixtures
- ✅ PR #37 / implementation run #277 passed the new reader test plus the complete provider, Explorer, native Windows, Release x64, clean-package verification and artifact path
- ✅ no Direct Browse or filesystem capability is implied by the isolated reader itself

#### VMDK hosted sparse standard uncompressed mappings — ✅ complete
- ✅ `VmdkSparseGuestByteReader` implements `IGuestByteReader`
- ✅ clean hosted sparse v1 `monolithicSparse`, one embedded extent, no parent chain
- ✅ active redundant/primary grain-directory selection and grain-table translation
- ✅ allocated reads, cross-grain reads and unallocated/no-parent zero semantics
- ✅ guest/physical bounds plus directory/table/grain metadata-overhead separation
- ✅ parent chains, split create types, unclean images, compressed/stream-optimized/zeroed-entry/unknown flag semantics fail closed
- ✅ generated fixtures cover active-directory selection, OOB pointers, metadata/data separation and cancellation
- ✅ PR #38 / run #284 passed the dedicated VMDK reader gate and complete Windows regression/build/package path
- ✅ no Direct Browse, Mount or extraction capability is implied by the isolated reader itself

#### Common guest partition/filesystem intelligence — ✅ complete
- ✅ `GuestPartitionTableReader` parses MBR/EBR/GPT only from proven guest-visible bytes
- ✅ guest partition layouts reuse the same geometry/bounds/overlap hardening as physical images
- ✅ `GuestFileSystemRecognitionService` recognizes FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS in bounded guest regions
- ✅ dedicated guest-relative detection models prevent physical/guest offset ambiguity
- ✅ QCOW2 and hosted-sparse VMDK feed the common guest-intelligence service
- ✅ text/JSON reports expose guest analysis separately from physical-container evidence
- ✅ generated QCOW2/VMDK fixtures cover MBR + FAT12, OOB guest partitions and cancellation
- ✅ PR #39 / implementation run #286 passed the dedicated gate and the complete provider/intelligence/Explorer/native Windows/Release/clean-package verification path

#### Future optional guest-byte variants
- ⬜ explicitly supported compressed/backing variants only after independent implementation/testing; never infer support

### Filesystem family depth
- 🚧 ISO9660/UDF — ISO direct browse proven; bounded UDF physical root traversal proven; generalized UDF Direct Browse and Type 2 mapping remain unsupported
- 🚧 FAT/FAT32/exFAT — physical and proven guest recognition plus selected metadata-health evidence proven; deeper reader functionality remains
- 🚧 NTFS metadata — boot + first-record metadata hardening proven; broader NTFS traversal remains unsupported
- ✅ ext-family recognition — ext2/ext3/ext4 recognition/basic metadata and bounded superblock state evidence proven

### Image-intelligence depth
- ✅ bootability + BIOS/UEFI foundation
- ✅ Windows/Linux installer-recognition foundation
- ✅ cross-source identity aggregation
- ✅ architecture reconciliation
- ✅ evidence-backed health/corruption foundation
- ✅ user-facing text/JSON analysis reports
- ✅ bounded non-recursive UDF Type 1 root traversal
- ✅ QCOW2/VMDK guest-byte readers integrated into bounded partition/filesystem analysis

**0.5 rule:** image intelligence must build on truthful provider byte mappings/capabilities. No UI shortcut may imply access the Core cannot actually perform.

**Next engineering focus:** final 0.5 beta-scope hardening/documentation synchronization and the remaining clean-machine/UAC/cross-process drag-out manual release gates.

---

## 0.6 Create + Convert + Verify — ⬜ planned
- ⬜ image creation and conversion pipeline
- ⬜ split/join and sparse/compression handling
- ⬜ SHA-256/SHA-512 verification
- ⬜ temporary output + atomic finalization
- ⬜ cancellation/rollback safety

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
