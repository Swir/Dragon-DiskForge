# Changelog

All notable changes to Dragon DiskForge will be documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- `ExplorerEntry` model and `IExplorerService` contract in Core
- filesystem-backed mounted-volume Explorer service for ISO/VHD/VHDX drive roots
- safe folders-first directory listing with file size and modified-time metadata
- folder navigation, Up and address/breadcrumb display in the WinUI Dragon Explorer
- recursive search with result limits and cancellation
- safe Copy out to a user-selected destination outside the mounted-image root
- non-destructive Copy out that refuses to overwrite existing destination names
- preservation of empty directories during folder Copy out
- Explorer progress/cancellation handling
- executable/script trust warning before shell-opening potentially active content
- live Explorer navigation item backed by the real service
- Mounted dashboard → Dragon Explorer routing using the current Windows drive root
- fallback Explorer navigation to the first currently mounted volume with a drive letter
- Core smoke coverage for Explorer listing, metadata, path-boundary safety, search, Copy out, empty folders, overwrite protection and cancellation
- real Windows Explorer integration against an IMAPI-generated mounted ISO: list → search → Copy out → content verification
- bounded `FilePreviewService` in Core with cancellation
- read-only text Preview with a hard character limit and truncation indication
- safe image Preview rendered inside Dragon Explorer without shell execution
- PDF metadata-only Preview
- media metadata-only Preview with no auto-play
- binary/unsupported metadata fallback
- Dragon Explorer Preview pane that follows the active selection and cancels stale preview requests
- local Recent Images history
- local Favorites
- atomic JSON-backed image-library persistence under the current Windows profile
- Windows case-insensitive image-path deduplication and bounded recent-list pruning that preserves Favorites
- real Images view with Open / Favorite / Unfavorite / Remove actions
- missing-file library state that disables Open rather than failing silently
- Core smoke coverage for Preview classification, bounded reads, cancellation, image-library persistence, deduplication, favorites, removal and pruning
- real mounted-ISO integration proving Dragon Preview reads exact text directly from the mounted image
- `MountHistoryEntry`, `MountHistoryAction` and `IMountHistoryService` for local mounted-image history
- atomic JSON-backed mounted-history persistence with bounded newest-first retention
- dedicated mounted-history smoke-test project and CI gate
- separate LIVE WINDOWS STATE and MOUNTED HISTORY sections in the Mounted dashboard
- safe Clear History workflow that never changes live Windows mount state
- multi-image `ExplorerWorkspaceView` using WinUI `TabView`
- one independent `ExplorerView` per mounted image/root
- duplicate-tab prevention by activating an existing image/root tab
- stale Explorer-tab pruning when a mounted Windows drive root disappears
- safe native drag-out from mounted Explorer files/folders to Windows Explorer/Desktop
- `ExplorerDragOutValidator` in Core for last-moment mounted-root, existence and reparse-point validation
- Copy-only WinUI `DataPackage` transfer using `StorageFile` / `StorageFolder`
- asynchronous `DragStarting` deferral for safe StorageItem resolution
- dedicated drag-out safety smoke-test project and CI gate
- `docs/DRAG-OUT.md` safety notes and a desktop gesture QA matrix
- Windows x64 workflow artifact publishing as `DragonDiskForge-win-x64`

### Changed
- project development version advanced to `0.3.0-alpha.2`
- milestone 0.3 remains in progress with mounted-volume Explorer, Preview/Image Library, mounted-history, multi-image workspace and safe drag-out slices complete
- Explorer navigation opens the multi-image workspace rather than a single global Explorer instance
- closing an Explorer tab never unmounts the image
- successful Forge unmount closes Explorer tabs backed by that image
- successful native Mount/Unmount operations record local history only after Windows confirms the state transition
- live mount state remains authoritative; history metadata never substitutes for Windows Storage state
- Explorer is enabled only when a real mounted-volume backing path can be resolved from current Windows state
- mounted-volume browsing and Preview remain read-only; writes are limited to explicit Copy out destinations
- drag-out explicitly advertises Copy only and never requests Move
- drag-out rechecks root containment, source existence and runtime reparse attributes immediately before populating the Windows transfer payload
- reparse points/junctions are not traversed during recursive search, Copy out or drag-out
- successfully opened images are recorded to Recent Images best-effort; persistence failure cannot block the core image-open path
- green CI runs publish a Windows x64 artifact for manual desktop validation

### Fixed
- qualified `System.IO.Path` inside the image-library view model to avoid property-name shadowing during WinUI compilation
- disambiguated `System.IO.FileAttributes` from `Windows.Storage.FileAttributes` in the WinUI app
- kept live Mounted inventory independent from mounted-history persistence so metadata failure cannot hide or misreport actual Windows mount state
- replaced the older drag-out branch/PR with a clean branch based on the latest `main`, preserving the x64 artifact pipeline and removing temporary branch-only markers

