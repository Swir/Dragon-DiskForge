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

### Provider foundation + hardening — ✅ complete
- ✅ explicit capability reporting
- ✅ deterministic priority/extension-first resolution
- ✅ deterministic provider-ID tie-breaking for equal priorities
- ✅ signature/provider fallback
- ✅ probe/inspection failure isolation
- ✅ cancellation hard stop
- ✅ diagnostics and duplicate-ID protection
- ✅ provider descriptor snapshots at registration time
- ✅ validated provider IDs and extension declarations
- ✅ immutable normalized descriptor extensions
- ✅ intentional signature-only provider support
- ✅ safe display-name fallback

### Proven 0.4 image families
- ✅ IMG / RAW partition metadata
- ✅ IMA / floppy media metadata
- ✅ BIN / CUE track layout
- ✅ MDF / MDS CD track layout
- ✅ NRG v1/v2 track layout
- ✅ CCD / IMG / SUB track layout
- ✅ VMDK sparse v1 metadata
- ✅ QCOW / QCOW2 metadata
- ✅ DMG / UDIF metadata
- ✅ WIM / ESD container metadata
- ✅ FFU container metadata

**0.4 exit criteria: PASSED.** Closing this milestone hardens the internal provider contract; it does not promise a stable public plugin API.

---

## 0.5 Partitions + File Systems + Image Intelligence — 🚧 in progress

**Current 0.5 completion: approximately 11%.** The first capability-driven intelligence slice is implemented and validated.

### Cross-provider partition intelligence — ✅ complete
- ✅ provider-agnostic `PartitionIntelligenceService`
- ✅ resolves through `ProviderRegistry` + `PartitionTable` capability rather than format-specific shortcuts
- ✅ stable structural finding codes and severities
- ✅ duplicate partition-index detection
- ✅ zero-length partition detection
- ✅ start/end LBA overflow detection
- ✅ provider-reported byte offset/size consistency checks
- ✅ physical image-bound validation
- ✅ overlapping partition-range detection
- ✅ bootable partition count preserved without claiming filesystem health
- ✅ capability-driven fake providers in dedicated smoke tests
- ✅ read-only only; no writes, repairs or mounting
- ✅ PR #28 / run #231 passed this slice plus the complete existing provider/native/build/artifact regression path before documentation synchronization

### Remaining 0.5 scope
- ⬜ ISO9660/UDF filesystem recognition and metadata
- ⬜ FAT/FAT32/exFAT recognition and metadata
- ⬜ NTFS metadata where supported
- ⬜ ext-family recognition
- ⬜ bootability + BIOS/UEFI detection
- ⬜ Windows/Linux installer recognition
- ⬜ architecture, labels and UUID/GUID metadata
- ⬜ health/corruption warnings

**0.5 entry rule:** build on provider capabilities rather than format-specific UI shortcuts. New filesystem/intelligence features remain disabled until their real engine path and tests exist.

**Next engineering focus:** bounded filesystem recognition/metadata, beginning with signatures and structures that can be validated read-only without mounting.

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
