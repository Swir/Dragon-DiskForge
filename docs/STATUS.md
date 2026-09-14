# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

## Current milestone

**0.3 Dragon Explorer — IN PROGRESS 🚧**

### Proven on current main

- WinUI 3 / .NET 10 application shell with Dragon visual identity
- Core/app separation
- disk-image catalogue and signature detection
- shared Core SHA-256 verification with progress and cancellation
- native Windows mount/unmount for ISO, VHD and VHDX
- read-only-first native mount behavior
- drive-letter and mount-state detection
- live Mounted dashboard backed by Windows state
- stale-state recovery by re-querying Windows
- Dragon Explorer backed by a real `IExplorerService` Core contract
- mounted-volume folder/file browsing for ISO/VHD/VHDX roots
- Up navigation and address/breadcrumb display
- file/folder metadata including size and modified time
- recursive search with cancellation and result limits
- safe Copy out to an explicit destination outside the mounted root
- non-destructive Copy out that refuses to overwrite existing destination names
- empty-directory preservation during folder Copy out
- executable/script trust warning before opening active content
- Mounted dashboard → Dragon Explorer routing using current Windows state
- path-boundary protection against `..` escape
- no recursive traversal through reparse points/junctions
- Core Explorer smoke tests for listing, search, path safety, Copy out, overwrite protection and cancellation
- real Windows integration test against a mounted IMAPI ISO: list → search → Copy out → verify content
- Windows x64 Release CI green on `main` through run #58

### Current safety state

Inspection, hashing, mounted-volume browsing and default native mounts are read-only-first. Explorer never writes into the mounted image during normal browsing. Copy out writes only to an explicit destination chosen by the user and refuses silent overwrite conflicts.

Executable/script content from a mounted image requires a trust warning before shell-open. Reparse points and junctions are not recursively traversed during search or Copy out.

The interactive UAC prompt cannot be faithfully exercised on GitHub-hosted administrator runners. Its non-admin desktop validation remains documented in `docs/MANUAL-VALIDATION.md` as a manual QA gate before public beta packaging.

## Remaining 0.3 scope

- preview framework for images/text/PDF/media metadata
- recent images
- favorites
- mounted history
- multi-image workspace/tabs
- drag-out where technically safe
- provider-backed direct browsing without mounting where technically supported

Milestone 0.3 remains open until these remaining user-facing Explorer/workspace capabilities are implemented and tested.
