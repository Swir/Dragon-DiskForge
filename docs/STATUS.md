# Dragon DiskForge — Current Status

## Completed milestones

- **0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**
- **0.2 Native Mount + Unmount — COMPLETE ✅**
- **0.3 Dragon Explorer — COMPLETE ✅**
- **0.4 Extended Image Providers — COMPLETE ✅**
- **0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅**
- **0.6 Create + Convert + Verify — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

The public beta suffix is intentionally not promoted until the independent `0.5.0-beta.1` release gate passes. Engineering work beyond the 0.5 beta scope may continue without weakening that gate.

## Overall project progress

**76% toward 1.0.** The previously verified 68% baseline through 0.6 is followed by six verified top-level 0.7 deliverables and two verified top-level 0.8 deliverables. The remaining 0.7 item is hardware-gated, so safe independent 0.8 work is progressing out of order rather than treating hardware CI as a substitute for real media validation.

## Current roadmap position

### 0.7 Physical Media Tools — IN PROGRESS 🚧 — 6/7 (~86%)

PR #45 established query-only physical inventory and fail-closed planning. PR #46 established the platform-independent execution/recovery contract. PR #47 adds a hard-gated Windows writer candidate without exposing a destructive UI action.

#### Proven Windows writer-candidate boundary

- read-only preflight resolves current target identity, capacity, system-disk state and logical sector size
- local source files are mapped to backing physical-disk extents; same-device sources are refused
- UNC/unprovable source topology fails closed
- source length and sector alignment are revalidated
- target volumes are enumerated, locked and dismounted before write access
- device writes are sequential and sector aligned
- final flush is explicit
- read-back SHA-256 can independently verify the written source-length region
- the disposable-media harness requires explicit destructive opt-in, exact `PhysicalDriveN`, stable identity, source image and the destination-bound confirmation token
- fixed media requires an additional explicit confirmation gate

PR #47 implementation run #338 passed the complete Windows x64 regression/build/package path. Disposable Media Guard run #10 passed the non-destructive preflight/harness gate. These results prove the candidate and its locked test harness, **not real-media destructive validation**.

#### Remaining 0.7 scope ⬜

- exercise the writer successfully on dedicated disposable media under the documented safety protocol

Until that happens, the application exposes no physical-media write action and 0.7 remains 6/7.

See `docs/PHYSICAL-MEDIA-SAFETY.md`.

### 0.8 Windows Integration + Power Tools — IN PROGRESS 🚧 — 2/4 (50%)

The remaining 0.7 item requires physical disposable-media evidence, so two independent read-only 0.8 slices were implemented without weakening the 0.7 gate.

#### Shared-Core CLI ✅

- new `DragonDiskForge.Cli` console project targeting .NET 10
- canonical built-in provider registration moved to `ProviderRegistryFactory` in Core and shared with the WinUI app
- `analyze <image> --format text|json`
- `verify <image> [--sha256 ...] [--sha512 ...] --format text|json`
- `formats --format text|json`
- no Create/Convert/physical-write command is exposed

#### PowerShell-friendly output + packaging ✅

- stdout contains only requested text or one complete JSON document
- errors and diagnostics use stderr
- stable exit codes: success, unexpected failure, usage, input/operation error, checksum mismatch and cancellation
- expected SHA-256/SHA-512 values are strictly validated and compared case-insensitively
- CLI smoke tests cover JSON cleanliness, unknown-image truthfulness, dual-hash matches/mismatch, provider uniqueness and error contracts
- clean package includes self-contained `cli/dragon-diskforge.exe`
- package manifest includes CLI entry point + SHA-256
- package verification checks the CLI hash, launches the unpacked CLI and validates its provider output

PR #48 implementation head `8ed9bce0...` passed Disposable Media Guard #15 and full Windows build run #343, including the new CLI smoke tests, Release x64 build, self-contained CLI packaging and clean-package runtime verification.

Remaining 0.8 work:

- Windows file associations/context-menu integration
- session restore + settings import/export + diagnostic export tooling

See `docs/CLI.md`.

## Proven validation checkpoints

### 0.4
- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy
- PR #18 / run #172 — BIN/CUE
- PR #19 / run #182 — MDF/MDS
- PR #20 / run #185 — NRG
- PR #21 / run #198 — CCD/IMG/SUB
- PR #22 / run #205 — VMDK metadata
- PR #23 / run #212 — QCOW/QCOW2 metadata
- PR #24 / run #219 — DMG/UDIF
- PR #25 / run #222 — WIM/ESD
- PR #26 / run #226 — FFU
- PR #27 / run #228 — provider-contract hardening

### 0.5
- PR #28 / run #232 — partition intelligence
- PR #29 / run #235 — filesystem recognition
- PR #30 / run #242 — boot/installer intelligence
- PR #31 / run #245 — unified identity/health
- PR #32 / run #260 — Analyze/reporting
- PR #33 / run #262 — deeper filesystem evidence
- PR #34 / run #266 — NTFS/architecture hardening + package foundation
- PR #35 / run #270 — version metadata + independent package verification
- PR #36 / run #274 — bounded UDF root traversal
- PR #37 / run #277 — QCOW2 guest reader
- PR #38 / run #284 — hosted-sparse VMDK guest reader
- PR #39 / run #287 — guest partition/filesystem intelligence
- PR #40 / implementation run #289 — guest GPT/EBR integrity hardening

### 0.6
- PR #41 / implementation run #292 — dual SHA-256/SHA-512 verification
- PR #42 / implementation run #296 and final run #298 — safe output transaction boundary
- PR #43 / implementation run #300 — RAW creation + guest-to-RAW conversion
- PR #44 / implementation run #304 — transactional split/join + bounded gzip transport pipelines

### 0.7
- PR #45 / implementation run #310 — physical inventory/planning/confirmation foundation
- PR #46 / implementation run #319 — bounded execution/recovery contract
- PR #47 / implementation run #338 + Disposable Media Guard #10 — hard-gated Windows writer candidate and preflight/harness coverage

### 0.8
- PR #48 / implementation run #343 + Disposable Media Guard #15 — shared-Core CLI, deterministic automation output and verified self-contained clean-package CLI

## Beta readiness

The planned first public beta remains **`0.5.0-beta.1`** and is **NOT READY YET**. Automated engineering and clean-package candidate gates are green, but these independent release gates remain:

- promote version/package metadata to the final beta suffix only at release time
- launch and exercise the package on a clean supported Windows machine
- confirm no developer SDK/Visual Studio requirement for a normal user
- complete normal-user UAC validation
- complete real cross-process Explorer drag-out validation
- complete basic clean-machine launch/open/mount/explore/verify/analyze regression
- publish the final ZIP, SHA-256 and GitHub pre-release only after those checks pass

The self-contained CLI strengthens the package but does not replace any of those manual beta gates.

## Current safety state

Inspection, reporting and CLI automation remain read-only-first. Unsupported capabilities stay disabled. Metadata parsers, guest readers and intelligence services validate offsets/ranges and reject unsupported or contradictory states instead of guessing.

The Windows physical writer candidate is not connected to a user-visible action. Its executable path remains behind destination identity/topology/confirmation checks and a hard-locked disposable-media harness. No completion claim is made until real dedicated-media validation proves the final 0.7 item.

Interactive UAC, clean-machine runtime and the real cross-process Explorer drag gesture remain manual beta QA gates.
