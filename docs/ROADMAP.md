# Dragon DiskForge — Product Roadmap

This file is the source of truth for project progress. Every meaningful feature change must update the relevant milestone, `CHANGELOG.md`, tests and status documentation when applicable.

## Status legend

- ✅ complete
- 🚧 in progress
- ⬜ planned

---

## 0.1 Foundation + Dragon Visual Identity — ✅ complete

### Core foundation
- ✅ WinUI 3 / .NET 10 desktop shell
- ✅ `DragonDiskForge.Core` separated from GUI
- ✅ Drag & drop + file picker
- ✅ Initial image-format catalogue
- ✅ Signature detection: ISO, VHD, VHDX, QCOW2, DMG, WIM/ESD
- ✅ SHA-256 verification through shared Core service
- ✅ SHA-256 progress reporting and user cancellation
- ✅ Read-only-first architecture
- ✅ Architecture document, changelog and repository hygiene
- ✅ Windows x64 GitHub Actions build validation
- ✅ Core smoke-test harness for signatures, fallback, errors, SHA-256 progress and cancellation
- ✅ Foundation regression pass and future-feature locking

### Dragon visual system
- ✅ Dragon visual language specification and custom sigil
- ✅ Final Windows application icon resource
- ✅ Obsidian / charcoal base palette
- ✅ Ember / molten-metal accent palette
- ✅ Deep-crimson secondary accent
- ✅ Dragon-scale micro-pattern and restrained ember active states
- ✅ Dragon-themed startup overlay and Forge home/drop state
- ✅ Custom Dragon header treatment
- ✅ Fluent/WinUI animations
- ✅ Light theme and system-aware High Contrast resources
- ✅ Responsive layout for compact and wide desktop windows

**Exit criteria — passed:** clean Windows x64 Release build, reliable image detection smoke tests, no fake actions presented as complete, documented architecture, and a recognizably Dragon DiskForge UI rather than a generic WinUI utility.

---

## 0.2 Native Mount + Unmount — ✅ complete

- ✅ `IMountService` contract in Core
- ✅ Native Windows service layer isolated in `DragonDiskForge.Windows`
- ✅ Native Windows mount service for ISO, VHD and VHDX
- ✅ Unmount / eject
- ✅ Drive-letter and mount-state detection
- ✅ Read-only mount mode where supported
- ✅ Elevation policy only when required by the VHD/VHDX native path
- ✅ Mount progress, cancellation and friendly error translation
- ✅ Live Mounted-image dashboard with refresh/open/unmount/cancel
- ✅ Safe stale-state recovery by re-querying Windows
- ✅ Live mounted inventory derived from Windows Storage state
- ✅ Integration tests for disposable VHD/VHDX and IMAPI ISO mount/read/unmount
- ✅ Cancellation-safety and unsupported-format validation
- ✅ Full Windows x64 Release CI regression

**Manual QA note:** GitHub-hosted Windows runners execute as administrators, so the visible normal-user UAC prompt cannot be faithfully exercised in CI. The implemented elevation path has a required desktop checklist in `docs/MANUAL-VALIDATION.md` before public beta packaging.

**Exit criteria — passed:** supported ISO/VHD/VHDX images can be mounted and unmounted reliably from Dragon DiskForge without external manual commands, and the app refreshes from real Windows state rather than trusting stale session state.

---

## 0.3 Dragon Explorer — ✅ complete

### Mounted-volume Explorer — ✅
- ✅ Core Explorer models + `IExplorerService`
- ✅ Safe filesystem-backed browsing for mounted ISO/VHD/VHDX
- ✅ folders-first listing, navigation, Up and breadcrumbs
- ✅ recursive search with cancellation and limits
- ✅ metadata, safe shell-open and executable/script trust warning
- ✅ safe Copy out with overwrite, root-boundary and reparse-point protection
- ✅ copy progress + cancellation
- ✅ mounted dashboard routing backed by live Windows state
- ✅ Core tests and real mounted-ISO list/search/copy integration

