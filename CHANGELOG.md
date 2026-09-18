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
- explicit UI Automation names/help text, polite live-region metadata and stable keyboard access keys across Direct Browse, Dragon Explorer, Explorer workspace, Images and Mounted views
- fail-closed `scripts/accessibility-contract.ps1` regression contract for labels, live regions, collection/progress names and per-view access-key uniqueness
- dedicated **Dragon DiskForge Accessibility Contract** workflow that validates the XAML contract and compiles the WinUI x64 Release app
- fail-closed `scripts/beta-manual-qa.ps1` evidence workflow for exact-package clean-desktop, normal-user UAC and cross-process drag-out observations
- dedicated **Dragon DiskForge Beta Manual QA Contract** workflow that self-tests the evidence contract under PowerShell 7 and Windows PowerShell 5.1
- external `scripts/beta-qa-session.ps1` exact-candidate preparation helper that verifies retained-package identity/runtime completeness, enforces an interactive unelevated UAC-enabled session, initializes evidence through the candidate's own hash-bound QA tool, performs a non-authoritative liveness preflight and opens an isolated Explorer drop target without auto-passing any human gate
- session-baseline binding for every passing interactive beta-QA observation, preventing evidence from mixing Windows desktop sessions while preserving exact package/tool/build/architecture/UAC bindings
- hash-bound packaged desktop witness companion with standalone verifier and guide, allowing objective desktop evidence to travel with the retained candidate without claiming a human gate
- retained-candidate evidence schema v2 binding the candidate package, beta QA kit and desktop witness companion hashes while preserving fail-closed beta readiness
- additive retained-candidate archive binding for the portable evidence-archive manifest, verifier, helper and guide, enforced by a dedicated fail-closed contract without changing beta readiness
- fail-closed beta candidate retention policy that permits retaining an exact `main` candidate through a meaningful `[beta-candidate]` push or an explicit `workflow_dispatch` request while rejecting pull requests, non-main refs and verification-only runs
- fail-closed retained-candidate currency guard that rejects stale packaged manual-QA tool hashes for final human evidence while permitting explicit inspection-only readback
- canonical `ExplorerPathSafetyValidator` shared by drag-out and mounted browse/search/preview/open/copy-out paths, with real Windows junction regression coverage
- bounded desktop image-preview admission with a 64 MiB default byte cap and metadata-only fallback for oversized images
- bounded image-signature admission for desktop rendering; spoofed or truncated image-extension files remain metadata-only
- native Windows mount/unmount commit-boundary hardening that lets an already-launched storage mutation finish and then performs bounded live-state reconciliation instead of cancelling the helper mid-commit
- `docs/ACCESSIBILITY.md` documenting the automated accessibility/keyboard contract and its human-testing limitations
- `docs/SECURITY-BOUNDARIES.md` documenting reviewed parsing, packaging, shell, diagnostics and privileged-operation boundaries plus manual-gate limitations
- `docs/CLEAN-MACHINE-RUNTIME.md` documenting the package-only runtime matrix and its explicit interactive/manual limitations
- `docs/PHYSICAL-MEDIA-SAFETY.md`, `docs/OUTPUT-TRANSACTIONS.md`, `docs/RAW-IMAGE-PIPELINES.md`, `docs/SPLIT-COMPRESSION-PIPELINES.md`, `docs/CLI.md` and `docs/WINDOWS-SHELL-INTEGRATION.md`

### Changed
- project progress remains **83% toward 1.0** after six verified 0.7 deliverables, all four verified 0.8 deliverables and five verified 0.9 hardening deliverables beyond the 68% 0.6 baseline
- **0.4 Extended Image Providers** remains **100% complete**
- **0.5 Partitions + File Systems + Image Intelligence** remains **100% automated engineering complete**
- **0.6 Create + Convert + Verify** remains **100% automated engineering complete**
- **0.7 Physical Media Tools** remains **in progress at 6/7 (~86%)** because real disposable-media validation is still required
- **0.8 Windows Integration + Power Tools** remains **100% complete (4/4)**
- **0.9 Quality, Security + Beta Hardening** remains **5/7 (~71%)**; exact candidate retention, session-bound manual-QA evidence, witness/archive-bound release provenance, retained-candidate currency hardening and mount commit-boundary hardening improve release confidence but do not complete either remaining human gate
- current development metadata is **0.5.0-beta.1**; this is candidate metadata and does not imply a public release
- Beta Candidate run #133 on `main` commit `c5e68e36c645d9a05a129bedffb8f139e026722c` is the current retained non-public Windows x64 candidate; GitHub artifact SHA-256 `8ab9fa539d3ce66e4cd463efb123d8f19ee4ecfef78a56e564dbbf06af64ae64`, nested package SHA-256 `abe37ea41adfe8e6461907454e2951ecea76fcdc4986f8c6c9867c86e356573b`; canonical evidence remains schema v2, witness-bound and additionally archive-bound to the portable evidence-archive companion
- clean Windows packaging builds and verifies both the self-contained CLI and shell-integration helper alongside the desktop application
- package manifest schema 5 records and SHA-256-binds the packaged `tools/beta-manual-qa.ps1` release-evidence tool
- diagnostic bundles include bounded sanitized crash evidence without weakening their privacy boundary
- CI publishes a dedicated large-image performance benchmark artifact, separately gates reviewed security-boundary invariants, validates the clean package on fresh Windows runner images without source checkout, separately gates beta-facing XAML accessibility, and self-tests the exact-package manual-QA evidence contract, session-preparation helper, desktop witness companion, candidate-retention policy and release contracts under PowerShell 7 and Windows PowerShell 5.1

