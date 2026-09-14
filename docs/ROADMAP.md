# Dragon DiskForge — Product Roadmap

This file is the source of truth for project progress. Every meaningful feature change must update the relevant milestone, `CHANGELOG.md`, and tests/docs when applicable.

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
- ✅ Architecture document
- ✅ Changelog and repository hygiene
- ✅ Windows x64 GitHub Actions build validation
- ✅ Core smoke-test harness for signatures, fallback, errors, SHA-256 progress and cancellation
- ✅ Foundation regression pass and future-feature locking

### Dragon visual system
- ✅ Dragon visual language specification
- ✅ Custom Dragon DiskForge dragon-head/sigil
- ✅ Final Windows application icon resource
- ✅ Obsidian / charcoal base palette
- ✅ Ember / molten-metal accent palette
- ✅ Deep-crimson secondary accent
- ✅ Dragon-scale micro-pattern used subtly in selected surfaces
- ✅ Subtle fire/ember active-state treatment
- ✅ Dragon-themed startup overlay
- ✅ Branded Forge home/drop state
- ✅ Custom Dragon header treatment
- ✅ Fluent/WinUI animations with a restrained Dragon character
- ✅ Light theme preserving the Dragon identity
- ✅ System-aware High Contrast resources
- ✅ Responsive layout for compact and wide desktop windows

**Exit criteria — passed:** clean Windows x64 Release build, reliable image detection smoke tests, no fake actions presented as complete, documented architecture, and a recognizably Dragon DiskForge UI rather than a generic WinUI utility.

---

## 0.2 Native Mount + Unmount — ✅ complete

- ✅ `IMountService` contract in Core
- ✅ Native Windows service layer isolated in `DragonDiskForge.Windows`
- ✅ Native Windows mount service for ISO
- ✅ Native Windows mount service for VHD/VHDX
- ✅ Unmount / eject
- ✅ Drive-letter and mount-state detection
- ✅ Read-only mount mode where supported
- ✅ Elevation policy only when required by the VHD/VHDX native path
- ✅ Mount operation progress + cancellation
- ✅ Mount error translation into user-friendly messages
- ✅ Live Mounted-image dashboard
- ✅ Refresh / Open drive / Unmount + Cancel from Mounted view
- ✅ Safe recovery from stale mount state by re-querying Windows
- ✅ Live mounted inventory derived from Windows Storage state
- ✅ Integration tests for real VHD/VHDX creation, mount, read-only state, drive detection and unmount
- ✅ Integration test for a real IMAPI-generated ISO, mounted file access and unmount
- ✅ Integration validation that mounted images appear in inventory and disappear after unmount
- ✅ Cancellation-safety validation proving a pre-cancelled mount does not alter storage state
- ✅ Unsupported-format friendly-error validation
- ✅ Full Windows x64 Release CI green on current main through run #37

**Manual QA note:** GitHub-hosted Windows runners execute as administrators, so the visible normal-user UAC prompt cannot be faithfully exercised in CI. The implemented elevation path has a required desktop checklist in `docs/MANUAL-VALIDATION.md` before public beta packaging.

**Exit criteria — passed:** supported ISO/VHD/VHDX images can be mounted and unmounted reliably from Dragon DiskForge without external manual commands, and the app refreshes from real Windows state rather than trusting stale session state.

---

## 0.3 Dragon Explorer — 🚧 next

- ⬜ In-app folder/file tree
- ⬜ Address/breadcrumb navigation
- ⬜ Search inside opened image
- ⬜ File details and properties
- ⬜ Open files from mounted/provider-backed images
- ⬜ Safe extraction of files/folders
- ⬜ Drag files out to Explorer where technically safe
- ⬜ Preview framework for images/text/PDF/media metadata
- ⬜ Recent images
- ⬜ Favorites
- ⬜ Mounted history
- ⬜ Multi-image workspace/tabs

**Exit criteria:** a user can inspect and extract useful content from an image without leaving Dragon DiskForge.

---

## 0.4 Extended Image Providers — ⬜ planned

- ⬜ IMG / RAW partition parser
- ⬜ IMA / floppy images
- ⬜ BIN/CUE
- ⬜ MDF/MDS
- ⬜ NRG
- ⬜ CCD/IMG/SUB
- ⬜ VMDK
- ⬜ QCOW/QCOW2
- ⬜ DMG
- ⬜ WIM/ESD
- ⬜ FFU
- ⬜ Stable provider/plugin contract
- ⬜ Capability reporting per provider
- ⬜ Provider fallback chain
- ⬜ Provider isolation/error containment

**Exit criteria:** providers expose consistent capabilities without turning Core into one monolithic parser.

---

## 0.5 Partitions + File Systems + Image Intelligence — ⬜ planned

- ⬜ MBR partition-table inspection
- ⬜ GPT partition-table inspection
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
- ⬜ Conversion pipeline
- ⬜ Conversion compatibility matrix
- ⬜ Split/join large images
- ⬜ Compression options where supported
- ⬜ Sparse-image handling where supported
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
- ⬜ Bootable USB workflow
- ⬜ Windows/Linux image writing workflow
- ⬜ Destructive-operation confirmation screen
- ⬜ Explicit disk-size/model/serial confirmation
- ⬜ Write progress and verification
- ⬜ Safe cancellation rules
- ⬜ Post-write verification
- ⬜ Prevent accidental system-disk selection where possible

**Exit criteria:** destructive operations are difficult to trigger accidentally and always identify the target clearly.

---

## 0.8 Windows Integration + Power Tools — ⬜ planned

- ⬜ File associations
- ⬜ "Open with Dragon DiskForge"
- ⬜ Windows context-menu integration
- ⬜ CLI using the same Core engine
- ⬜ `dragon mount`, `dragon explore`, `dragon verify`, `dragon convert`
- ⬜ Optional PowerShell-friendly output
- ⬜ Session restore
- ⬜ Settings import/export
- ⬜ Diagnostic log export

---

## 0.9 Quality, Security + Beta Hardening — ⬜ planned

- ⬜ Comprehensive automated Core unit tests
- ⬜ Provider tests
- ⬜ Mount/unmount integration tests expansion
- ⬜ Non-admin UAC desktop validation matrix
- ⬜ Large-image stress tests
- ⬜ Multi-terabyte sparse-image tests where feasible
- ⬜ Corrupt/truncated image tests
- ⬜ Fuzz-style parser robustness testing for untrusted image metadata
- ⬜ Keyboard-first navigation
- ⬜ Screen-reader/accessibility review
- ⬜ High-DPI testing
- ⬜ Light/dark/system theme testing
- ⬜ Localization architecture
- ⬜ English baseline
- ⬜ Polish baseline
- ⬜ Crash handling with privacy-preserving report export
- ⬜ Performance and memory profiling
- ⬜ Beta regression checklist

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
6. `ROADMAP.md` and `CHANGELOG.md` must stay synchronized with meaningful milestones/releases.
7. Large disk-image test files are never committed to the repository.
8. Dragon styling must remain recognizable but must never reduce readability or accessibility.
9. All dangerous operations must surface exactly what device/file will be changed before execution.
10. A milestone is complete only after its exit criteria are met.
