# Changelog

All notable changes to Dragon DiskForge will be documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Planned
- Milestone 0.4 Extended Image Providers
- IMG / RAW partition provider work
- provider capability/fallback/isolation consolidation

## [0.3.0] - 2026-09-15

### Added
- `ExplorerEntry` model and `IExplorerService` contract in Core
- filesystem-backed mounted-volume Explorer for ISO/VHD/VHDX drive roots
- folders-first browsing, folder navigation, Up and breadcrumb/address display
- recursive mounted-volume search with cancellation and result limits
- safe mounted-volume Copy out with progress/cancellation, overwrite protection, empty-folder preservation and root/reparse safety
- executable/script trust warning before shell-opening active content
- Mounted dashboard → Dragon Explorer routing using current Windows state
- real mounted-ISO Explorer integration: list → search → Copy out → content verification
- bounded `FilePreviewService` with read-only text Preview and truncation indication
- image Preview rendered without shell execution
- PDF/media metadata-only Preview and binary fallback
- Preview cancellation so stale selections cannot replace a newer selection
- local Recent Images + Favorites with atomic JSON persistence and Windows path deduplication
- real Images view with Open / Favorite / Unfavorite / Remove actions
- `MountHistoryEntry`, `MountHistoryAction` and `IMountHistoryService`
- atomic mounted-history persistence with bounded newest-first retention
- dedicated Mounted history UI and safe Clear History behavior
- multi-image `ExplorerWorkspaceView` using WinUI `TabView`
- one independent mounted Explorer session per image/root with duplicate-tab prevention and stale-tab pruning
- safe native drag-out from mounted Explorer files/folders to Windows Explorer/Desktop
- `ExplorerDragOutValidator` with mounted-root, stale-source and reparse-point validation
- Copy-only WinUI `DataPackage` transfer using `StorageFile` / `StorageFolder`
- dedicated drag-out safety smoke tests and manual cross-process gesture QA matrix
- `IDirectBrowseProvider` and `IDirectImageExplorer` contracts for read-only browsing without a Windows mount
- direct-browse provider registry that enables only providers that accept the real image
- ISO9660/Joliet direct provider in Core
- virtual ISO list/navigation/search without assigning a Windows drive
- safe direct-provider Copy out for files/folder trees with progress/cancellation and overwrite protection
- Windows-name sanitization and path-collision checks for provider extraction
- fail-closed handling for unsupported multi-extent ISO file records
- dedicated direct-provider Explorer tabs inside the existing multi-image workspace
- independent lifetime for mounted and direct-provider tabs
- real Windows IMAPI integration proving ISO9660/Joliet list/search/Unicode/Copy out while `Get-DiskImage` remains detached
- direct-provider rejection of invalid fake ISO payloads
- Windows x64 workflow artifact publishing as `DragonDiskForge-win-x64`

### Changed
- project version advanced from `0.3.0-alpha.2` to `0.3.0`
- Explorer navigation uses a multi-image workspace that can host both mounted-volume and tested direct-provider sessions
- successful Forge unmount closes only tabs backed by that mounted image; direct-provider tabs remain independent
- live Windows mount state remains authoritative; history metadata never substitutes for Windows Storage state
- mounted-volume browsing, Preview and direct-provider browsing remain read-only-first
- explicit Copy out is the only write path from direct-provider sessions
- direct-provider virtual entries intentionally do not expose shell Open, Preview or drag-out until materialized as real Windows files
- drag-out advertises Copy only and never Move
- successfully opened images continue to record Recent Images best-effort without blocking image open
- CI order now includes dedicated direct ISO browse integration before native mount regression

### Fixed
- qualified `System.IO.Path` inside image-library view-model code to avoid name shadowing
- disambiguated `System.IO.FileAttributes` from `Windows.Storage.FileAttributes`
- kept live Mounted inventory independent from mounted-history persistence
- replaced the stale first drag-out branch with a clean latest-main implementation
- corrected the direct-ISO test fixture generator to use CodeDom-compatible C# syntax on GitHub-hosted Windows runners

### Verified
- PR #5 / run #56: mounted Explorer vertical slice passes Core, real ISO/VHD/VHDX integration and WinUI Release x64
- main run #58: mounted Explorer regression after merge
- PR #6 / run #72 and main #73: Preview/Image Library and mounted-ISO Preview regression
- PR #7 / run #86: mounted history + multi-image workspace with dedicated history tests
- PR #10 / runs #103 and #111: drag-out safety, full Windows integration, WinUI Release x64 and artifact publication
- main run #112: drag-out post-merge regression
- PR #11 / run #115: real direct ISO9660/Joliet browse/search/Copy out while detached, native mount regression, restore, full WinUI Release x64 and artifact publication
- final `0.3.0` documentation/version head and post-merge main regression are required before formal milestone closure

### Manual QA notes
- GitHub-hosted Windows runners execute as administrators, so visible normal-user UAC prompt behavior remains tracked in `docs/MANUAL-VALIDATION.md`.
- The human cross-process drag gesture remains a desktop QA case even though its validator and compiled WinUI path are automated.

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
