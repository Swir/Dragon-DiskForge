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

## 0.2 Native Mount + Unmount — NEXT

0.2 starts after the completed 0.1 checkpoint. It will deliver real ISO/VHD/VHDX mounting, unmount/eject, mount-state detection, read-only behavior, UAC handling, progress/cancellation, errors and stale-state recovery.

No Mount button becomes active before the underlying service is proven by integration tests.
