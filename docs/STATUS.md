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

**0.5.0-beta.1**

Source and clean-package metadata are promoted to the `0.5.0-beta.1` candidate suffix. Public release remains gated by clean-desktop interactive regression, normal-user UAC, real cross-process Explorer drag-out and final GitHub pre-release publication.

<!-- retained-beta-candidate:start -->
Current retained candidate: Beta Candidate run #208 (`35607989060`), artifact `DragonDiskForge-0.5.0-beta.1-win-x64-candidate-35607989060`, built from `main` source commit `9e0ea20b677e9f56bf5dc6f58bc55a4e51ca9998`; nested package SHA-256 `3ae71a7468b487500f6a3e4e80ce65fe752c6fbefc3729f5723b2caf5bbbf342`. Evidence is witness-bound (schema v2), archive-bound, live-session-bound, Explorer-witness-bound and package-UAC-witness-bound to the packaged desktop witness, portable evidence-archive, package-specific live continuity, package-specific real-Explorer witness, packaged normal-user UAC witness and packaged UAC before/after pair-verifier companions; no human gate is claimed. It is a non-public engineering candidate, not a public release. Authoritative retained evidence: [`retained-beta-candidate.json`](retained-beta-candidate.json).
<!-- retained-beta-candidate:end -->

The retained artifact is package-manifest schema 6, x64, `.NET=self-contained`, `WindowsAppSDK=self-contained` and `VisualCpp=app-local`. Its source is the latest deliberately retained `main` checkpoint selected through the fail-closed retention policy; schema-v2 evidence binds the packaged desktop witness, portable evidence archive, package-specific live-session continuity, package-specific real-Explorer witness, packaged normal-user UAC witness and packaged UAC before/after pair verifier. The synchronization commits that record this evidence do not alter packaged product code.

## Overall project progress

**83% toward 1.0.** The verified 68% baseline through 0.6 is followed by six verified top-level 0.7 deliverables, all four verified 0.8 deliverables and five verified 0.9 hardening deliverables. The remaining 0.7 item is hardware-gated, so safe independent work may continue in 0.9 without treating host-side CI as a substitute for real-media validation.

## Current roadmap position

### 0.7 Physical Media Tools — IN PROGRESS 🚧 — 6/7 (~86%)

Proven scope:

- read-only Windows physical-disk inventory
- capacity/bus/removable/vendor/product/revision/serial/system-disk evidence
- hard refusal for system disks, ambiguous identity and unknown capacity
- source/destination write-plan preview with same-device and oversize refusal
- destination-bound destructive confirmation contract
- bounded execution/recovery contract with a hard-gated Windows writer candidate
- source/target topology preflight, target-volume lock/dismount, sector-aligned bounded writes, flush and optional read-back SHA-256

Remaining 0.7 scope:

- exercise the writer successfully on dedicated disposable media under the documented safety protocol

No destructive physical-media action is user-visible. CI proves the locked engineering path, not real-media destructive validation. See `docs/PHYSICAL-MEDIA-SAFETY.md`.

### 0.8 Windows Integration + Power Tools — COMPLETE ✅ — 4/4 (100%)

Verified scope:

- canonical supported-format shell registration under `HKCU\Software\Classes`
- reversible Open With/context-menu integration without `UserChoice` takeover
- shared-Core read-only CLI (`analyze`, `verify`, `formats`) with deterministic text/JSON output and stable exit codes
- self-contained packaged CLI and shell helper with SHA-256 manifest binding
- versioned session/settings persistence and validated import/export
- best-effort desktop last-image restore
- sanitized diagnostic export that excludes full image paths and image contents

See `docs/CLI.md` and `docs/WINDOWS-SHELL-INTEGRATION.md`.

### 0.9 Quality, Security + Beta Hardening — IN PROGRESS 🚧 — 5/7 (~71%)

Verified deliverables:

- crash/diagnostic release-support hardening ✅
- performance + large-image regression evidence ✅
- parsing/package/privileged-boundary security review ✅
- package-only clean-machine runtime matrix ✅
- accessibility/keyboard/screen-reader hardening ✅

Remaining deliverables:

- normal-user UAC validation ⬜
- real cross-process Explorer drag-out validation ⬜

The clean-machine runtime path builds the clean package once and exercises package identity, manifest hashes, self-contained CLI/state/diagnostic paths and reversible per-user shell integration on fresh Windows runner images without repository checkout. The accessibility gate verifies XAML semantics and a WinUI Release build. These automated gates do not substitute for interactive desktop UAC/Explorer evidence.

See `docs/SECURITY-BOUNDARIES.md`, `docs/CLEAN-MACHINE-RUNTIME.md` and `docs/ACCESSIBILITY.md`.

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
- PR #42 / implementation run #296 and final run #298 — safe output transaction foundation
- PR #43 / implementation run #300 — RAW creation + guest-to-RAW conversion
- PR #44 / implementation run #304 — transactional split/join + bounded gzip transport pipelines

### 0.7
- PR #45 / implementation run #310 — physical inventory/planning/confirmation foundation
- PR #46 / implementation run #319 — bounded execution/recovery contract
- PR #47 / implementation run #338 + Disposable Media Guard #10 — hard-gated Windows writer candidate and preflight/harness coverage

