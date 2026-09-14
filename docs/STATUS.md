# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

### Proven on current main

- WinUI 3 / .NET 10 application shell with Dragon visual identity
- Core/app separation
- disk-image catalogue and signature detection
- shared Core SHA-256 verification with progress and cancellation
- automated Core smoke-test harness
- native Windows mount service for ISO, VHD and VHDX
- native unmount/eject for ISO, VHD and VHDX
- read-only-first mount behavior
- drive-letter and mount-state detection
- live Mounted dashboard backed by Windows state rather than app cache
- refresh-safe recovery from stale mounted state
- Mount/Unmount progress and cancellation handling
- friendly unsupported-format and native-operation error translation
- VHD/VHDX elevation policy isolated to the native operation that requires it
- real Windows integration tests that create disposable VHD/VHDX and ISO images, mount them, verify state/read-only/drive access, validate inventory, then unmount them
- cancellation-safety tests proving a pre-cancelled mount does not alter storage state
- Windows x64 Release CI pipeline green on main through run #37

### Current safety state

Inspection, hashing and the default native mount path are read-only-first. ISO/VHD/VHDX mount actions are enabled because their backend paths are proven by Windows integration tests. Unsupported mount formats remain disabled.

The interactive UAC prompt cannot be faithfully exercised on GitHub-hosted administrator runners. Its non-admin desktop validation is documented in `docs/MANUAL-VALIDATION.md` and remains a manual QA gate before public beta packaging.

## Next milestone

**0.3 Dragon Explorer** — in-app folder/file browsing for mounted/provider-backed images, breadcrumbs, search, file details, safe extraction, drag-out where safe, previews, recents/favorites and multi-image workspace foundations.
