# 🐉 Dragon DiskForge

**Universal Disk Image Manager for Windows**

Dragon DiskForge is a WinUI 3 / .NET 10 desktop application and shared Core toolkit for inspecting, mounting, exploring, verifying and analyzing disk images. The project follows a strict truthful-capability rule: unsupported actions remain disabled until a real engine path exists and is verified.

## Current development version — 0.5.0-alpha.1

The public beta suffix is intentionally not promoted until the independent gate in [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md) passes. Engineering work may continue beyond the 0.5 beta scope without weakening that release gate.

## Project progress — 76% toward 1.0

`███████████████░░░░░ 76%`

**Overall completion:** **76%**

- `0.1 Foundation + Dragon UI` — **100%** ✅
- `0.2 Native Mount + Unmount` — **100%** ✅
- `0.3 Dragon Explorer` — **100%** ✅
- `0.4 Extended Image Providers` — **100%** ✅
- `0.5 Partitions + File Systems + Image Intelligence` — **100%** ✅
- `0.6 Create + Convert + Verify` — **100%** ✅
- `0.7 Physical Media Tools` — **~86% (6/7)** 🚧
- `0.8 Windows Integration + Power Tools` — **50% (2/4)** 🚧
- `0.9 Quality, Security + Beta Hardening` — planned
- `1.0 Production Release` — planned

> Progress changes only after meaningful implementation and validation checkpoints. CI count, documentation-only edits and placeholders do not increase completion. Work may advance out of milestone order when a remaining milestone is blocked by a real hardware/manual gate.

## Proven product foundation

- native Windows ISO/VHD/VHDX read-only-first Mount + Unmount
- mounted-volume Dragon Explorer with navigation, search, Preview and safe Copy out
- Recent Images, Favorites, mount history and multi-image workspace
- safe Copy-only drag-out to Windows Explorer/Desktop
- managed ISO9660/Joliet direct browsing without mounting
- hardened canonical provider registry with truthful capabilities and deterministic resolution
- IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU provider coverage
- bounded partition, filesystem, boot/install and image-intelligence services
- bounded QCOW2 and hosted-sparse VMDK guest-byte readers for explicitly supported uncompressed subsets
- guest-relative MBR/EBR/GPT and filesystem intelligence over proven guest-byte mappings
- user-facing **Analyze** action with text/JSON reporting and Save JSON
- SHA-256 + SHA-512 verification in one bounded sequential pass
- transactional RAW creation, guest-to-RAW export, split/join and bounded gzip transport pipelines
- checksum-verified clean Windows x64 package-candidate pipeline
- read-only Windows physical-disk inventory and fail-closed physical-media planning/execution contracts
- gated Windows `PhysicalDriveN` writer candidate with target-volume locking/dismount, sector-aligned writes, flush and read-back SHA-256 verification; **not user-visible and not hardware-approved**
- shared-Core read-only automation CLI with deterministic text/JSON output
- required **by Swir** + GitHub footer in the Windows UI

## 0.7 Physical Media Tools — IN PROGRESS 🚧

The safety-first engineering scope remains **6/7 (~86%)**.

Dragon DiskForge now contains a Windows physical writer **candidate** behind hard safety gates. It performs read-only source/destination preflight, rejects unprovable topology and source-on-target cases, locks/dismounts target volumes, uses sector-aligned bounded transfer, flushes the device and can perform read-back SHA-256 verification. A disposable-media harness requires explicit opt-in plus exact destination identity and confirmation binding.

PR #47 passed the full Windows x64 regression/build/package path in run #338 and the non-destructive disposable-media guard in run #10. Those results prove the code path and locked harness, **not a real destructive hardware validation**.

The final 0.7 deliverable remains open until the writer is exercised successfully on dedicated disposable media under the documented safety protocol. No destructive physical-media action is exposed in the application UI.

See [`docs/PHYSICAL-MEDIA-SAFETY.md`](docs/PHYSICAL-MEDIA-SAFETY.md).

## 0.8 Windows Integration + Power Tools — IN PROGRESS 🚧

Two of four top-level deliverables are now implemented and validated.

