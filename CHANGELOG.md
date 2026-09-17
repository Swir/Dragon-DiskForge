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
- package-only clean-machine runtime matrix that hands the verified ZIP to fresh `windows-2022` and `windows-latest` jobs without a repository checkout
- clean-machine runtime probe that rechecks ZIP/manifest hashes, package hygiene, removes developer-toolchain paths, exercises packaged CLI analyze/verify/state/diagnostics and per-user shell register/status/unregister, and emits machine-readable evidence
- read-only Windows physical-disk inventory with stable-identity/system-disk evidence where Windows can prove it
- fail-closed physical-media planning, destination-bound confirmation and platform-independent execution/recovery contract
- hard-gated Windows `PhysicalDriveN` writer candidate with source/target topology preflight, target-volume lock/dismount, aligned bounded transfer, flush and read-back SHA-256
- disposable-media validation harness that remains inert without explicit destructive opt-in and destination-bound evidence
- canonical `ProviderRegistryFactory` in Core shared by desktop and automation front ends
- `DragonDiskForge.Cli` with read-only image/media commands `analyze`, `verify` and `formats`
- deterministic CLI text/JSON stdout, stderr diagnostics and stable automation exit codes
- self-contained x64 `cli/dragon-diskforge.exe` in the clean Windows package
- package-manifest CLI entry point + SHA-256 and unpacked runtime/provider verification
- versioned Core application-state schema for session/settings portability
- best-effort desktop last-image persistence/restore with missing/corrupt-state startup isolation
- atomic state import/export plus CLI `state-show`, `state-export`, `state-import` and `restore-last-image` commands
- sanitized diagnostic ZIP export plus CLI `diagnostics`, excluding full image paths and image contents
- bounded privacy-preserving crash history with exception type/HRESULT/SHA-256 fingerprint/method-only frame evidence
- sanitized `crash-summary.json` support-bundle entry containing at most three recent crash reports without raw exception messages or source-file paths
- dedicated portability/privacy smoke coverage for schema validation, rollback, cancellation, crash rotation/malformed input and diagnostic privacy
- large-image performance regression smoke project covering one-pass dual hashing and bounded sparse RAW/IMG recognition
- machine-readable benchmark JSON artifact with runtime, throughput and managed-allocation evidence plus conservative regression ceilings
- safe per-user Windows Open With/context-menu registration derived from canonical supported formats without default-app takeover
- command-line image activation that routes supported existing images through the normal desktop open pipeline ahead of saved-session restore
- self-contained `tools/dragon-diskforge-shell.exe` with register/status/unregister commands
- package-manifest shell-helper entry point + SHA-256 and unpacked launch verification
- isolated shell integration smoke coverage for registry ownership, idempotency, unrelated-handler preservation and launch-path validation
- explicit desktop `asInvoker` / `uiAccess=false` execution boundary
- `DragonDiskForge.SecurityBoundary.SmokeTests` covering elevation, shell, CLI, package, destructive-writer and physical-media fail-closed invariants
- dedicated **Dragon DiskForge Security Boundary** GitHub Actions workflow
- `docs/SECURITY-BOUNDARIES.md` documenting reviewed parsing, packaging, shell, diagnostics and privileged-operation boundaries plus manual-gate limitations
- `docs/CLEAN-MACHINE-RUNTIME.md` documenting the package-only runtime matrix and its explicit interactive/manual limitations
- `docs/PHYSICAL-MEDIA-SAFETY.md`, `docs/OUTPUT-TRANSACTIONS.md`, `docs/RAW-IMAGE-PIPELINES.md`, `docs/SPLIT-COMPRESSION-PIPELINES.md`, `docs/CLI.md` and `docs/WINDOWS-SHELL-INTEGRATION.md`

