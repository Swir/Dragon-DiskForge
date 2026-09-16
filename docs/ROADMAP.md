# Dragon DiskForge — Product Roadmap

This file is the source of truth for product progress. Meaningful feature changes must update tests, `CHANGELOG.md`, status and progress documentation.

## Status legend

- ✅ complete
- 🚧 in progress
- ⬜ planned

---

## 0.1 Foundation + Dragon Visual Identity — ✅ complete

WinUI 3/.NET 10 shell, Core separation, image detection, SHA-256 verification, read-only-first architecture, Dragon visual system, responsive/accessibility resources, Windows icon and x64 CI are proven.

---

## 0.2 Native Mount + Unmount — ✅ complete

Native Windows ISO/VHD/VHDX read-only-first Mount/Unmount, state detection, progress/cancellation and disposable Windows integration tests are proven.

---

## 0.3 Dragon Explorer — ✅ complete

- ✅ mounted-volume list/navigation/search/Copy out
- ✅ bounded Preview modes
- ✅ Recent Images + Favorites
- ✅ mounted history + multi-image workspace
- ✅ safe Copy-only drag-out
- ✅ managed ISO9660/Joliet direct browsing without mount

---

## 0.4 Extended Image Providers — ✅ complete

**Required 0.4 engineering scope: 100%.** Provider foundation, eleven additional image families and provider-contract hardening are real and tested.

- ✅ truthful capability reporting and deterministic provider resolution
- ✅ failure isolation, cancellation and descriptor hardening
- ✅ IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB
- ✅ VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU metadata

**0.4 exit criteria: PASSED.** Closing this milestone hardens the internal provider contract; it does not promise a stable public plugin API.

---

## 0.5 Partitions + File Systems + Image Intelligence — 🚧 in progress

**Current 0.5 completion: approximately 82%.** Cross-provider partition intelligence, bounded filesystem recognition, boot/installer intelligence, unified identity/health intelligence, the Windows analysis/report surface and a deeper bounded filesystem-evidence layer are implemented and validated.

### Cross-provider partition intelligence — ✅ complete
- ✅ provider-agnostic `PartitionIntelligenceService`
- ✅ resolves through `ProviderRegistry` + `PartitionTable`
- ✅ duplicate-index, zero-length, arithmetic-overflow, geometry, physical-bound and overlap findings
- ✅ stable finding codes/severities
- ✅ capability-driven fake providers in dedicated smoke tests
- ✅ no writes, repairs or fake filesystem-health claims
- ✅ PR #28 / final run #232 passed full Windows regression/build/artifact

### Bounded filesystem-recognition foundation — ✅ complete
- ✅ provider-integrated `FileSystemRecognitionService`
- ✅ scans whole physical images only when that byte mapping is truthful
- ✅ partition-capable providers are scanned only through structurally validated physical partition ranges
- ✅ FAT12/FAT16/FAT32 bounded BPB recognition and metadata
- ✅ exFAT bounded boot metadata
- ✅ supported NTFS boot metadata
- ✅ ext2/ext3/ext4 superblock recognition, label, UUID and block size
- ✅ ISO9660/Joliet descriptor recognition and volume label
- ✅ UDF VRS recognition (`BEA01` / `NSR02|NSR03` / `TEA01`)
- ✅ generated sparse fixtures, false-positive checks and cancellation coverage
- ✅ explicit refusal to pretend sparse/compressed virtual-disk guest sectors are physical bytes
- ✅ PR #29 / final run #235 passed full Windows regression/build/artifact

### Boot + installer intelligence foundation — ✅ complete
- ✅ bounded El Torito boot-record/catalog discovery and validation
- ✅ physical boot-image load-range bounds
- ✅ BIOS and UEFI bootability derived only from catalog evidence
- ✅ bounded provider-backed Direct Browse traversal with cycle/depth/entry limits
- ✅ Windows setup + boot WIM + install payload recognition
- ✅ Linux casper, Debian-style and Anaconda-style installer/live evidence
- ✅ bounded EFI fallback filename architecture hints
- ✅ file markers never fabricate BIOS/UEFI bootability
- ✅ PR #30 / final run #242 passed full Windows regression/build/artifact

See [`docs/BOOT-INSTALLER-INTELLIGENCE.md`](BOOT-INSTALLER-INTELLIGENCE.md).

### Unified identity + health intelligence foundation — ✅ complete
- ✅ `ImageIntelligenceService` composes only already-proven provider/intelligence capabilities
- ✅ partition names and filesystem labels/identifiers retain source + partition provenance
- ✅ WIM/ESD container GUIDs and FFU PlatformIDs are exposed as bounded identity evidence
- ✅ architecture hints flow from the bounded boot/installer layer
- ✅ partition structural findings flow into a shared health model
- ✅ exFAT dirty/media-failure, ext state, NTFS backup-boot and FAT32 backup-boot checks
- ✅ metadata-only sparse/compressed/container providers do not gain fake filesystem probing
- ✅ PR #31 / implementation run #245 passed full code/test/native/build/artifact regression

