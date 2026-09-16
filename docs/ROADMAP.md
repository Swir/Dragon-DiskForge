# Dragon DiskForge — Product Roadmap

This file is the source of truth for project progress. Every meaningful feature change must update the relevant milestone, `CHANGELOG.md`, tests and status documentation when applicable.

## Status legend

- ✅ complete
- 🚧 in progress
- ⬜ planned

---

## 0.1 Foundation + Dragon Visual Identity — ✅ complete

- ✅ WinUI 3 / .NET 10 desktop shell
- ✅ `DragonDiskForge.Core` separated from GUI
- ✅ drag & drop + file picker
- ✅ initial image-format catalogue and signature detection
- ✅ SHA-256 verification with progress/cancellation
- ✅ read-only-first architecture
- ✅ Windows x64 CI and Core smoke tests
- ✅ Dragon visual system, responsive layout, accessible themes and final Windows icon

**Exit criteria — passed.**

---

## 0.2 Native Mount + Unmount — ✅ complete

- ✅ native Windows ISO/VHD/VHDX mount/unmount
- ✅ read-only default
- ✅ drive-letter and attached-state detection
- ✅ progress/cancellation and friendly errors
- ✅ live Mounted state refreshed from Windows
- ✅ disposable VHD/VHDX/ISO integration tests

**Manual QA:** normal-user UAC interaction remains documented in `docs/MANUAL-VALIDATION.md`.

**Exit criteria — passed.**

---

## 0.3 Dragon Explorer — ✅ complete

### Mounted-volume Explorer — ✅
- ✅ list/navigation/breadcrumbs/metadata
- ✅ recursive search with cancellation
- ✅ safe Copy out with overwrite and reparse-point protection
- ✅ mounted ISO integration

### Preview + Image Library — ✅
- ✅ bounded text/image/PDF/media preview modes
- ✅ stale-preview cancellation
- ✅ Recent Images + Favorites with atomic persistence

### Mounted history + multi-image workspace — ✅
- ✅ local history independent from authoritative Windows state
- ✅ WinUI TabView workspace
- ✅ duplicate-tab prevention and stale-tab pruning

### Safe drag-out — ✅
- ✅ native Copy-only drag-out to Windows Explorer/Desktop
- ✅ root containment and reparse-point revalidation
- ✅ dedicated safety tests

### Provider-backed direct ISO browsing — ✅
- ✅ `IDirectBrowseProvider`
- ✅ managed ISO9660/Joliet parser
- ✅ direct list/navigation/search/Copy out without mounting
- ✅ extent/path/filename validation
- ✅ real IMAPI integration

**Exit criteria — passed.**

---

## 0.4 Extended Image Providers — 🚧 in progress

**Current 0.4 completion: approximately 71%.** Provider foundation plus IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS CD, NRG, CCD/IMG/SUB and VMDK sparse metadata are real and tested.

### Provider foundation — ✅ complete
- ✅ explicit provider capability reporting
- ✅ deterministic priority and extension-first resolution
- ✅ signature/provider fallback
- ✅ probe and inspection failure isolation
- ✅ cancellation hard-stop semantics
- ✅ provider diagnostics and duplicate-ID protection
- ✅ ISO9660/Joliet direct browsing through the registry
- 🚧 public stable plugin/provider contract remains a later stability gate

### IMG / RAW partition provider — ✅ complete
- ✅ `.img`, `.raw`, `.dd`
- ✅ MBR primary partitions
- ✅ bounded EBR logical traversal
- ✅ GPT with 512/4096 logical-sector probing
- ✅ partition type/name metadata
- ✅ strict image bounds
- ✅ `PartitionTable` capability
- ✅ no fake Direct Browse, Mount or Convert

### IMA / floppy media provider — ✅ complete
- ✅ `.ima`, `.flp`
- ✅ standard raw floppy geometries
- ✅ blank/unformatted exact-size recognition
- ✅ FAT-style BPB parsing
- ✅ capacity and CHS validation
- ✅ `MediaGeometry` capability
- ✅ no fake Direct Browse, Mount or Convert