### Safety
- inspection/provider/intelligence/image-media CLI paths remain read-only-first
- unsupported UI actions remain disabled rather than simulated
- the desktop manifest explicitly requests `asInvoker` and disables UI access; normal startup does not request ambient administrator elevation
- caller cancellation is honored up to the native mount/unmount commit boundary; once Windows begins the storage mutation, Dragon DiskForge completes the helper wait and performs bounded live-state reconciliation instead of killing a potentially committed operation
- file-producing Core operations publish through explicit transaction boundaries
- state imports validate version/size/path data before atomically replacing local application state
- diagnostic bundles intentionally exclude full saved-image paths and image contents
- persisted crash evidence intentionally excludes raw exception messages, source-file paths and image contents, is size-bounded and rotates to a fixed maximum
- shell integration is explicit, per-user and reversible; it does not write Windows `UserChoice`, HKLM, or replace default handlers
- shell unregister removes only Dragon-owned application/verb keys and preserves unrelated handlers
- shell command registration enforces the Dragon executable identity, quotes `%1`, and rejects quote injection
- mounted Explorer paths fail closed when a component below the trusted root is a reparse point/junction; browse/search/preview/open/copy-out and drag-out share the same canonical boundary, and copy-out revalidates files before opening them
- oversized image previews fall back to metadata-only rather than invoking the desktop image renderer; this is a byte-budget guard and not a decoder sandbox
- recognized image extensions must also match a bounded format signature before entering the desktop image renderer; mismatched or truncated signatures stay metadata-only
- physical-disk planning refuses system disks, ambiguous identity, unknown capacity, physical-device sources, source-on-target and oversized inputs
- the Windows writer candidate revalidates destination/source evidence, requires locked/dismounted target volumes and fails closed on unprovable topology
- the disposable-media harness requires explicit destructive opt-in and exact destination-bound evidence; default CI execution cannot write physical media
- no physical-media writer is exposed in the product UI or public CLI
- accessibility keyboard changes do not add hidden destructive shortcuts or broaden provider/media capabilities
- beta manual-QA evidence cannot pass without explicit human confirmation from an interactive unelevated Windows session, exact release-package hash binding and the same recorded desktop session
- desktop witness evidence is supporting provenance only and cannot claim the human UAC/Explorer gates
- no completion claim is made for the remaining 0.9 UAC/Explorer gates until a real desktop validation succeeds
- no completion claim is made for 0.7 until a real dedicated disposable-media validation succeeds
- performance fixtures are generated locally and removed after testing rather than committed as large binary test images
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
- PR #54 / implementation run #381 + Accessibility Contract #1 + Disposable Media Guard #53 + Security Boundary #15 + Clean Machine Runtime #8 — automated accessibility/keyboard/screen-reader semantics hardening across the primary browsing surfaces
- PR #55 / implementation run #389 + Beta Manual QA Contract #1 + Disposable Media Guard #61 + Security Boundary #23 + Clean Machine Runtime #16 + Accessibility Contract #9 — exact-package interactive beta-QA evidence contract, packaged/hash-bound tooling and fail-closed manual release evidence foundation
- PR #73 / PR-head Build #456 + fully green `main` Build #457 + Beta Candidate #64 — native mount commit-boundary cancellation hardening, `0.5.0-beta.1` source promotion and retained exact Windows x64 candidate
- PR #82 / exact PR head `357bda52a2bfa18c49ba1bb241b9c1596ddd07c2` + fully green `main` Build #488 + Beta Candidate #95 — session-bound beta-QA evidence hardening, SVG-only progress presentation cleanup and refreshed exact Windows x64 retained candidate
- PR #87 / exact PR head `96f20d422d6d1104da30ef28c40e815ca0ea7830` + green post-merge Beta Candidate #109 on `a7ef4d7dd76bc3e4f10078b0496dca9fa9986422` — witness-bound retained-candidate schema v2 and fresh witness-enabled Windows x64 candidate
- PR #93 / exact PR head `88221ccac1e3e0d702423d7baf337cc418ea0070` + fully green merge `cc74093fc776afef058423ab9ed9acba32ad0cdd` + Beta Candidate #123 — fail-closed exact-main retention policy and witness/archive-bound Windows x64 candidate
- PR #96 / exact PR head `5c4e6e49b0cdd9ffed0c8e06fccab8487eb43344` + fully green `main` Build #526 (attempt 2) + Beta Candidate #133 on `c5e68e36c645d9a05a129bedffb8f139e026722c` — fail-closed retained-candidate QA-tool currency guard and fresh current witness/archive-bound Windows x64 candidate

### Planned
- complete the remaining `0.5.0-beta.1` interactive clean-desktop/UAC/cross-process drag-out gates against the retained exact candidate, then publish the final public ZIP/checksum/pre-release only if those observations pass
- complete 0.7 only after real dedicated disposable-media writer validation
- keep expanding accessibility regression coverage when future beta-facing surfaces are added
- keep destructive physical operations out of the product UI until separately validated and deliberately exposed

## [0.3.0] - 2026-09-15
- Dragon Explorer, search/Preview/Copy out, Recent/Favorites, mounted history, multi-image workspace, safe drag-out, ISO direct browsing and Windows x64 artifacts

## [0.2.0] - 2026-09-14
- native Windows ISO/VHD/VHDX mount/unmount with read-only-first state detection/progress/cancellation and integration tests

## [0.1.0] - 2026-09-14
- WinUI 3 / .NET 10 shell, Core split, image detection, SHA-256 verification foundation, Dragon visual system/icon and x64 CI
