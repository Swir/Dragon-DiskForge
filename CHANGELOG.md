# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- hardened provider registry with deterministic resolution, failure isolation and truthful capabilities
- read-only providers for IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU
- provider-agnostic partition intelligence, bounded filesystem recognition, boot/install intelligence and identity/health aggregation
- Windows **Analyze** action with text/JSON reports and Save JSON
- deeper exFAT/FAT32/UDF/NTFS metadata evidence, bounded UDF traversal and guest GPT/EBR hardening
- bounded QCOW2 standard-uncompressed and hosted-sparse VMDK guest-byte readers
- combined SHA-256 + SHA-512 verification with exact hashed-byte reporting
- `SafeOutputService`, blank RAW creation, proven guest-to-RAW export, transactional split/join and bounded gzip transport pipelines
- checksum-verified clean Windows x64 package candidate with deterministic manifest and SHA-256 sidecar
- read-only Windows physical-disk inventory with stable-identity/system-disk evidence where Windows can prove it
- fail-closed physical-media planning, destination-bound confirmation and platform-independent execution/recovery contract
- hard-gated Windows `PhysicalDriveN` writer candidate with source/target topology preflight, target-volume lock/dismount, aligned bounded transfer, flush and read-back SHA-256
- disposable-media validation harness that remains inert without explicit destructive opt-in and destination-bound evidence
- canonical `ProviderRegistryFactory` in Core shared by desktop and automation front ends
- read-only `DragonDiskForge.Cli` with `analyze`, `verify` and `formats`
- deterministic CLI text/JSON stdout, stderr diagnostics and stable automation exit codes
- self-contained x64 `cli/dragon-diskforge.exe` in the clean Windows package
- package-manifest CLI entry point + SHA-256 and unpacked runtime/provider verification
- dedicated CLI smoke tests for JSON cleanliness, truthful unknown-format analysis, dual-digest matching/mismatch, provider uniqueness and error contracts
- `docs/PHYSICAL-MEDIA-SAFETY.md`, `docs/OUTPUT-TRANSACTIONS.md`, `docs/RAW-IMAGE-PIPELINES.md`, `docs/SPLIT-COMPRESSION-PIPELINES.md` and `docs/CLI.md`

### Changed
- project progress advances to **76% toward 1.0** after six verified 0.7 deliverables plus two independently verified 0.8 deliverables beyond the 68% 0.6 baseline
- **0.4 Extended Image Providers** remains **100% complete**
- **0.5 Partitions + File Systems + Image Intelligence** remains **100% automated engineering complete**
- **0.6 Create + Convert + Verify** remains **100% automated engineering complete**
- **0.7 Physical Media Tools** remains **in progress at 6/7 (~86%)** because real disposable-media validation is still required
- **0.8 Windows Integration + Power Tools** begins **in progress at 2/4 (50%)** with shared-Core CLI + PowerShell-friendly output
- current development metadata remains **0.5.0-alpha.1** until the independent public-beta release gate decides final `0.5.0-beta.1` promotion
- desktop direct-browse registration now consumes the same canonical Core provider factory as the CLI
- clean Windows packaging now builds and verifies a self-contained CLI alongside the desktop application

### Safety
- inspection/provider/intelligence/CLI paths remain read-only-first
- unsupported UI actions remain disabled rather than simulated
- file-producing Core operations publish through explicit transaction boundaries
- physical-disk planning refuses system disks, ambiguous identity, unknown capacity, physical-device sources, source-on-target and oversized inputs
- the Windows writer candidate revalidates destination/source evidence, requires locked/dismounted target volumes and fails closed on unprovable topology
- the disposable-media harness requires explicit destructive opt-in and exact destination-bound evidence; default CI execution cannot write physical media
- no physical-media writer is exposed in the product UI
- no completion claim is made for 0.7 until a real dedicated disposable-media validation succeeds
- the CLI exposes no create, convert or physical-media mutation command

### Verified
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
- PR #41 / implementation run #292 — dual SHA-256/SHA-512 verification
- PR #42 / implementation run #296 and final run #298 — safe output transaction foundation
- PR #43 / implementation run #300 — RAW creation + guest-to-RAW conversion
- PR #44 / implementation run #304 — transactional split/join + bounded gzip transport pipelines
- PR #45 / implementation run #310 — read-only physical inventory, planning and confirmation foundation
- PR #46 / implementation run #319 — bounded physical-write execution/recovery contract
- PR #47 / full Windows run #338 + Disposable Media Guard #10 — hard-gated Windows writer candidate and non-destructive validation harness
- PR #48 / implementation run #343 + Disposable Media Guard #15 — shared-Core CLI, deterministic automation contract, self-contained CLI packaging and clean-package runtime verification

### Planned
- complete the independent `0.5.0-beta.1` clean-machine/UAC/cross-process drag-out/final-package release gates
- complete 0.7 only after real dedicated disposable-media writer validation
- continue 0.8 with Windows file associations/context menu and session/settings/diagnostic portability
- keep destructive physical operations out of the product UI until separately validated and deliberately exposed

## [0.3.0] - 2026-09-15
- Dragon Explorer, search/Preview/Copy out, Recent/Favorites, mounted history, multi-image workspace, safe drag-out, ISO direct browsing and Windows x64 artifacts

## [0.2.0] - 2026-09-14
- native Windows ISO/VHD/VHDX mount/unmount with read-only-first state detection/progress/cancellation and integration tests

## [0.1.0] - 2026-09-14
- WinUI 3 / .NET 10 shell, Core split, image detection, SHA-256 verification, Dragon visual system/icon and x64 CI