### BIN / CUE track-layout provider — ✅ complete
- ✅ BINARY CUE parsing
- ✅ AUDIO, MODE1/2048, MODE1/2352, MODE2/2336, MODE2/2352
- ✅ single-file and multi-file layouts
- ✅ bounded INDEX 00/01 and payload ranges
- ✅ path containment
- ✅ ambiguous mixed-sector single-BIN layouts rejected
- ✅ `TrackLayout` capability

### MDF / MDS CD track-layout provider — ✅ complete
- ✅ `MEDIA DESCRIPTOR` signature/version/medium validation
- ✅ bounded session/track structures
- ✅ explicit MDF byte offsets
- ✅ safe ASCII/UTF-16 footer payload resolution
- ✅ DVD-style MDS intentionally rejected until separately proven

### NRG v1/v2 track-layout provider — ✅ complete
- ✅ `NERO` v1 and `NER5` v2 footer parsing
- ✅ CUES BCD MSF and CUEX signed-LBA positions
- ✅ DAOI/DAOX byte ranges
- ✅ cue/DAO consistency checks
- ✅ terminating empty `END!` required

### CCD / IMG / SUB track-layout provider — ✅ complete
- ✅ CloneCD `.ccd`, same-name `.img`, validated `.sub`
- ✅ MODE 0/1/2 and INDEX 0/1 handling
- ✅ 2352-byte IMG alignment
- ✅ optional SUB exactly 96 bytes per IMG sector
- ✅ CCD takes precedence over RAW `.img` only after valid same-name descriptor detection
- ✅ malformed/unsupported metadata rejected rather than guessed

### VMDK sparse metadata provider — ✅ complete
- ✅ `.vmdk` hosted sparse header version 1
- ✅ `IVirtualDiskMetadataProvider` and `VirtualDiskMetadata` capability
- ✅ sparse magic `0x564D444B` and version validation
- ✅ flags, virtual capacity and grain-size parsing
- ✅ embedded descriptor offset/size parsing
- ✅ grain-table entry count, redundant grain-directory offset, grain-directory offset and overhead metadata
- ✅ sector-offset overflow and physical-file bounds validation
- ✅ unclean-shutdown/newline/compression consistency checks
- ✅ embedded descriptor limited to 1 MiB with line/line-length bounds
- ✅ descriptor version/createType/CID/parentCID/extent-count metadata
- ✅ conflicting/malformed descriptor metadata rejected
- ✅ text-only VMDK descriptors and unproven sparse-header versions not claimed
- ✅ cancellation propagation
- ✅ no grain-table translation, virtual-sector reads, Direct Browse, Mount or Convert
- ✅ PR #22 / run #200 passed VMDK plus the complete previous provider/native/build/artifact path before documentation synchronization

### Remaining image families
- ⬜ QCOW/QCOW2
- ⬜ DMG
- ⬜ WIM/ESD
- ⬜ FFU

**Next provider:** **QCOW/QCOW2**.

**Exit criteria:** providers expose consistent, truthful capabilities without turning Core into one monolithic parser.

---

## 0.5 Partitions + File Systems + Image Intelligence — ⬜ planned

> RAW already supplies proven MBR/GPT parsing and floppy supplies validated BPB hints. 0.5 owns cross-provider filesystem/content intelligence.

- ⬜ partition-table inspection across supported virtual-disk providers
- ⬜ ISO9660/UDF recognition
- ⬜ FAT/FAT32/exFAT recognition
- ⬜ NTFS metadata inspection where supported
- ⬜ ext-family recognition where supported
- ⬜ bootability and BIOS/UEFI detection
- ⬜ Windows/Linux installer recognition
- ⬜ architecture detection where discoverable
- ⬜ labels, UUID/GUID and filesystem metadata
- ⬜ image health/corruption warnings

---

## 0.6 Create + Convert + Verify — ⬜ planned

