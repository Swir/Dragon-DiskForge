# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- central provider registry with explicit capabilities and priority
- deterministic extension-first provider selection and fallback
- provider probe/inspection failure isolation with diagnostics
- dedicated provider-registry smoke tests
- conservative read-only IMG/RAW provider
- 512-byte alignment and known-structured-signature guards for IMG/RAW
- dedicated IMG/RAW provider smoke tests
- beta release gate documentation for planned `0.5.0-beta.1`

### Changed
- development version advanced to `0.4.0-alpha.2`
- overall project progress advanced to 32% after proven 0.4 foundation + IMG/RAW slices
- ISO direct browsing now resolves through the central provider registry
- direct-browse UI distinguishes a recognized provider from a provider that actually exposes DirectBrowse
- IMG/RAW keeps Browse/Mount/Convert disabled until real backends exist

### Fixed
- one provider probe failure no longer blocks fallback discovery
- one provider inspection failure no longer blocks fallback inspection
- RAW provider refuses known structured image signatures renamed to `.img`/`.raw`
- RAW provider refuses implausibly small or non-512-byte-aligned candidates

### Verified
- PR #14 / run #146 — provider registry, direct ISO/native mount regression, WinUI Release x64 and artifact
- PR #15 / run #151 — IMG/RAW provider, registry fallback, full regression, WinUI Release x64 and artifact

### Planned next
- IMA / floppy-image provider
- remaining 0.4 image families
- `0.5.0-beta.1` only after required 0.4 providers + agreed 0.5 beta-scope intelligence pass the release gate

## [0.3.0] - 2026-09-15

### Added
- Dragon Explorer for mounted ISO/VHD/VHDX volumes
- folder navigation, metadata, search and safe Copy out
- bounded text/image/PDF/media Preview modes
- Recent Images and Favorites with local atomic persistence
- Mounted history kept separate from live Windows state
- multi-image Explorer workspace with WinUI tabs
- safe Copy-only drag-out to Windows Explorer/Desktop
- provider-backed direct browsing contract
- managed read-only ISO9660/Joliet direct-browse provider
- direct ISO navigation, search and Copy out without mounting
- dedicated `Direct ISO` tabs marked `NO MOUNT`
- direct ISO integration tests using a disposable Windows IMAPI image
- Windows x64 artifact publishing
- README project progress bar synchronized with the roadmap

### Changed
- project version advanced to `0.3.0`
- milestone 0.3 completed
- direct ISO tabs made independent from Mount/Unmount state
- Explorer actions remain enabled only when their real backend exists

### Fixed
- WinUI namespace/path naming conflicts found by CI
- Mounted history remains independent from live Windows inventory
- direct ISO parser compilation issue found by CI
- direct ISO IMAPI test fixture compatibility issue found by CI
- deterministic UTF-8 test fixture output

### Verified
- PR #5 / run #56 — mounted Explorer slice
- PR #6 / run #72 — Preview + Image Library
- PR #7 / run #86 — Mounted history + multi-image workspace
- PR #10 / run #103 — safe drag-out + Windows x64 artifact
- PR #12 / run #128 — direct ISO browsing + native mount regression + WinUI build + artifact
- PR #12 / run #131 — full regression after README progress synchronization

### Manual QA notes
- normal-user UAC interaction remains documented in `docs/MANUAL-VALIDATION.md`
- real cross-process pointer drag remains a manual desktop QA case

## [0.2.0] - 2026-09-14

### Added
- `IMountService` contract in Core
- isolated Windows service layer
- native ISO, VHD and VHDX mount/unmount
- read-only-first mount requests
- drive-letter and attached-state detection
- Mount/Unmount progress and cancellation
- live Mounted dashboard backed by Windows state
- stale-state recovery by re-querying Windows
- friendly native-operation errors
- disposable VHD/VHDX and IMAPI ISO integration tests
- manual non-admin/UAC checklist

### Changed
- Mount controls are enabled only for proven native paths
- Mounted navigation is a real working view
- Windows owns mounted state; the app does not trust stale session cache
- project version advanced to `0.2.0`

### Fixed
- replaced unreliable direct mounted-image class enumeration with the Windows Storage volume pipeline
- corrected integration-test imports

### Verified
- ISO, VHD and VHDX mount/unmount paths pass Windows CI
- read-only state, drive detection and mounted inventory are validated
- cancellation safety and unsupported-format errors are validated

## [0.1.0] - 2026-09-14

### Added
- initial WinUI 3 / .NET 10 application shell
- separate Core project
- drag-and-drop image loading and file picker
- supported-format catalogue and signature detection
- SHA-256 verification with progress and cancellation
- Dragon visual design system and custom sigil
- responsive dashboard and startup overlay
- light/dark/High Contrast resources
- final Windows application icon
- Core smoke-test harness
- GitHub Actions Windows x64 validation
- roadmap, milestone, status and testing documentation

### Fixed
- solution platform mappings for `Release|x64`
- verification smoke-test delegate
- startup overlay stacking
- Windows icon resource packaging

### Verified
- Core smoke tests pass
- full WinUI `Release|x64` CI passes
- milestone 0.1 exit criteria passed on `main`
