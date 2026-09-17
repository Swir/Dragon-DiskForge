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

**83% toward 1.0.** The verified 68% baseline through 0.6 is followed by six verified top-level 0.7 deliverables, all four verified 0.8 deliverables and five verified 0.9 hardening deliverables. The remaining 0.7 item is hardware-gated, so safe independent work may continue in 0.9 without treating host-side CI as a substitute for real-media validation.

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

### 0.9 Quality, Security + Beta Hardening — IN PROGRESS 🚧 — 5/7 (~71%)

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

PR #51 implementation head `44a075a471f38a8350587400e72f4a4e781954ac` passed full Windows build/package run #360 and Disposable Media Guard #32, including the crash/privacy tests and performance gate.

#### Parsing/package/privileged-boundary security review ✅

- the desktop application manifest explicitly requests `asInvoker` with `uiAccess=false`; normal desktop startup does not request ambient administrator elevation
- a dedicated Security Boundary workflow regression-tests the reviewed invariants on Windows
- per-user shell integration remains rooted in `HKCU\Software\Classes`, does not write HKLM and does not modify Windows `UserChoice`
- the public CLI remains isolated from the physical-media execution service, Windows raw-write sink and destructive test opt-in contract
- clean packaging continues excluding debug/test payloads and binds the app, CLI and shell-helper entry points by SHA-256
- default CI execution remains incapable of enabling destructive disposable-media writes
- physical-media safety tests prove fail-closed behavior for system disks, unstable identity, unknown/insufficient capacity and physical-device sources, plus exact destination-bound confirmation
- shell command construction retains strict executable identity and quoting/injection rejection
- parser/guest-reader security review relies on the existing bounded/truncated/OOB/cancellation regression suites and is documented without overstating formal sandbox guarantees

PR #52 exact implementation head `0ff048a264469406c86ab056d7a3471b82dfc4cb` passed full Windows build #367, Disposable Media Guard #39 and Security Boundary #1 before this deliverable was marked complete. See `docs/SECURITY-BOUNDARIES.md`.

#### Package-only clean-machine runtime matrix ✅

- a clean Windows x64 ZIP is built and package-verified once, then handed to fresh `windows-2022` and `windows-latest` runtime jobs
- runtime jobs intentionally have no repository checkout and assert that `.git`/`src` are absent
- the probe rechecks the ZIP sidecar, manifest schema/architecture, app/CLI/shell SHA-256 bindings and absence of PDB/test payloads
- .NET/Visual Studio/Git toolchain paths are removed before packaged entry points are launched, reducing the chance of hidden developer-SDK dependence
- the self-contained CLI is exercised for provider enumeration, `analyze`, SHA-256/SHA-512 `verify`, isolated state/settings and sanitized diagnostics
- the self-contained shell helper is exercised through per-user register → status → unregister
- every runtime image emits machine-readable OS/build/package/runtime evidence

PR #53 exact implementation head `64519394512d030d90e5650e7aabd91b6f82a2f1` passed full Windows build #374, Disposable Media Guard #46, Security Boundary #8 and Clean Machine Runtime #1 on both matrix images before this deliverable was marked complete. See `docs/CLEAN-MACHINE-RUNTIME.md`.

#### Accessibility/keyboard/screen-reader hardening ✅

- Direct Browse, Dragon Explorer, multi-image workspace, Images and Mounted surfaces expose explicit UI Automation names/help text for important interactive and dynamic elements
- status, item-count, path and preview changes use polite live-region metadata where appropriate
- stable common actions expose per-view keyboard access keys without adding hidden destructive shortcuts
- list/tab collections and progress indicators are explicitly named for automation clients
- `scripts/accessibility-contract.ps1` fails closed on missing labels/live regions/named collections/progress indicators and duplicate per-view access keys
- the dedicated Windows Accessibility Contract workflow runs the XAML contract and then compiles the WinUI x64 Release application

PR #54 exact implementation head `69490fe118614e9fb6bc39433756ccd0c50d5dd9` passed Accessibility Contract #1, full Windows build #381, Disposable Media Guard #53, Security Boundary #15 and Clean Machine Runtime #8 before this deliverable was marked complete. This is repeatable automated accessibility/keyboard evidence, not a claim of formal accessibility certification or a human Narrator/NVDA/JAWS validation. See `docs/ACCESSIBILITY.md`.

#### Remaining 0.9 scope ⬜

- normal-user UAC validation
- real cross-process Explorer drag-out validation

The clean-machine package-only and accessibility gates are repeatable automation. Interactive WinUI launch, UAC, real cross-process Explorer behavior and dedicated-media validation retain their independent evidence requirements.

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
- PR #36 / run #274 — bounded UDF traversal
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
- PR #52 / implementation run #367 + Disposable Media Guard #39 + Security Boundary #1 — repeatable parsing/package/privileged security review and explicit asInvoker boundary
- PR #53 / implementation run #374 + Disposable Media Guard #46 + Security Boundary #8 + Clean Machine Runtime #1 — package-only clean-machine runtime matrix across fresh Windows runner images
- PR #54 / implementation run #381 + Accessibility Contract #1 + Disposable Media Guard #53 + Security Boundary #15 + Clean Machine Runtime #8 — automated accessibility/keyboard/screen-reader semantics hardening

## Beta readiness

The planned first public beta remains **`0.5.0-beta.1`** and is **NOT READY YET**. Automated engineering, security-boundary, clean-package, package-only clean-machine runtime and accessibility-hardening gates are green, but these independent release gates remain:

- promote version/package metadata to the final beta suffix only at release time
- human-confirmed WinUI launch and basic open/mount/explore/verify/analyze regression on a clean supported Windows desktop
- complete normal-user UAC validation
- complete real cross-process Explorer drag-out validation
- publish the final ZIP, SHA-256 and GitHub pre-release only after those checks pass

The automated accessibility contract proves checked XAML semantics plus a successful WinUI build; it deliberately does not claim a human assistive-technology certification session. The package-only runtime matrix proves that the clean packaged CLI/shell/state/diagnostic paths run without a repository checkout and without relying on developer toolchain paths. Neither gate substitutes for remaining interactive desktop/UAC/Explorer validation.

## Current safety state

Inspection, reporting and image/media automation remain read-only-first. CLI state/settings commands mutate only Dragon DiskForge local application state. Shell integration is explicit, per-user, reversible and does not replace Windows default-app choices. Unsupported capabilities stay disabled. The desktop manifest is explicitly `asInvoker`, so ordinary startup does not request ambient elevation.

Crash evidence is bounded, rotated and privacy-preserving; support export deliberately excludes raw exception messages, source-file paths and image contents. Performance fixtures are generated locally and removed after testing.

The Windows physical writer candidate is not connected to a user-visible action. Its executable path remains behind destination identity/topology/confirmation checks and a hard-locked disposable-media harness. No completion claim is made until real dedicated-media validation proves the final 0.7 item.

Interactive WinUI clean-desktop launch, normal-user UAC and real cross-process Explorer drag-out remain beta QA gates.