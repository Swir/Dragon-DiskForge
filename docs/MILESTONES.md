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

### Completed
1. Read-only physical-disk inventory ✅
2. Physical-device evidence ✅
3. Hard refusal policy ✅
4. Write-plan preview ✅
5. Destination-bound confirmation contract ✅
6. Bounded execution + hard-gated Windows writer candidate ✅

The writer candidate revalidates source/destination identity, refuses unprovable topology, locks and dismounts target volumes, performs sector-aligned bounded writes, flushes the device and can perform read-back SHA-256. The disposable-media harness requires explicit destructive opt-in and exact destination-bound evidence. No destructive UI action is exposed.

### Remaining
7. Dedicated disposable-media validation ⬜

CI cannot substitute for the final real-media destructive test. See `docs/PHYSICAL-MEDIA-SAFETY.md`.

## 0.8 Windows Integration + Power Tools — COMPLETE ✅

Required engineering scope: **4/4 (100%)**.

- Windows file associations + context menu ✅
- shared-Core read-only image/verification CLI ✅
- PowerShell-friendly deterministic output and stable exit codes ✅
- session restore + settings import/export + sanitized diagnostic export ✅

Shell integration is per-user under `HKCU\Software\Classes`, reversible and does not replace Windows `UserChoice`. Clean packaging SHA-256-binds the self-contained CLI and shell helper. See `docs/CLI.md` and `docs/WINDOWS-SHELL-INTEGRATION.md`.

## 0.9 Quality, Security + Beta Hardening — IN PROGRESS 🚧

Current required engineering scope: **5/7 (~71%)**.

### Completed
- performance and large-image regression benchmarks ✅
- crash/diagnostic export and release support bundle ✅
- security review of parsing, packaging and privileged boundaries ✅
- package-only clean-machine runtime matrix ✅
- accessibility/keyboard/screen-reader hardening ✅

These gates cover bounded crash evidence, generated benchmark fixtures, explicit `asInvoker` execution, HKCU-only shell integration, destructive-writer CI lockout, clean-package entry-point binding, package-only fresh-runner runtime probes and repeatable XAML accessibility semantics/build checks.

### Remaining
- normal-user UAC validation ⬜
- real cross-process Explorer drag-out validation ⬜

The remaining items require genuine interactive Windows evidence; hosted CI cannot truthfully substitute for them.

See `docs/SECURITY-BOUNDARIES.md`, `docs/CLEAN-MACHINE-RUNTIME.md` and `docs/ACCESSIBILITY.md`.

## 1.0 Production Release — PLANNED

Production release requires all final capability, package, documentation, checksum, support-matrix and post-release verification gates.

## Beta release track

The first planned public beta remains **`0.5.0-beta.1`**. Source/package metadata is promoted and automated engineering, clean-package, security-boundary, accessibility-hardening, clean-machine runtime and candidate-retention gates are green. Interactive WinUI clean-desktop launch/open/mount/explore/verify/analyze, normal-user UAC, real cross-process Explorer drag-out and public Release/checksum publication remain open.

<!-- retained-beta-candidate:start -->
Current retained candidate: Beta Candidate run #79 (`35291908987`), artifact `DragonDiskForge-0.5.0-beta.1-win-x64-candidate-35291908987`, built from `main` source commit `a0483311bf000a995598b42ed9ec71019e63902e`; nested package SHA-256 `ecc4f0798ecc4d40fb9f77b433ae9dcbb206a358a9d2346bc9ac5411e8ba66a2`. It is a non-public engineering candidate, not a public release. Authoritative retained evidence: [`retained-beta-candidate.json`](retained-beta-candidate.json).
<!-- retained-beta-candidate:end -->

A capability becomes user-visible only after its real backing path and tests exist.
