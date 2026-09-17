# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines product direction. Pull requests and CI runs prove execution. A milestone remains open until its real validation gate is satisfied; passing host-side tests does not replace hardware/manual evidence.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅
Windows x64 CI, Core smoke tests, Dragon UI, verification foundation, responsive/accessibility resources and the application icon are proven.

## 0.2 Native Mount + Unmount — COMPLETE ✅
Native Windows ISO/VHD/VHDX read-only-first Mount/Unmount, state detection and disposable integration tests are proven. Normal-user UAC remains a manual beta gate.

## 0.3 Dragon Explorer — COMPLETE ✅
Released version: **0.3.0**.

- mounted-volume Explorer ✅
- Preview + Image Library ✅
- mounted history + multi-image workspace ✅
- safe Copy-only drag-out ✅
- provider-backed direct ISO browsing ✅

## 0.4 Extended Image Providers — COMPLETE ✅
Required engineering scope: **100% complete**.

Provider/foundation slices: registry foundation, IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD, FFU and provider-contract hardening.

Final checkpoints: PR #26 / run #226 FFU; PR #27 / run #228 provider-contract hardening.

## 0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅

Required automated engineering scope: **100% complete**.

Completed slices include cross-provider partition intelligence, bounded filesystem recognition, boot/installer intelligence, unified identity/health intelligence, Windows Analyze/reporting, deeper exFAT/FAT32/UDF/NTFS evidence, architecture reconciliation, bounded UDF traversal, QCOW2/VMDK guest readers, common guest partition/filesystem intelligence and guest GPT/EBR integrity hardening.

Public beta publication remains a separate release decision gated by `docs/BETA-RELEASE.md`.

## 0.6 Create + Convert + Verify — COMPLETE ✅

Required engineering scope: **100% complete**.

- dual SHA-256/SHA-512 bounded verification ✅
- safe output transaction boundary ✅
- blank RAW creation + proven guest-to-RAW materialization ✅
- transactional split/join with versioned SHA-256 manifest ✅
- bounded whole-file gzip transport compression/decompression ✅

Sparse-container writing and unsupported format-internal compression decoding are not claimed.

## 0.7 Physical Media Tools — IN PROGRESS 🚧

Current required engineering scope: **6/7 (~86%)**.

### 1. Read-only physical-disk inventory ✅
- canonical Windows `\\.\PhysicalDriveN` enumeration
- query-only inventory handles
- serial-backed stable identity where Windows exposes sufficient evidence

### 2. Physical-device evidence ✅
- capacity, bus/removable/vendor/product/revision/serial evidence
- Windows system volume mapped to backing physical-disk extents

### 3. Hard refusal policy ✅
- system disks, ambiguous identity and unknown capacity fail closed
- refused plans receive no destructive confirmation token

### 4. Write-plan preview ✅
- source/destination intent is modeled before mutation
- physical-device sources, same-device source/destination and oversized images are refused

### 5. Destination-bound confirmation contract ✅
- exact token binds physical disk number and stable identity material
- another disk's token cannot confirm a plan

### 6. Bounded execution + hard-gated Windows writer candidate ✅
- Core `PhysicalMediaWriteExecutionService` revalidates identity, confirmation and source state
- Windows preflight resolves source backing disks and logical-sector geometry read-only
- unprovable source topology and source-on-target fail closed
- target volumes must be enumerated, locked and dismounted before write access
- raw writes are sequential, bounded and sector aligned
- final device flush is explicit
- independent read-back SHA-256 is available for the source-length region
- disposable-media harness requires explicit destructive opt-in, exact destination identity/source/confirmation and an extra fixed-media gate
- no destructive UI action is exposed
- PR #47 / full Windows run #338 + Disposable Media Guard #10 passed

### 7. Dedicated disposable-media validation ⬜
- exercise the real Windows writer on dedicated disposable hardware
- prove target binding, aligned transfer, flush and claimed read-back behavior on real media
- record the exact hardware/test evidence before considering user-facing exposure

**0.7 remains open.** CI cannot substitute for the final real-media destructive test.

See `docs/PHYSICAL-MEDIA-SAFETY.md`.

## 0.8 Windows Integration + Power Tools — COMPLETE ✅

Required engineering scope: **4/4 (100%)**. Safe work completed here while 0.7 waits on hardware evidence.

### 1. Windows file associations + context menu ✅
- extension set comes from canonical Core `SupportedFormats`
- explicit per-user registration under `HKCU\Software\Classes`
- Open With application registration plus **Open with Dragon DiskForge** verb
- no `UserChoice`/default-handler replacement
- idempotent register/unregister; unrelated shell verbs are preserved
- supported command-line image path opens through the desktop's normal image pipeline and wins over saved-session restore
- clean x64 package contains self-contained `tools/dragon-diskforge-shell.exe`
- package manifest stores and re-verifies the shell helper SHA-256
- isolated Windows registry/activation smoke gate
- PR #50 / full Windows run #356 + Disposable Media Guard #28 passed

### 2. Shared-Core CLI ✅
- `DragonDiskForge.Cli` project
- read-only image/media commands `analyze`, `verify` and `formats`
- same canonical `ProviderRegistryFactory` as the desktop application
- no create/convert/physical mutation commands