### Preview + Image Library — ✅
- ✅ bounded text preview with truncation and cancellation
- ✅ image preview without shell execution
- ✅ PDF/media metadata-only preview
- ✅ binary/unsupported fallback
- ✅ stale-preview cancellation
- ✅ Recent Images + Favorites with atomic persistence and path deduplication
- ✅ real Images view with Open/Favorite/Unfavorite/Remove
- ✅ missing-file handling
- ✅ Core and mounted-ISO preview integration tests

### Mounted history + multi-image workspace — ✅
- ✅ atomic local Mount/Unmount history with bounded retention
- ✅ history independent from authoritative Windows mounted state
- ✅ dedicated Mounted history UI and safe Clear History
- ✅ dedicated history smoke tests
- ✅ WinUI TabView multi-image workspace
- ✅ duplicate-tab prevention, stale-tab pruning and safe close semantics
- ✅ successful unmount closes only workspace tabs backed by that image

### Safe drag-out — ✅
- ✅ native Copy-only drag-out to Windows Explorer/Desktop
- ✅ WinUI `DragStarting` deferral for StorageItem resolution
- ✅ mounted-root containment revalidation
- ✅ stale source and path-escape rejection
- ✅ listed/runtime reparse-point rejection
- ✅ dedicated drag-out safety smoke tests
- ✅ real cross-process pointer gesture retained as explicit manual QA

### Provider-backed direct ISO browsing — ✅
- ✅ `IDirectBrowseProvider` contract
- ✅ managed ISO9660/Joliet parser
- ✅ direct list/navigation/search from image extents
- ✅ safe direct file/folder Copy out with cancellation/progress
- ✅ overwrite, path-traversal, filename and extent validation
- ✅ dedicated `Direct ISO` WinUI tabs marked `NO MOUNT`
- ✅ `Explore directly` enabled only after positive provider detection
- ✅ direct tabs independent from Mount/Unmount lifecycle
- ✅ real IMAPI integration proving list/search/copy while ISO stays detached
- ✅ fake-extension and cancellation regression tests

### 0.3 validation checkpoints
- ✅ PR #5 / run #56 — mounted Explorer
- ✅ PR #6 / run #72 — Preview + Image Library
- ✅ PR #7 / run #86 — history + multi-image workspace
- ✅ PR #10 / run #103 — safe drag-out + x64 artifact
- ✅ PR #12 / run #128 — direct ISO + native regression + WinUI Release + artifact
- ✅ PR #12 / run #131 — full regression after progress synchronization

**Exit criteria — passed:** supported mounted images can be browsed and useful content can be extracted without leaving Dragon DiskForge; ISO9660/Joliet can also be browsed directly without mounting.

---

## 0.4 Extended Image Providers — 🚧 in progress

**Current 0.4 completion: approximately 45%.** Provider foundation plus IMG/RAW, IMA/floppy and BIN/CUE families are real and tested.

### Provider foundation — ✅ complete
- ✅ explicit provider capability reporting
- ✅ deterministic priority and extension-first resolution
- ✅ provider/signature fallback when extension candidates reject an image
- ✅ probe and inspection failure isolation
- ✅ cancellation remains a hard stop across fallback
- ✅ provider diagnostics and duplicate provider-ID protection
- ✅ ISO9660/Joliet direct browsing migrated to registry resolution
- 🚧 stable provider/plugin contract — internal Core contract is usable; public third-party stability remains a later gate

### IMG / RAW partition provider — ✅ complete
- ✅ `.img`, `.raw` and `.dd` registration
- ✅ MBR primary partition parsing
- ✅ EBR logical-partition traversal with loop and count limits
- ✅ GPT primary-header and partition-entry parsing
- ✅ 512-byte and 4096-byte GPT logical-sector probing
- ✅ common MBR/GPT type decoding and GPT UTF-16 partition names
- ✅ strict image-boundary validation
- ✅ malformed/fake-extension rejection and cancellation propagation
- ✅ explicit `PartitionTable` capability
- ✅ no fake Direct Browse, Mount or Convert
- ✅ dedicated Windows CI smoke tests

