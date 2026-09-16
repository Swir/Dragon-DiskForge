# Dragon DiskForge — Product Roadmap

This file is the source of truth for project progress. Meaningful feature changes must update tests, `CHANGELOG.md`, status and progress documentation.

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

**Current 0.5 completion: approximately 30%.** Cross-provider partition intelligence and the bounded filesystem-recognition foundation are implemented and validated.

### Cross-provider partition intelligence — ✅ complete
- ✅ provider-agnostic `PartitionIntelligenceService`
- ✅ resolves through `ProviderRegistry` + `PartitionTable`
- ✅ duplicate-index, zero-length, arithmetic-overflow, geometry, physical-bound and overlap findings
- ✅ stable finding codes/severities
- ✅ capability-driven fake providers in dedicated smoke tests
- ✅ no writes, repairs or fake filesystem-health claims
- ✅ PR #28 / final docs-synchronized run #232 passed full Windows regression/build/artifact

### Bounded filesystem-recognition foundation — ✅ complete
- ✅ provider-integrated `FileSystemRecognitionService`
- ✅ scans whole physical images only when that byte mapping is truthful
- ✅ partition-capable providers are scanned only through structurally validated physical partition ranges
- ✅ FAT12/FAT16/FAT32 bounded BPB recognition and metadata
- ✅ exFAT bounded boot metadata
- ✅ NTFS bounded boot metadata where supported
- ✅ ext2/ext3/ext4 superblock recognition, label, UUID and block size
- ✅ ISO9660/Joliet descriptor recognition and volume label
- ✅ UDF VRS recognition (`BEA01` / `NSR02|NSR03` / `TEA01`)
- ✅ generated sparse fixtures, false-positive checks and cancellation coverage
- ✅ structurally invalid partition layouts rejected before filesystem probing
- ✅ explicit refusal to pretend sparse/compressed virtual-disk guest sectors are physical bytes
- ✅ PR #29 / run #234 passed this code slice plus the complete existing provider/native/build/artifact regression before documentation synchronization

### Filesystem family depth
- 🚧 ISO9660/UDF — ISO direct browsing already proven; bounded ISO/Joliet and UDF recognition now proven; richer UDF metadata/traversal remains
- 🚧 FAT/FAT32/exFAT — recognition/geometry proven; deeper reader functionality remains
- 🚧 NTFS metadata — bounded boot metadata proven; deeper supported metadata remains
- ✅ ext-family recognition — ext2/ext3/ext4 recognition and basic metadata proven

### Remaining 0.5 scope
- ⬜ bootability + BIOS/UEFI detection
- ⬜ Windows/Linux installer recognition
- ⬜ architecture and cross-source labels/UUID/GUID aggregation
- ⬜ health/corruption warnings grounded in proven metadata checks
- ⬜ virtual guest-sector reader paths before filesystems inside sparse/compressed virtual disks can be analyzed

**0.5 rule:** image intelligence must build on truthful provider byte mappings/capabilities. No format-specific UI shortcut may imply access the Core cannot actually perform.

**Next engineering focus:** bootability/BIOS/UEFI and installer intelligence using existing provider + direct-browse evidence, while continuing to deepen filesystem metadata safely.

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