### 3. PowerShell-friendly output ✅
- deterministic text/JSON stdout
- stderr-only errors/diagnostics
- stable automation exit codes
- expected SHA-256/SHA-512 matching with a dedicated mismatch exit code
- self-contained x64 `cli/dragon-diskforge.exe` in the clean package
- manifest SHA-256 for the CLI and runtime launch/provider verification after ZIP extraction
- PR #48 implementation run #343 + Disposable Media Guard #15 passed

### 4. Session/settings/diagnostic portability ✅
- versioned Core application-state schema
- atomic local state persistence and validated import/export
- best-effort desktop last-image restore that cannot block startup
- CLI state inspection/import/export and restore-setting control
- sanitized support ZIP without full image paths or image contents
- fail-closed schema/size/path validation, cancellation and rollback coverage
- PR #49 implementation run #353 + Disposable Media Guard #25 passed

Automated shell integration proves the registry, launch and clean-package contracts. Visual Explorer placement varies by Windows build (for example classic verbs may appear under **Show more options**) and remains appropriate for clean-machine/manual 0.9 QA.

See `docs/CLI.md` and `docs/WINDOWS-SHELL-INTEGRATION.md`.

## 0.9 Quality, Security + Beta Hardening — IN PROGRESS 🚧

Current required engineering scope: **5/7 (~71%)**.

### Completed
- performance and large-image regression benchmarks ✅
  - 128 MiB dual SHA-256/SHA-512 verification throughput/allocation regression gate
  - bounded recognition against a valid 8 GiB sparse RAW/IMG MBR fixture
  - benchmark JSON is published as CI evidence
- crash/diagnostic export and release support bundle ✅
  - bounded, rotated privacy-preserving crash history
  - type/HRESULT/fingerprint/method-only frame evidence
  - no raw exception messages, source-file paths or image contents
  - up to three sanitized crash summaries in the existing diagnostic ZIP
- security review of parsing, packaging and privileged boundaries ✅
  - desktop process explicitly remains `asInvoker` with `uiAccess=false`
  - dedicated Windows security-boundary CI gate
  - HKCU-only shell boundary with no `UserChoice`/HKLM takeover
  - public CLI isolation from the physical writer/destructive opt-in
  - clean package excludes debug/test payloads and binds all shipped entry points by SHA-256
  - destructive CI opt-in stays hard-locked off
  - physical-media refusal/confirmation invariants and shell command quoting/injection cases are regression-tested
  - parser/guest-reader bounded-input coverage is documented without claiming formal verification
- package-only clean-machine runtime matrix ✅
  - clean package is built once and handed to fresh `windows-2022` and `windows-latest` jobs without repository checkout
  - ZIP sidecar, package manifest, desktop/CLI/shell entry-point SHA-256 and package hygiene are independently rechecked
  - .NET/Visual Studio/Git toolchain paths are removed before packaged runtime probes
  - self-contained CLI provider/analyze/dual-hash/state/diagnostics paths are exercised
  - self-contained per-user shell helper is exercised through register/status/unregister
  - machine-readable OS/build/package/runtime evidence is published for each matrix image
- accessibility/keyboard/screen-reader hardening ✅
  - explicit UI Automation names/help text on the primary browsing/library/mounted surfaces
  - polite live status/path/count/preview metadata where dynamic announcements are useful
  - stable keyboard access keys without adding hidden destructive shortcuts
  - named list/tab collections and progress indicators for automation clients
  - fail-closed XAML accessibility regression contract plus WinUI Release compile gate

PR #51 implementation head passed full Windows run #360 and Disposable Media Guard #32 before the first two items were marked complete.

PR #52 exact implementation head `0ff048a264469406c86ab056d7a3471b82dfc4cb` passed full Windows build #367, Disposable Media Guard #39 and Security Boundary #1 before the security-review item was marked complete.

PR #53 exact implementation head `64519394512d030d90e5650e7aabd91b6f82a2f1` passed full Windows build #374, Disposable Media Guard #46, Security Boundary #8 and Clean Machine Runtime #1 before the clean-machine matrix was marked complete.

PR #54 exact implementation head `69490fe118614e9fb6bc39433756ccd0c50d5dd9` passed full Windows build #381, Accessibility Contract #1, Disposable Media Guard #53, Security Boundary #15 and Clean Machine Runtime #8 before accessibility/keyboard hardening was marked complete. This is repeatable automated semantics/compile evidence; it does not claim formal certification or a human Narrator/NVDA/JAWS session.

See `docs/SECURITY-BOUNDARIES.md`, `docs/CLEAN-MACHINE-RUNTIME.md` and `docs/ACCESSIBILITY.md`.

### Remaining
- normal-user UAC validation ⬜
- real cross-process Explorer drag-out validation ⬜

## 1.0 Production Release — PLANNED

Production release requires all final capability, package, documentation, checksum, support-matrix and post-release verification gates.

## Beta release track

The first planned public beta remains **`0.5.0-beta.1`**. The package-only clean-machine runtime matrix, automated engineering, clean-package, security-boundary and accessibility-hardening gates are green, but interactive WinUI clean-desktop launch, normal-user UAC, real cross-process drag-out, final beta suffix/package verification and public Release/checksum publication remain open.

A capability becomes user-visible only after its real backing path and tests exist.