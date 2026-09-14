# Changelog

All notable changes to Dragon DiskForge will be documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- Dragon visual design system with obsidian, charcoal, ember, molten and crimson design tokens
- Custom Dragon DiskForge dragon-head/sigil in the application shell
- Vector Dragon sigil asset under `docs/branding/dragon-sigil.svg`
- Branded Forge dashboard and disk-image drop zone
- Dragon-styled capability cards, status pills and image action card
- Branded Dragon startup overlay with Forge loading state
- Subtle dragon-scale geometry in the Forge hero surface
- Responsive desktop layout for compact and narrow window widths
- Animated startup fade and selected-image reveal
- Completed Dragon light-theme surfaces and system-aware High Contrast resources
- Final Windows application `.ico` wired into the WinExe build
- `ImageVerificationService` in Core for shared SHA-256 verification
- SHA-256 progress reporting and cancellation in Core
- Verify/Cancel UI that shows live percentage while hashing large images
- Core smoke-test harness covering signatures, extension fallback, missing files, SHA-256 progress and cancellation
- GitHub Actions Windows x64 validation pipeline
- x64 and ARM64 solution platform configurations
- Testing guide under `docs/TESTING.md`
- Milestone execution and current-status documents
- GitHub execution Issues for 0.1 closure and 0.2 mount engine

### Changed
- Reworked the generic WinUI dashboard into a recognizable Dragon DiskForge interface
- SHA-256 verification now lives in `DragonDiskForge.Core` instead of the GUI layer
- Capability cards and image actions adapt when the application window becomes narrow
- Loading a different image cancels an active verification operation
- GitHub Actions cancels superseded builds for the same branch/ref
- Future Images/Mounted/Explorer/Convert/Tools navigation is disabled until its real engine milestone exists
- Settings entry is hidden until a real settings experience is implemented
- Mount and Explore remain disabled until their real engine milestones are implemented

### Fixed
- Corrected solution platform mappings so `Release|x64` restores and builds in CI
- Corrected the SHA-256 smoke-test delegate so the automated Core harness compiles cleanly
- Corrected startup-overlay stacking after WinUI rejected `Grid.ZIndex`
- Replaced a corrupted binary icon upload with a valid Win32 `.ico` resource accepted by the Release compiler

### Verified
- Core signature/fallback/error/SHA-256/progress/cancellation smoke tests pass
- Dragon startup, responsive layout, dark/light/High Contrast resources and future-feature locking pass full WinUI `Release|x64` CI
- Final Windows icon resource passes the Win32 resource compiler in the full Release build
- Milestone 0.1 exit criteria passed on current `main`

### Planned
- Native ISO/VHD/VHDX mount and unmount service
- Mounted-drive state detection
- In-app Dragon Explorer
- Open and extract files from supported images
- Recent-image history

## [0.1.0] - 2026-09-14

### Added
- Initial WinUI 3 / .NET 10 application shell
- Separate `DragonDiskForge.Core` engine project
- Drag-and-drop image loading and file picker
- Supported-format catalogue
- Signature-based detection for ISO, VHD, VHDX, QCOW2, DMG and WIM/ESD
- SHA-256 verification
- Initial roadmap and developer scripts
