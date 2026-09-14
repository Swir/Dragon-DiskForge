# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines the product direction. GitHub Issues track concrete execution and validation work.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅

0.1 exit criteria were satisfied on `main`: Windows x64 CI is green, Core smoke tests pass, Dragon startup/responsive UI compiles cleanly, verification remains in Core with progress/cancellation, accessibility-aware themes are implemented, the final Windows icon compiles and future actions remain locked until real.

## 0.2 Native Mount + Unmount — COMPLETE ✅

0.2 exit criteria are satisfied on `main`: ISO/VHD/VHDX mount/unmount uses the native Windows Storage path, read-only is the default, drive letters and attached state are detected from Windows, progress/cancellation and friendly errors are surfaced, live Mounted state is refreshed from Windows and disposable VHD/VHDX/ISO integration tests are green.

The visible normal-user UAC prompt remains a manual desktop QA case in `docs/MANUAL-VALIDATION.md` because GitHub-hosted Windows runners execute elevated.

## 0.3 Dragon Explorer — IN PROGRESS 🚧

Current development version: **0.3.0-alpha.1**.

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
   - PR #7 / run #86 green before the documentation/version synchronization pass

### Remaining before 0.3 closure

- drag files out to Windows Explorer where technically safe
- provider-backed direct browsing without mounting where technically supported
- final 0.3 regression pass and documentation/version closure

The same project rule continues: no Explorer control becomes active before its backing operation exists and is tested.