### IMA / floppy media provider — ✅ complete
- ✅ `.ima` and `.flp` registration
- ✅ standard raw floppy geometries from 160 KB through 2.88 MB
- ✅ blank/unformatted exact-size recognition without fabricated filesystem claims
- ✅ FAT-style BPB parsing when present
- ✅ BPB capacity and CHS validation against real image geometry
- ✅ OEM string, volume label, media descriptor and filesystem hint metadata
- ✅ unsupported-size, malformed-BPB and foreign-extension rejection
- ✅ cancellation propagation
- ✅ explicit `MediaGeometry` capability
- ✅ no fake Direct Browse, Mount or Convert
- ✅ dedicated Windows CI smoke tests

### BIN / CUE track-layout provider — ✅ complete
- ✅ `.cue` plus same-name companion `.bin` resolution
- ✅ `ITrackLayoutProvider` contract and explicit `TrackLayout` capability
- ✅ BINARY CUE parsing
- ✅ AUDIO, MODE1/2048, MODE1/2352, MODE2/2336 and MODE2/2352 track modes
- ✅ single-file and multi-file CUE layouts
- ✅ bounded CUE byte size, line count and track count
- ✅ INDEX 00/01 parsing and time validation
- ✅ BIN existence, non-empty state and sector-alignment validation
- ✅ track start/end range validation against real BIN length
- ✅ strict increasing track numbers and INDEX 01 positions
- ✅ payload paths confined to the CUE directory
- ✅ absolute/path-traversal references rejected
- ✅ unsupported FILE/track types rejected
- ✅ mixed sector sizes inside one BIN rejected instead of guessing byte offsets
- ✅ orphan `.bin` files not claimed without a matching CUE
- ✅ cancellation propagation
- ✅ no fake Direct Browse, Mount or Convert
- ✅ dedicated Windows CI smoke tests
- ✅ PR #18 / run #172 passes BIN/CUE plus the complete previous provider/native/build/artifact regression path

### Remaining image families
- ⬜ MDF/MDS
- ⬜ NRG
- ⬜ CCD/IMG/SUB
- ⬜ VMDK
- ⬜ QCOW/QCOW2
- ⬜ DMG
- ⬜ WIM/ESD
- ⬜ FFU

**Next provider:** **MDF/MDS**.

**Exit criteria:** providers expose consistent, truthful capabilities without turning Core into one monolithic parser.

---

## 0.5 Partitions + File Systems + Image Intelligence — ⬜ planned

> RAW already supplies a proven low-level MBR/GPT parser and floppy supplies validated BPB hints. Milestone 0.5 still owns cross-provider partition intelligence, actual filesystem recognition and the user-facing intelligence layer.

- ⬜ MBR partition-table inspection across supported image providers
- ⬜ GPT partition-table inspection across supported image providers
- ⬜ ISO9660/UDF recognition
- ⬜ FAT/FAT32/exFAT recognition
- ⬜ NTFS metadata inspection where supported
- ⬜ ext-family recognition where supported
- ⬜ Bootability detection
- ⬜ BIOS/UEFI boot detection
- ⬜ Windows installer recognition
- ⬜ Linux installer recognition
- ⬜ Architecture detection: x86/x64/ARM64 where discoverable
- ⬜ Volume labels, UUID/GUID and filesystem metadata
- ⬜ Image health/corruption warnings

---

## 0.6 Create + Convert + Verify — ⬜ planned

- ⬜ Create supported image formats
- ⬜ Conversion pipeline and compatibility matrix
- ⬜ Split/join large images
- ⬜ Compression and sparse-image handling where supported
- ⬜ SHA-256 / SHA-512
- ⬜ MD5 only for legacy verification compatibility
- ⬜ Verify source/output after conversion
- ⬜ Temporary-output + atomic finalization strategy
- ⬜ Cancellation and rollback-safe output

**Exit criteria:** conversion never silently destroys the source and output can be independently verified.

---

