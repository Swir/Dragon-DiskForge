# Dragon DiskForge — Current Status

## Completed milestones

- **0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**
- **0.2 Native Mount + Unmount — COMPLETE ✅**
- **0.3 Dragon Explorer — COMPLETE ✅**
- **0.4 Extended Image Providers — COMPLETE ✅**
- **0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅**
- **0.6 Create + Convert + Verify — COMPLETE ✅**
- **0.8 Windows Integration + Power Tools — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

The public beta suffix is intentionally not promoted until the independent `0.5.0-beta.1` release gate passes. Engineering work beyond the 0.5 beta scope may continue without weakening that gate.

## Overall project progress

**80% toward 1.0.** The verified 68% baseline through 0.6 is followed by six verified top-level 0.7 deliverables, all four verified 0.8 deliverables and two verified 0.9 hardening deliverables. The remaining 0.7 item is hardware-gated, so safe independent work may continue in 0.9 without treating host-side CI as a substitute for real-media validation.

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

### 0.8 Windows Integration + Power Tools — COMPLETE ✅ — 4/4 (100%)

The final safe 0.8 slice is implemented and independently verified while 0.7 remains correctly blocked on real hardware evidence.

#### Shared-Core CLI ✅

- `DragonDiskForge.Cli` console project targeting .NET 10
- canonical built-in provider registration in `ProviderRegistryFactory` shared with the WinUI app
- `analyze <image> --format text|json`
- `verify <image> [--sha256 ...] [--sha512 ...] --format text|json`
- `formats --format text|json`
- no Create/Convert/physical-write command is exposed

#### PowerShell-friendly output + packaging ✅

- stdout contains only requested text or one complete JSON document
- errors and diagnostics use stderr
- stable exit codes: success, unexpected failure, usage, input/operation error, checksum mismatch and cancellation
- expected SHA-256/SHA-512 values are strictly validated and compared case-insensitively
- clean package includes self-contained `cli/dragon-diskforge.exe`
- package manifest includes CLI entry point + SHA-256
- package verification checks the CLI hash, launches the unpacked CLI and validates its provider output

PR #48 implementation head passed Disposable Media Guard #15 and full Windows build run #343.

#### Session/settings/diagnostic portability ✅

- versioned Core schema stores `RestoreLastImage`, last-image path and save timestamp
- local state is loaded/saved atomically through the safe-output transaction boundary
- desktop startup can best-effort restore the last image when enabled; missing/corrupt state cannot prevent startup
- `state-show`, `state-export`, `state-import`, `restore-last-image` and `diagnostics` are available through the CLI
- diagnostic ZIP excludes full saved-image paths and image content

PR #49 implementation head passed full Windows build run #353 and Disposable Media Guard #25.

#### Windows shell integration ✅

- canonical supported extensions are derived directly from Core `SupportedFormats`
- per-user registration uses `HKCU\Software\Classes`; no administrator requirement is introduced
- Dragon DiskForge is registered for Open With discovery plus an explicit **Open with Dragon DiskForge** context-menu verb
- Windows `UserChoice`/default-app selection is not replaced
- unregister is idempotent and removes only Dragon-owned application/verb keys
- startup image arguments are validated against supported existing image paths and take precedence over saved-session restore
- clean package contains self-contained `tools/dragon-diskforge-shell.exe`
- package manifest records/verifies the shell-helper SHA-256
- isolated registry/startup smoke tests cover registration, unrelated-verb preservation and safe activation

PR #50 implementation run #356 and Disposable Media Guard #28 passed before the milestone was marked complete. This proves the implementation/package contract, not a human visual check of every Explorer menu variant.

See `docs/CLI.md` and `docs/WINDOWS-SHELL-INTEGRATION.md`.

### 0.9 Quality, Security + Beta Hardening — IN PROGRESS 🚧 — 2/7 (~29%)

PR #51 completes two automation-safe quality slices without weakening the manual beta gate.

#### Crash/diagnostic release-support hardening ✅

- WinUI unhandled exceptions are captured best-effort without marking the exception handled
- persisted crash reports are bounded to ten entries and 64 KiB per report
- evidence is limited to schema/time, exception type, HRESULT, SHA-256 fingerprint, exception-chain type names and method-only stack frames
- raw exception messages, source-file paths and image contents are not persisted
- malformed/oversized crash evidence is ignored during collection
- the existing diagnostic ZIP includes at most three sanitized crash summaries through the same safe-output transaction boundary
- portability/privacy tests prove that a private image path and raw exception message do not leak into persisted crash JSON or the support ZIP

#### Performance + large-image regression evidence ✅

- 128 MiB generated fixture exercises the real one-pass SHA-256 + SHA-512 verification service
- CI records throughput and managed-allocation evidence with conservative regression ceilings
- 8 GiB sparse RAW/IMG fixture contains a valid MBR and bounded partition so recognition resolves through the real `raw-partitions` provider
- recognition runtime and managed allocations are bounded
- benchmark evidence is emitted as JSON and uploaded as a dedicated CI artifact

The first benchmark attempt exposed an invalid blank RAW fixture and correctly failed run #359. The fixture was corrected in the same development iteration to contain a valid MBR partition. Exact implementation head `44a075a471f38a8350587400e72f4a4e781954ac` then passed full Windows build/package run #360 and Disposable Media Guard #32, including the new crash/privacy tests and performance gate.

#### Remaining 0.9 scope ⬜

- clean-machine runtime matrix
- normal-user UAC validation
- real cross-process Explorer drag-out validation
- accessibility/keyboard/screen-reader hardening
- security review of parsing, packaging and privileged boundaries

The first three remain explicit manual/real-environment gates. Accessibility and security-review work can continue in automation where evidence is real and repeatable.

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
- PR #32 / run #260 — Windows Analyze/reporting
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
- PR #49 / implementation run #353 + Disposable Media Guard #25 — session/settings portability, desktop restore and sanitized diagnostic tooling
- PR #50 / implementation run #356 + Disposable Media Guard #28 — safe per-user shell integration, launch activation and verified shell-helper packaging

### 0.9
- PR #51 / implementation run #360 + Disposable Media Guard #32 — sanitized crash/support evidence and large-image performance regression gate

## Beta readiness

The planned first public beta remains **`0.5.0-beta.1`** and is **NOT READY YET**. Automated engineering and clean-package candidate gates are green, but these independent release gates remain:

- promote version/package metadata to the final beta suffix only at release time
- launch and exercise the package on a clean supported Windows machine
- confirm no developer SDK/Visual Studio requirement for a normal user
- complete normal-user UAC validation
- complete real cross-process Explorer drag-out validation
- complete basic clean-machine launch/open/mount/explore/verify/analyze regression
- publish the final ZIP, SHA-256 and GitHub pre-release only after those checks pass

The new crash/support and benchmark evidence strengthen the beta-hardening path but do not replace any manual release gate.

## Current safety state

Inspection, reporting and image/media automation remain read-only-first. CLI state/settings commands mutate only Dragon DiskForge local application state. Shell integration is explicit, per-user, reversible and does not replace Windows default-app choices. Unsupported capabilities stay disabled.

Crash evidence is bounded, rotated and privacy-preserving; support export deliberately excludes raw exception messages, source-file paths and image contents. Performance fixtures are generated locally and removed after testing.

The Windows physical writer candidate is not connected to a user-visible action. Its executable path remains behind destination identity/topology/confirmation checks and a hard-locked disposable-media harness. No completion claim is made until real dedicated-media validation proves the final 0.7 item.

Interactive UAC, clean-machine runtime and the real cross-process Explorer drag gesture remain manual beta QA gates.
