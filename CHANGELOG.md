# Changelog

All notable changes to Dragon DiskForge are documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Planned
- 0.4 Extended Image Providers
- broader provider capability reporting, fallback and isolation

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
- milestone 0.3 is complete
- 0.4 Extended Image Providers is the next milestone
- direct ISO tabs are independent from Mount/Unmount state
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
- PR #10 / run #111 — final drag-out docs/version regression
- PR #12 / run #128 — direct ISO browsing + native mount regression + WinUI build + artifact
- PR #12 / run #131 — full regression remained green after README progress synchronization

### Manual QA notes
- normal-user UAC interaction remains documented in `docs/MANUAL-VALIDATION.md`
- the real cross-process pointer drag gesture remains a manual desktop QA case

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