## 0.7 Bootable USB + Physical Media Tools — ⬜ planned

- ⬜ Physical disk enumeration
- ⬜ Strong target-disk identification
- ⬜ Bootable USB and Windows/Linux image-writing workflows
- ⬜ Destructive-operation confirmation screen
- ⬜ explicit disk-size/model/serial confirmation
- ⬜ write progress, safe cancellation and post-write verification
- ⬜ prevent accidental system-disk selection where possible

**Exit criteria:** destructive operations are difficult to trigger accidentally and always identify the target clearly.

---

## 0.8 Windows Integration + Power Tools — ⬜ planned

- ⬜ File associations
- ⬜ "Open with Dragon DiskForge"
- ⬜ Windows context-menu integration
- ⬜ CLI using the same Core engine
- ⬜ `dragon mount`, `dragon explore`, `dragon verify`, `dragon convert`
- ⬜ optional PowerShell-friendly output
- ⬜ session restore
- ⬜ settings import/export
- ⬜ diagnostic log export

---

## 0.9 Quality, Security + Beta Hardening — ⬜ planned

- ⬜ comprehensive Core/provider automated tests
- ⬜ expanded mount/unmount integration tests
- ⬜ non-admin UAC desktop validation matrix
- ⬜ cross-process Explorer drag-out desktop validation matrix
- ⬜ large-image and multi-terabyte sparse-image stress tests where feasible
- ⬜ corrupt/truncated image tests
- ⬜ fuzz-style parser robustness testing for untrusted metadata
- ⬜ keyboard-first navigation
- ⬜ screen-reader/accessibility review
- ⬜ High-DPI and light/dark/system theme testing
- ⬜ localization architecture
- ⬜ English and Polish baseline
- ⬜ crash handling with privacy-preserving report export
- ⬜ performance and memory profiling
- ⬜ beta regression checklist

---

## 1.0 Production Release — ⬜ planned

- ⬜ Final Dragon UI/UX pass
- ⬜ Signed Windows installer
- ⬜ Portable build if technically appropriate
- ⬜ GitHub Actions release pipeline
- ⬜ Automatic-update strategy
- ⬜ Stable provider API
- ⬜ Versioned configuration migration
- ⬜ Full user documentation
- ⬜ Supported-format/capability matrix
- ⬜ Troubleshooting guide
- ⬜ Release checklist
- ⬜ Regression suite green
- ⬜ GitHub Release with binaries and checksums

**1.0 definition of done:** Dragon DiskForge is visually distinctive, safe, installable, documented and genuinely useful for mounting, browsing, verifying and managing supported disk images.

---

## Post-1.0 candidates

These are intentionally not allowed to delay 1.0:

- ⬜ Plugin SDK for third-party providers
- ⬜ More filesystems and forensic read-only inspection
- ⬜ Batch conversion queue
- ⬜ Network image sources
- ⬜ Image comparison/diff tools
- ⬜ Advanced repair workflows only where technically safe

---

## GitHub delivery workflow

Every substantial feature follows this order:

1. Define scope/capabilities.
2. Implement in Core when logic is non-visual.
3. Connect the WinUI layer.
4. Add failure/cancellation paths.
5. Test the real operation.
6. Update docs.
7. Update `CHANGELOG.md`.
8. Update this roadmap status.
9. Only then present the feature as complete.

## Non-negotiable project rules

1. Never mark a UI action as working when the underlying engine is still a placeholder.
2. Read-only inspection is the safe default.
3. Destructive operations require explicit target validation and confirmation.
4. UI stays separate from `DragonDiskForge.Core`.
5. New formats are added through providers/capabilities rather than one monolithic parser.
6. `ROADMAP.md`, `CHANGELOG.md`, README progress and status docs stay synchronized with meaningful checkpoints.
7. Large disk-image test files are never committed to the repository.
8. Dragon styling must remain recognizable but must never reduce readability or accessibility.
9. All dangerous operations must surface exactly what device/file will be changed before execution.
10. A milestone is complete only after its exit criteria are met.