### Verified
- PR #5 / GitHub Actions run #56 passes Core smoke tests, real ISO/VHD/VHDX integration including Explorer list/search/Copy out, restore and full WinUI `Release|x64` build
- main GitHub Actions run #58 passes the same first-slice regression path after merge and roadmap synchronization
- PR #6 / GitHub Actions run #72 passes Core Preview/Image Library tests, real ISO/VHD/VHDX integration including mounted-ISO text Preview, restore and full WinUI `Release|x64` build
- main GitHub Actions run #73 passes the same Preview/Image Library regression path after merge
- PR #7 / GitHub Actions run #86 passes Core smoke tests, dedicated mounted-history tests, real ISO/VHD/VHDX + Explorer + Preview integration, restore and full WinUI `Release|x64` build before the documentation/version pass
- PR #10 / GitHub Actions run #103 passes Core smoke tests, mounted-history tests, dedicated drag-out safety tests, real Windows mount/Explorer/Preview integration, restore, full WinUI `Release|x64` build and Windows x64 artifact publishing before this documentation/version pass

### Planned
- provider-backed direct browsing without mounting where technically supported
- final 0.3 regression/documentation closure

## [0.2.0] - 2026-09-14

### Added
- `IMountService` contract in Core
- isolated `DragonDiskForge.Windows` service layer for native Windows Storage operations
- native ISO, VHD and VHDX mount support
- native unmount/eject support for proven ISO/VHD/VHDX paths
- read-only-first mount requests
- drive-letter and attached-state detection
- Mount/Unmount progress reporting and cancellation
- live Mounted dashboard backed by Windows state
- Mounted dashboard Refresh, Open drive and Unmount/Cancel actions
- live mounted-image enumeration through the Windows Storage pipeline
- stale-state recovery by re-querying Windows after operations/cancellation
- friendly native-operation and unsupported-format errors
- VHD/VHDX elevation policy isolated to native operations that require it
- real Windows integration tests using disposable VHD/VHDX images created with DiskPart
- real ISO integration fixture generated with Windows IMAPI2FS
- integration validation for mount, read-only state, drive access, mounted inventory and unmount
- cancellation-safety validation proving pre-cancelled mount requests do not alter storage state
- manual non-admin/UAC validation checklist under `docs/MANUAL-VALIDATION.md`

### Changed
- ISO/VHD/VHDX Mount controls are enabled only after their real Windows integration path is proven
- Mounted navigation is now a real working view rather than a locked future placeholder
- native mounted state is treated as Windows-owned state instead of app-session cache
- project version advanced from 0.1.0 to 0.2.0

### Fixed
- corrected the first Mounted inventory implementation after integration tests showed that direct `MSFT_DiskImage` class enumeration was not reliable
- mounted-image enumeration now uses the Windows Storage `Get-Volume → Get-DiskImage` path
- corrected the integration harness import for `MountOperationException`

### Verified
- ISO mount → drive detection → file access → unmount passes on Windows CI
- VHD mount → read-only state → unmount passes on Windows CI
- VHDX mount → drive-letter detection → read-only state → unmount passes on Windows CI
- live Mounted inventory includes attached images and removes them after unmount
- pre-cancelled mount operations leave images detached
- unsupported mount formats produce a friendly capability error
- Core smoke tests, Windows mount integration, restore and full WinUI `Release|x64` build pass on `main`

### Manual QA note
- GitHub-hosted Windows runners execute as administrators, so the visible UAC prompt in a normal non-admin desktop session is tracked separately in `docs/MANUAL-VALIDATION.md` before public beta packaging.

## [0.1.0] - 2026-09-14

### Added
- Initial WinUI 3 / .NET 10 application shell
- Separate `DragonDiskForge.Core` engine project
- Drag-and-drop image loading and file picker
- Supported-format catalogue
- Signature-based detection for ISO, VHD, VHDX, QCOW2, DMG and WIM/ESD
- SHA-256 verification
- Dragon visual design system with obsidian, charcoal, ember, molten and crimson design tokens
- Custom Dragon DiskForge dragon-head/sigil in the application shell
- Vector Dragon sigil asset under `docs/branding/dragon-sigil.svg`
- Branded Forge dashboard and disk-image drop zone
- Dragon-styled capability cards, status pills and image action card
- Branded Dragon startup overlay with Forge loading state
- Subtle dragon-scale geometry in the Forge hero surface
- Responsive desktop layout for compact and narrow window widths
- Animated startup fade and selected-image reveal
- completed light-theme surfaces and system-aware High Contrast resources
- final Windows application `.ico` wired into the WinExe build
- `ImageVerificationService` in Core for shared SHA-256 verification
- SHA-256 progress reporting and cancellation in Core
- Verify/Cancel UI with live percentage while hashing large images
- Core smoke-test harness
- GitHub Actions Windows x64 validation pipeline
- testing, roadmap, milestone and status documentation

### Fixed
- corrected solution platform mappings so `Release|x64` restores and builds in CI
- corrected the SHA-256 smoke-test delegate
- corrected startup-overlay stacking after WinUI rejected `Grid.ZIndex`
- replaced a corrupted binary icon upload with a valid Win32 `.ico` resource accepted by the Release compiler

### Verified
- Core signature/fallback/error/SHA-256/progress/cancellation smoke tests pass
- Dragon startup, responsive layout, dark/light/High Contrast resources and future-feature locking pass full WinUI `Release|x64` CI
- milestone 0.1 exit criteria passed on `main`
