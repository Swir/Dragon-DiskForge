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

### IMG / RAW partition provider — ✅ complete
- ✅ MBR/EBR/GPT
- ✅ strict bounds
- ✅ `PartitionTable`

### IMA / floppy provider — ✅ complete
- ✅ standard media geometry
- ✅ FAT-style BPB capacity/CHS validation
- ✅ `MediaGeometry`

### BIN / CUE provider — ✅ complete
- ✅ AUDIO/MODE1/MODE2 BINARY CUE layouts
- ✅ INDEX/payload/range safety
- ✅ `TrackLayout`

### MDF / MDS CD provider — ✅ complete
- ✅ bounded descriptor/session/track metadata
- ✅ explicit MDF offsets
- ✅ safe footer payload resolution

### NRG v1/v2 provider — ✅ complete
- ✅ NERO/NER5
- ✅ CUES/CUEX
- ✅ DAOI/DAOX
- ✅ END! and cue/DAO consistency

### CCD / IMG / SUB provider — ✅ complete
- ✅ CloneCD MODE/INDEX
- ✅ 2352-byte IMG alignment
- ✅ optional 96-byte SUB validation
- ✅ safe CCD-vs-RAW `.img` fallback

### VMDK sparse metadata provider — ✅ complete
- ✅ hosted sparse v1 magic/header validation
- ✅ capacity/grain/descriptor/GD metadata
- ✅ physical-file bounds and descriptor limits
- ✅ `VirtualDiskMetadata`
- ✅ no guest-sector translation/browse/mount/convert

### QCOW / QCOW2 metadata provider — ✅ complete
- ✅ QCOW v1 and QCOW2 v2/v3 big-endian metadata
- ✅ L1/refcount/snapshot/feature validation
- ✅ backing filename bounded and never followed
- ✅ no cluster translation/virtual-sector I/O/browse/mount/convert

### DMG / UDIF metadata provider — ✅ complete
- ✅ `.dmg` single-file UDIF detection through trailing `koly`
- ✅ bounded big-endian trailer and plist metadata
- ✅ no `blkx` decompression or filesystem browsing

### WIM / ESD metadata provider — ✅ complete
- ✅ standalone Part 1/1 WIM/ESD metadata
- ✅ bounded 208-byte header and resource descriptors
- ✅ `ContainerMetadata`
- ✅ no resource decompression/file-tree extraction

### FFU metadata provider — ✅ complete
- ✅ bounded common `SignedImage ` / `ImageFlash ` metadata
- ✅ chunk/catalog/hash/manifest/store bounds
- ✅ `ContainerMetadata`
- ✅ no write-destination interpretation or device writes

### Final 0.4 contract gate — ✅ complete
- ✅ invalid provider declarations fail fast
- ✅ descriptor metadata is snapshotted and immutable for resolution
- ✅ equal-priority selection is deterministic
- ✅ existing fallback, diagnostics, failure isolation and cancellation remain proven
- ✅ PR #27 / run #228 passed the full provider/native/build/artifact regression path before documentation synchronization

**0.4 exit criteria: PASSED.** Closing this milestone hardens the internal provider contract; it does not promise a stable public plugin API.

---

## 0.5 Partitions + File Systems + Image Intelligence — ⬜ next

- ⬜ cross-provider partition intelligence
- ⬜ ISO9660/UDF
- ⬜ FAT/FAT32/exFAT
- ⬜ NTFS metadata where supported
- ⬜ ext-family recognition
- ⬜ bootability + BIOS/UEFI detection
- ⬜ Windows/Linux installer recognition
- ⬜ architecture, labels and UUID/GUID metadata
- ⬜ health/corruption warnings

**0.5 entry rule:** build on provider capabilities rather than format-specific UI shortcuts. New filesystem/intelligence features remain disabled until their real engine path and tests exist.

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