See [`docs/IMAGE-INTELLIGENCE.md`](IMAGE-INTELLIGENCE.md).

### Windows Analyze + reporting surface — ✅ complete
- ✅ bounded **Analyze** action in the image result card
- ✅ shared `ImageReportService` text/JSON reporting over truthful provider resolution
- ✅ Save JSON without enabling unsupported mutation paths
- ✅ unknown-input truthfulness, serialization and cancellation tests
- ✅ `by Swir` + GitHub navigation footer
- ✅ PR #32 / run #260 passed complete Windows CI and Release x64 artifact publication

### Deeper filesystem evidence — ✅ complete
- ✅ `FileSystemDepthService` is restricted to already-recognized physical filesystem regions
- ✅ exFAT main/backup 12-sector boot-region checksum validation
- ✅ exFAT redundant boot-copy comparison excluding mutable VolumeFlags/PercentInUse bytes
- ✅ FAT32 FSInfo reserved-area/range validation
- ✅ FAT32 FSInfo lead/structure/trail signature validation
- ✅ FAT32 free-cluster count and next-free hint bounds
- ✅ UDF primary Anchor Volume Descriptor Pointer validation at logical block 256
- ✅ UDF descriptor-tag checksum, tag-location and CRC validation
- ✅ bounded main volume-descriptor sequence with a 16 MiB inspection ceiling
- ✅ validated UDF Primary/Logical Volume Descriptor d-strings become identity evidence
- ✅ generated valid/corrupt exFAT, FAT32 FSInfo, valid/corrupt UDF and cancellation tests
- ✅ PR #33 / implementation run #262 passed the new gate plus all prior provider/Explorer/native Windows/Release x64/artifact checks before documentation synchronization

See [`docs/FILESYSTEM-DEPTH.md`](FILESYSTEM-DEPTH.md).

### Filesystem family depth
- 🚧 ISO9660/UDF — ISO direct browsing proven; UDF VRS plus bounded primary anchor/main descriptor-sequence metadata proven; independently bounded UDF traversal remains
- 🚧 FAT/FAT32/exFAT — recognition plus selected backup/checksum/FSInfo health evidence proven; deeper reader functionality remains
- 🚧 NTFS metadata — bounded boot metadata plus backup-boot consistency proven; deeper supported metadata remains
- ✅ ext-family recognition — ext2/ext3/ext4 recognition/basic metadata and bounded superblock state evidence proven

### Image-intelligence depth
- ✅ bootability + BIOS/UEFI foundation through bounded El Torito evidence
- ✅ Windows/Linux installer-recognition foundation through bounded Direct Browse file evidence
- ✅ cross-source identity aggregation for partition/filesystem/container/platform identifiers
- ✅ bounded architecture-hint aggregation foundation
- ✅ evidence-backed health/corruption foundation for partition structure and selected filesystem metadata
- ✅ user-facing text/JSON analysis report surface
- 🚧 stronger reconciliation when several independent architecture sources exist
- 🚧 deeper supported NTFS and UDF reader paths
- ⬜ virtual guest-sector readers before filesystems inside sparse/compressed virtual disks can be analyzed

**0.5 rule:** image intelligence must build on truthful provider byte mappings/capabilities. No format-specific UI shortcut may imply access the Core cannot actually perform.

**Next engineering focus:** deeper supported NTFS metadata, cross-source architecture reconciliation and independently tested guest-sector reader paths for sparse/compressed virtual disks; then final 0.5 beta-scope hardening.

---

## 0.6 Create + Convert + Verify — ⬜ planned

- ⬜ image creation and conversion pipeline
- ⬜ split/join and sparse/compression handling
- ⬜ SHA-256/SHA-512 verification
- ⬜ temporary output + atomic finalization
- ⬜ cancellation/rollback safety

---

## 0.7 Physical Media Tools — ⬜ planned

Future physical-media functionality remains gated behind dedicated safety design, explicit user confirmation, device identity checks and independent validation before it can become user-visible.

---

## 0.8 Windows Integration + Power Tools — ⬜ planned

- ⬜ file associations/context menu
- ⬜ shared-Core CLI
- ⬜ PowerShell-friendly output
- ⬜ session restore/settings import-export/diagnostic export

---

## 0.9 Quality, Security + Beta Hardening — ⬜ planned

- ⬜ expanded provider/integration tests
- ⬜ non-admin UAC/manual drag validation
- ⬜ large/corrupt/truncated/fuzz-style image tests
- ⬜ keyboard/screen-reader/High-DPI/theme review
- ⬜ localization architecture and EN/PL baseline
- ⬜ crash diagnostics/performance profiling
- ⬜ beta regression checklist

---

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