### Changed
- project progress advances to **82% toward 1.0** after six verified 0.7 deliverables, all four verified 0.8 deliverables and four verified 0.9 hardening deliverables beyond the 68% 0.6 baseline
- **0.4 Extended Image Providers** remains **100% complete**
- **0.5 Partitions + File Systems + Image Intelligence** remains **100% automated engineering complete**
- **0.6 Create + Convert + Verify** remains **100% automated engineering complete**
- **0.7 Physical Media Tools** remains **in progress at 6/7 (~86%)** because real disposable-media validation is still required
- **0.8 Windows Integration + Power Tools** remains **100% complete (4/4)**
- **0.9 Quality, Security + Beta Hardening** advances to **4/7 (~57%)** after verified crash/support, large-image performance, security-boundary and package-only clean-machine runtime hardening
- current development metadata remains **0.5.0-alpha.1** until the independent public-beta release gate decides final `0.5.0-beta.1` promotion
- clean Windows packaging builds and verifies both the self-contained CLI and shell-integration helper alongside the desktop application
- package manifest schema records the shell helper and SHA-256
- diagnostic bundles include bounded sanitized crash evidence without weakening their privacy boundary
- CI publishes a dedicated large-image performance benchmark artifact, separately gates reviewed security-boundary invariants, and now validates the clean package on fresh Windows runner images without source checkout

### Safety
- inspection/provider/intelligence/image-media CLI paths remain read-only-first
- unsupported UI actions remain disabled rather than simulated
- the desktop manifest explicitly requests `asInvoker` and disables UI access; normal startup does not request ambient administrator elevation
- file-producing Core operations publish through explicit transaction boundaries
- state imports validate version/size/path data before atomically replacing local application state
- diagnostic bundles intentionally exclude full saved-image paths and image contents
- persisted crash evidence intentionally excludes raw exception messages, source-file paths and image contents, is size-bounded and rotates to a fixed maximum
- shell integration is explicit, per-user and reversible; it does not write Windows `UserChoice`, HKLM, or replace default handlers
- shell unregister removes only Dragon-owned application/verb keys and preserves unrelated handlers
- shell command registration enforces the Dragon executable identity, quotes `%1`, and rejects quote injection
- physical-disk planning refuses system disks, ambiguous identity, unknown capacity, physical-device sources, source-on-target and oversized inputs
- the Windows writer candidate revalidates destination/source evidence, requires locked/dismounted target volumes and fails closed on unprovable topology
- the disposable-media harness requires explicit destructive opt-in and exact destination-bound evidence; default CI execution cannot write physical media
- no physical-media writer is exposed in the product UI or public CLI
- no completion claim is made for 0.7 until a real dedicated disposable-media validation succeeds
- performance fixtures are generated locally and removed after the regression run rather than committed as large binary test images
- the clean-machine runtime probe does not enable the physical writer and uses only a generated temporary image plus reversible per-user shell registration

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
- PR #49 / implementation run #353 + Disposable Media Guard #25 — session/settings portability, best-effort desktop restore, CLI state tooling and sanitized diagnostic export
- PR #50 / implementation run #356 + Disposable Media Guard #28 — safe per-user shell integration, command-line activation and verified shell-helper packaging
- PR #51 / implementation run #360 + Disposable Media Guard #32 — privacy-preserving crash/support evidence and large-image performance regression gate
- PR #52 / implementation run #367 + Disposable Media Guard #39 + Security Boundary #1 — explicit asInvoker boundary and repeatable parsing/package/privileged security review
- PR #53 / implementation run #374 + Disposable Media Guard #46 + Security Boundary #8 + Clean Machine Runtime #1 — package-only clean-machine runtime matrix on fresh Windows runner images

### Planned
- complete the independent `0.5.0-beta.1` interactive clean-desktop/UAC/cross-process drag-out/accessibility/final-package release gates
- complete 0.7 only after real dedicated disposable-media writer validation
- continue 0.9 with accessibility/keyboard/screen-reader hardening where repeatable evidence is available
- keep destructive physical operations out of the product UI until separately validated and deliberately exposed

## [0.3.0] - 2026-09-15
- Dragon Explorer, search/Preview/Copy out, Recent/Favorites, mounted history, multi-image workspace, safe drag-out, ISO direct browsing and Windows x64 artifacts

## [0.2.0] - 2026-09-14
- native Windows ISO/VHD/VHDX mount/unmount with read-only-first state detection/progress/cancellation and integration tests

## [0.1.0] - 2026-09-14
- WinUI 3 / .NET 10 shell, Core split, image detection, SHA-256 verification, Dragon visual system/icon and x64 CI