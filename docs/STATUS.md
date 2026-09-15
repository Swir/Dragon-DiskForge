# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

## Current development version

**0.3.0-alpha.2**

## Current milestone

**0.3 Dragon Explorer — IN PROGRESS 🚧**

### Proven slices

**Mounted-volume Explorer**

- real Core Explorer contract and filesystem-backed service
- mounted ISO/VHD/VHDX browsing
- folders-first listing, navigation, breadcrumbs and metadata
- recursive search with cancellation and result limits
- safe Copy out with overwrite protection and reparse-point safety
- trust warning before shell-opening executable/script content
- real mounted-ISO integration coverage

**Preview + Image Library**

- bounded read-only text Preview with cancellation and truncation indication
- safe image Preview without shell execution
- PDF/media metadata-only Preview
- binary/unsupported metadata fallback
- local Recent Images + Favorites
- atomic JSON persistence with Windows path deduplication
- real Images view with Open / Favorite / Unfavorite / Remove actions

**Mounted history + multi-image workspace**

- local mounted-history contract and atomic JSON persistence
- distinct Mount/Unmount history events with bounded newest-first retention
- live Windows mount state kept independent from history metadata
- separate Mounted dashboard history section
- Clear History cannot alter live mounted state
- dedicated mounted-history smoke tests in CI
- multi-image Dragon Explorer workspace using WinUI tabs
- one independent Explorer session per mounted image/root
- reopening the same image/root activates the existing tab
- closing a tab never unmounts the image
- successful unmount closes tabs backed by the image
- stale tabs are pruned when the backing Windows drive root disappears

**Safe drag-out to Windows Explorer**

- mounted Explorer rows expose native WinUI drag-out
- drag payload uses Windows Storage items and advertises Copy only
- root containment is revalidated immediately before transfer
- stale/missing sources are blocked
- listed and runtime reparse points/junctions are blocked
- asynchronous StorageItem resolution uses a `DragStarting` deferral
- dedicated drag-out safety smoke tests are green
- PR #10 / run #103 passed Core, history, drag-out, real Windows integration, restore, WinUI build and artifact publishing before this docs/version pass

### Current safety state

Inspection, hashing, mounted-volume browsing, Preview and default native mounts are read-only-first. Explorer never writes into the mounted image during normal browsing. Copy out writes only to an explicit destination and refuses silent overwrite conflicts. Drag-out is Copy-only and never requests Move.

Preview text reads are bounded. Image Preview renders without shell execution. PDF and media Preview are metadata-only. Executable/script content requires a trust warning before shell-open. Reparse points and junctions are not recursively traversed during search, Copy out or drag-out.

Recents, Favorites and Mounted history are local per-user metadata. None of those metadata stores controls or substitutes for Windows mount state. A persistence failure must not roll back or hide a successful native storage operation.

The interactive UAC prompt cannot be faithfully exercised on GitHub-hosted administrator runners. Its non-admin desktop validation remains documented in `docs/MANUAL-VALIDATION.md` as a manual QA gate before public beta packaging. The real cross-process drag gesture is also a manual desktop QA case even though its validator, WinUI build path and storage integration are automated.

## Remaining 0.3 scope

- provider-backed direct browsing without mounting where technically supported
- final 0.3 regression/documentation closure

Milestone 0.3 remains open until the remaining direct-browsing capability is implemented and tested.
