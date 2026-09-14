# Dragon DiskForge roadmap

This roadmap is the source of truth for project progress. Every meaningful feature commit should update the relevant milestone when its status changes.

## Status legend

- ✅ complete
- 🚧 in progress
- ⬜ planned

## 0.1 Foundation — 🚧 current

- ✅ WinUI 3 / .NET 10 desktop shell
- ✅ Core separated from GUI
- ✅ Drag & drop + file picker
- ✅ Format catalogue
- ✅ Signature detection: ISO, VHD, VHDX, QCOW2, DMG, WIM/ESD
- ✅ SHA-256 verification
- ✅ Modern dashboard foundation
- ✅ Architecture document
- ✅ Changelog and repository hygiene
- 🚧 UI polish, error states and foundation testing

**Exit criteria:** clean build on Windows, reliable image detection, no fake actions presented as complete, and documented architecture.

## 0.2 Mount + Explorer — ⬜ next

- ⬜ Native Windows mount service for ISO/VHD/VHDX
- ⬜ Unmount / eject
- ⬜ Drive-letter and mount-state detection
- ⬜ Read-only mode where supported
- ⬜ In-app tree/file explorer
- ⬜ Open files from mounted or provider-backed images
- ⬜ Safe extraction from an image
- ⬜ Recent images + mounted history
- ⬜ Clear privilege/elevation handling

**Exit criteria:** a user can open a supported image, inspect it, mount it, browse files and unmount it without using external tools.

## 0.3 Extended providers — ⬜ planned

- ⬜ IMG/RAW partition parser
- ⬜ BIN/CUE
- ⬜ MDF/MDS
- ⬜ NRG / CCD
- ⬜ VMDK
- ⬜ QCOW/QCOW2 provider
- ⬜ DMG provider
- ⬜ WIM/ESD/FFU provider
- ⬜ Stable provider/plugin contract
- ⬜ Capability reporting per provider

## 0.4 Create + Convert — ⬜ planned

- ⬜ Image creation
- ⬜ Format conversion pipeline
- ⬜ Split/join images
- ⬜ Compression options where supported
- ⬜ Bootability inspection
- ⬜ Windows/Linux installer recognition
- ⬜ Conversion validation and rollback-safe output

## 0.5 USB + Power tools — ⬜ planned

- ⬜ Bootable USB workflow with destructive-action safeguards
- ⬜ Hash library: SHA-256/SHA-512/MD5 (MD5 for legacy verification only)
- ⬜ Image repair/validation where technically supported
- ⬜ Windows context-menu integration
- ⬜ CLI foundation sharing the same Core engine

## 0.6 Quality + Platform integration — ⬜ planned

- ⬜ Automated Core tests
- ⬜ Large-image stress tests
- ⬜ Keyboard-first navigation
- ⬜ Accessibility review
- ⬜ Light/dark/system themes
- ⬜ File association options
- ⬜ Localization architecture and English/Polish baseline
- ⬜ Structured diagnostic log export

## 1.0 Production release — ⬜ planned

- ⬜ Signed Windows installer
- ⬜ Release build pipeline
- ⬜ Automatic update strategy
- ⬜ Stable provider API
- ⬜ Performance pass
- ⬜ Crash handling/report export with privacy controls
- ⬜ Full user documentation
- ⬜ Release checklist and regression suite

## Non-negotiable project rules

1. Never mark a UI action as working when the underlying engine is still a placeholder.
2. Read-only inspection is the safe default.
3. Destructive operations require explicit target validation and confirmation.
4. UI stays separate from `DragonDiskForge.Core`.
5. New formats are added through providers/capabilities rather than one monolithic parser.
6. `ROADMAP.md` and `CHANGELOG.md` are updated with meaningful milestones and releases.
7. Large disk-image test files are never committed to the repository.