- ⬜ create supported image formats
- ⬜ conversion compatibility matrix and pipeline
- ⬜ split/join large images
- ⬜ compression and sparse handling where supported
- ⬜ SHA-256 / SHA-512
- ⬜ MD5 only for legacy compatibility
- ⬜ post-conversion verification
- ⬜ temporary output + atomic finalization
- ⬜ cancellation and rollback-safe output

**Exit criteria:** conversion never silently destroys the source and output can be independently verified.

---

## 0.7 Bootable USB + Physical Media Tools — ⬜ planned

- ⬜ physical disk enumeration
- ⬜ strong target-disk identification
- ⬜ bootable USB and image-writing workflows
- ⬜ destructive-operation confirmation
- ⬜ disk size/model/serial confirmation
- ⬜ write progress, cancellation and verification
- ⬜ system-disk protection where possible

---

## 0.8 Windows Integration + Power Tools — ⬜ planned

- ⬜ file associations
- ⬜ Open with Dragon DiskForge
- ⬜ Windows context-menu integration
- ⬜ CLI using the shared Core engine
- ⬜ `dragon mount`, `dragon explore`, `dragon verify`, `dragon convert`
- ⬜ PowerShell-friendly output
- ⬜ session restore
- ⬜ settings import/export
- ⬜ diagnostic log export

---

## 0.9 Quality, Security + Beta Hardening — ⬜ planned

- ⬜ comprehensive provider tests
- ⬜ expanded mount/unmount integration matrix
- ⬜ non-admin UAC desktop validation
- ⬜ cross-process Explorer drag-out validation
- ⬜ large/sparse image stress tests
- ⬜ corrupt/truncated image cases
- ⬜ fuzz-style parser robustness
- ⬜ keyboard/screen-reader/accessibility review
- ⬜ High-DPI and theme testing
- ⬜ localization architecture with English/Polish baseline
- ⬜ privacy-preserving crash report export
- ⬜ performance/memory profiling
- ⬜ beta regression checklist

---

## 1.0 Production Release — ⬜ planned

- ⬜ final Dragon UI/UX pass
- ⬜ signed Windows installer
- ⬜ portable build if appropriate
- ⬜ GitHub Actions release pipeline
- ⬜ automatic-update strategy
- ⬜ stable provider API
- ⬜ configuration migration
- ⬜ full user documentation
- ⬜ supported-format/capability matrix
- ⬜ troubleshooting guide
- ⬜ release checklist
- ⬜ full regression suite green
- ⬜ GitHub Release with binaries and checksums

**1.0 definition of done:** Dragon DiskForge is visually distinctive, safe, installable, documented and genuinely useful for mounting, browsing, verifying and managing supported disk images.

---

## Post-1.0 candidates

- ⬜ Plugin SDK for third-party providers
- ⬜ additional filesystems and forensic read-only inspection
- ⬜ batch conversion queue
- ⬜ network image sources
- ⬜ image comparison/diff tools
- ⬜ advanced repair workflows only where technically safe

---

## GitHub delivery workflow

1. Define scope/capabilities.
2. Implement non-visual logic in Core.
3. Connect WinUI only when truthful.
4. Add failure/cancellation paths.
5. Test the real operation.
6. Update docs and changelog.
7. Update roadmap/progress only after validation.
8. Present the feature as complete only after its exit gate passes.

## Non-negotiable project rules

1. Never mark a UI action as working when the engine is still a placeholder.
2. Read-only inspection is the safe default.
3. Destructive operations require explicit target validation and confirmation.
4. UI stays separate from `DragonDiskForge.Core`.
5. New formats are added through providers/capabilities rather than a monolithic parser.
6. README, ROADMAP, STATUS, MILESTONES and CHANGELOG stay synchronized.
7. Large disk-image test files are never committed.
8. Dragon styling must never reduce readability or accessibility.
9. Dangerous operations must state exactly what device/file will change.
10. A milestone is complete only after its exit criteria are met.
