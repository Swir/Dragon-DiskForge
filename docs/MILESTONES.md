# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines the product direction. GitHub Issues and pull requests track concrete execution and validation work.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅

0.1 exit criteria were satisfied on `main`: Windows x64 CI is green, Core smoke tests pass, Dragon startup/responsive UI compiles cleanly, verification remains in Core with progress/cancellation, accessibility-aware themes are implemented, the final Windows icon compiles and future actions remain locked until real.

## 0.2 Native Mount + Unmount — COMPLETE ✅

0.2 exit criteria are satisfied on `main`: ISO/VHD/VHDX mount/unmount uses the native Windows Storage path, read-only is the default, drive letters and attached state are detected from Windows, progress/cancellation and friendly errors are surfaced, live Mounted state is refreshed from Windows and disposable VHD/VHDX/ISO integration tests are green.

The visible normal-user UAC prompt remains a manual desktop QA case in `docs/MANUAL-VALIDATION.md` because GitHub-hosted Windows runners execute elevated.

## 0.3 Dragon Explorer — COMPLETE ✅

Released version: **0.3.0**.

### Completed execution slices

1. **Mounted-volume Explorer** ✅
   - Core Explorer contract and filesystem-backed service
   - listing/navigation/breadcrumbs/metadata
   - recursive search with cancellation
   - safe Copy out with overwrite and reparse-point protection
   - real mounted-ISO integration coverage

2. **Preview + Image Library** ✅
   - bounded text Preview, safe image Preview, PDF/media metadata modes
   - cancellation-safe selection Preview
   - Recent Images + Favorites with atomic local persistence
   - real Images view and missing-file handling

3. **Mounted history + multi-image workspace** ✅
   - atomic local Mount/Unmount history with bounded retention
   - history metadata kept separate from live Windows mount state
   - dedicated Mounted history UI and safe Clear History
   - dedicated history smoke tests
   - WinUI TabView workspace with one Explorer session per mounted image/root
   - duplicate-tab prevention, stale-tab pruning and safe tab close semantics
   - successful unmount closes only workspace tabs backed by that image

4. **Safe drag-out to Windows Explorer** ✅
   - mounted files/folders expose native WinUI drag-out
   - `DataPackage` advertises Copy only; Dragon never requests Move
   - source path is revalidated against the mounted root before transfer
   - stale sources and path escapes are rejected
   - listed/runtime reparse points and junctions are rejected
   - async StorageItem resolution uses the WinUI DragStarting deferral
   - dedicated drag-out safety smoke tests are part of CI

5. **Provider-backed direct ISO browsing** ✅
   - `IDirectBrowseProvider` provider contract
   - managed read-only ISO9660/Joliet parser
   - virtual-path list/navigation/search directly from image extents
   - safe Copy out without mounting
   - overwrite, path-traversal, filename and extent-boundary validation
   - dedicated `Direct ISO` WinUI tabs marked `NO MOUNT`
   - provider capability is checked before `Explore directly` becomes active
   - real IMAPI integration proves list/search/Copy out while the ISO stays detached

### 0.3 validation checkpoints

- PR #5 / run #56 — first mounted Explorer slice
- PR #6 / run #72 — Preview + Image Library
- PR #7 / run #86 — Mounted history + multi-image workspace
- PR #10 / run #103 — safe drag-out and artifact publication
- PR #12 / run #128 — direct ISO integration, native mount regression, WinUI Release and artifact
- PR #12 / run #131 — same full path remains green after README progress synchronization

The same project rule continues: no Explorer control becomes active before its backing operation exists and is tested. Human cross-process drag and normal-user UAC prompts remain explicit manual desktop QA cases rather than fabricated CI claims.

## 0.4 Extended Image Providers — NEXT 🚧

Next execution focuses on expanding the provider architecture beyond ISO while keeping capability reporting, isolation and read-only safety explicit. `docs/ROADMAP.md` remains the source of truth for the exact provider order and completion state.
