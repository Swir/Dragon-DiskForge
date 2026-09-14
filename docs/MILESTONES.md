# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines the product direction. GitHub Issues track concrete execution and validation work.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅

0.1 exit criteria were satisfied on the current main branch:

- Windows x64 CI is green
- Core smoke tests pass
- Dragon startup/responsive UI compiles cleanly
- verification remains in Core and supports progress/cancellation
- system-aware High Contrast resources are implemented
- completed light-theme Dragon surfaces are implemented
- final Windows application icon resource compiles as a Win32 icon
- future navigation/actions are locked instead of behaving like fake features
- roadmap, changelog, README and status documents match the tested implementation

## 0.2 Native Mount + Unmount — COMPLETE ✅

0.2 exit criteria are satisfied on current `main`:

- `IMountService` lives in Core and the Windows implementation is isolated in `DragonDiskForge.Windows`
- ISO, VHD and VHDX use the native Windows Storage path
- read-only is the default mount policy
- drive letters and attached state are detected from Windows
- Mount/Unmount progress and cancellation are surfaced to the UI
- ISO/VHD/VHDX are enabled only after integration validation
- the Mounted dashboard enumerates live Windows state, supports Refresh, Open drive and Unmount/Cancel
- stale state is corrected by re-querying Windows instead of trusting a previous app session
- friendly capability/native-operation errors are surfaced to the user
- real integration tests generate disposable VHD/VHDX and ISO fixtures, mount them, inspect state, validate live inventory and unmount them
- pre-cancelled operations are proven not to alter storage state
- full Windows x64 Release CI is green on `main` through run #37

The VHD/VHDX elevation path is implemented, but the visible UAC prompt itself cannot be faithfully tested on GitHub-hosted administrator runners. A non-admin desktop checklist lives in `docs/MANUAL-VALIDATION.md` and remains a manual QA gate before public beta packaging.

## 0.3 Dragon Explorer — NEXT

0.3 will turn mounted/provider-backed images into a full in-app browsing workflow: folder/file tree, breadcrumbs, search, file details, safe extraction, drag-out where safe, previews, recents/favorites and the first multi-image workspace foundations.

The same project rule continues: no Explorer control becomes active before its backing operation exists and is tested.