### Shared-Core CLI ✅

The clean Windows package includes a self-contained `cli/dragon-diskforge.exe` that reuses the same canonical Core provider registry as the desktop application.

```powershell
.\cli\dragon-diskforge.exe analyze .\sample.iso --format json
.\cli\dragon-diskforge.exe verify .\sample.iso --sha256 <64-hex-digest> --format json
.\cli\dragon-diskforge.exe formats --format json
```

The current CLI is deliberately **read-only**. It does not expose Create/Convert or physical-media mutation. See [`docs/CLI.md`](docs/CLI.md).

### PowerShell-friendly automation contract ✅

- text or one complete JSON document on stdout
- errors/diagnostics on stderr
- stable exit codes, including `4` for an expected checksum mismatch
- exact SHA-256/SHA-512 verification support
- package manifest stores the CLI entry point and SHA-256
- clean-package verification launches the unpacked self-contained CLI and validates the provider registry

Remaining 0.8 scope: Windows file associations/context-menu integration and session/settings/diagnostic import-export tooling.

## 0.6 Create + Convert + Verify — COMPLETE ✅

The automated 0.6 engineering scope is complete: dual SHA verification, safe output transactions, blank RAW + proven guest-to-RAW export, transactional split/join and bounded whole-file gzip transport compression/decompression. Sparse-container writing and unsupported format-internal compression decoding are not claimed.

## 0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅

The automated 0.5 engineering scope is complete. Proven areas include cross-provider partition intelligence, physical and guest filesystem recognition, boot/installer intelligence, identity/health aggregation, Windows Analyze/reporting, deeper exFAT/FAT32/UDF/NTFS evidence, bounded UDF root traversal, QCOW2/VMDK guest readers and guest GPT/EBR integrity hardening.

Public beta publication remains a separate release decision gated by [`docs/BETA-RELEASE.md`](docs/BETA-RELEASE.md).

## Beta readiness

The planned first public beta remains **`0.5.0-beta.1`** and is **not ready yet**. Automated engineering and clean-package candidate gates are green, but these independent manual/release gates remain:

- final beta suffix/package promotion
- clean supported Windows launch and basic open/mount/explore/verify/analyze regression
- confirm a normal user does not need Visual Studio/developer SDKs
- normal-user UAC validation
- real cross-process Explorer drag-out validation
- final public ZIP + SHA-256 + GitHub pre-release publication

No empty, symbolic or CI-only beta will be published.

## Tech

- C# / .NET 10 / WinUI 3 / Windows App SDK
- x64 + ARM64 project targets
- shared Core engine separated from WinUI
- isolated Windows native-storage layer
- self-contained x64 CLI in the clean Windows package
- generated smoke/integration fixtures rather than committed large images

## Build on Windows

Open `DragonDiskForge.sln` in Visual Studio and run `DragonDiskForge.App`, or use:

```powershell
.\scripts\build.ps1
```

CI validates Core, verification, safe output transactions, RAW pipelines, split/join + gzip, physical-media safety, provider invariants, partition/filesystem intelligence, reporting, QCOW2/VMDK guest-byte translation, all proven providers, Explorer safety, direct ISO integration, native Windows mount/inventory, the shared-Core CLI, Release x64 build and independently verified clean ZIP package candidate.

## Safety design

Inspection, analysis and CLI automation remain read-only-first. Native mounts default to read-only. Parsers validate offsets/lengths and reject contradictory or unsupported states instead of guessing.

Physical-device mutation remains outside the product surface. The Windows writer candidate is kept behind explicit identity/topology/confirmation checks and a disposable-media validation harness; it does not become a user-visible capability until real dedicated-media validation proves the remaining 0.7 gate.

## Project rule

**No fake features.** A capability becomes enabled in the UI only after its real backing path exists and is testable.

Development is tracked in [`docs/ROADMAP.md`](docs/ROADMAP.md), with execution state in [`docs/STATUS.md`](docs/STATUS.md), [`docs/MILESTONES.md`](docs/MILESTONES.md), [`docs/CLI.md`](docs/CLI.md) and [`CHANGELOG.md`](CHANGELOG.md).