### 0.8
- PR #48 / implementation run #343 + Disposable Media Guard #15 — shared-Core CLI and verified self-contained clean-package CLI
- PR #49 / implementation run #353 + Disposable Media Guard #25 — session/settings portability, restore and sanitized diagnostics
- PR #50 / implementation run #356 + Disposable Media Guard #28 — safe per-user shell integration and verified shell-helper packaging

### 0.9 and beta hardening
- PR #51 / implementation run #360 + Disposable Media Guard #32 — sanitized crash/support evidence and large-image performance gate
- PR #52 / implementation run #367 + Disposable Media Guard #39 + Security Boundary #1 — repeatable security review and explicit `asInvoker` boundary
- PR #53 / implementation run #374 + Disposable Media Guard #46 + Security Boundary #8 + Clean Machine Runtime #1 — package-only clean-machine runtime matrix
- PR #54 / implementation run #381 + Accessibility Contract #1 + Disposable Media Guard #53 + Security Boundary #15 + Clean Machine Runtime #8 — automated accessibility/keyboard semantics hardening
- PR #55 / implementation run #389 + Beta Manual QA Contract #1 — exact-package interactive beta-QA evidence foundation
- PR #73 / PR-head Build #456 + main Build #457 + Beta Candidate #64 — native mount commit-boundary cancellation hardening and beta source promotion
- PR #78 — retained-candidate evidence hardened and bound to its then-current retained run
- PR #82 / exact PR head `357bda52a2bfa18c49ba1bb241b9c1596ddd07c2` + `main` Build #488 + Beta Candidate #95 — session-bound beta-QA evidence hardening, SVG progress presentation cleanup and fresh retained candidate selection
- PR #87 / exact PR head `96f20d422d6d1104da30ef28c40e815ca0ea7830` + Beta Candidate #109 on merge `a7ef4d7dd76bc3e4f10078b0496dca9fa9986422` — retained-candidate schema-v2 witness binding and fresh witness-enabled candidate
- PR #93 / exact PR head `88221ccac1e3e0d702423d7baf337cc418ea0070` + fully green merge `cc74093fc776afef058423ab9ed9acba32ad0cdd` + Beta Candidate #123 — explicit fail-closed retention policy and fresh archive/witness-bound Windows x64 candidate
- PR #96 / exact PR head `5c4e6e49b0cdd9ffed0c8e06fccab8487eb43344` + fully green `main` Build #526 (attempt 2) + Beta Candidate #133 on merge `c5e68e36c645d9a05a129bedffb8f139e026722c` — fail-closed retained-candidate QA-tool currency guard and fresh witness/archive-bound Windows x64 candidate
- PR #100 / exact PR head `f1ee320ffae55108d614f52fea5cbe1cfb49c151` + fully green merge `ab21392f27a0909e0a62886a770607a7976b3334` + Beta Candidate #142 — hash-bound live-session continuity tooling and fresh witness/archive/live-session-bound Windows x64 candidate
- PR #102 / exact PR head `2a09c893d2363881ea3f955402ad061445c72c4b` + fully green merge `75ba8788c9bf3eac44778e7e2efadc6f8119ad31` + Beta Candidate #146 — process-start binding for the live beta-QA session, rejecting PID reuse/rebinding after preparation, with a fresh witness/archive/live-session-bound Windows x64 candidate
- PR #106 + PR #107 / exact PR #107 head `8719ee64cac75bebbad0b628dcb4edfd6d990832` + fully green `main` `b50a28d98bccb14511bf33812df1b80f68756d69` + Beta Candidate #158 — package-bound real-Explorer witness companion, fail-closed retained Explorer-currency guard and fresh Explorer-bound Windows x64 candidate

## Beta readiness

The planned first public beta remains **`0.5.0-beta.1`** and is **NOT READY YET**. Automated engineering, security-boundary, clean-package, package-only clean-machine runtime, accessibility-hardening and retained exact-candidate evidence gates are green. The authoritative candidate identity is the retained-evidence block above.

Remaining independent release gates:

- human-confirmed WinUI launch and basic open/mount/explore/verify/analyze regression on a clean supported Windows desktop
- complete normal-user UAC validation
- complete real cross-process Explorer drag-out validation
- publish the final ZIP, SHA-256 and GitHub pre-release only after those checks pass

## Current safety state

Inspection, reporting and image/media automation remain read-only-first. CLI state/settings commands mutate only Dragon DiskForge local application state. Shell integration is explicit, per-user and reversible and does not replace Windows default-app choices. Unsupported capabilities stay disabled. The desktop manifest is explicitly `asInvoker`, so ordinary startup does not request ambient elevation.

Crash evidence is bounded, rotated and privacy-preserving; support export deliberately excludes raw exception messages, source-file paths and image contents. Performance fixtures are generated locally and removed after testing.

The Windows physical writer candidate is not connected to a user-visible action. Its executable path remains behind destination identity/topology/confirmation checks and a hard-locked disposable-media harness. No completion claim is made until real dedicated-media validation proves the final 0.7 item.
