# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines the product direction. GitHub Issues track concrete execution and validation work.

## 0.1 Foundation + Dragon Visual Identity

Before 0.1 can be closed:

- Windows x64 CI must be green on the current main branch
- Core smoke tests must pass
- Dragon startup/responsive UI must compile cleanly
- verification must stay in Core and support cancellation/progress states
- high-contrast/accessibility-safe states must be reviewed
- final application icon assets must be prepared
- light-theme Dragon identity must be completed
- roadmap and changelog must match the tested implementation

## 0.2 Native Mount + Unmount

0.2 starts only after 0.1 exit criteria are met. It will deliver real ISO/VHD/VHDX mounting, unmount/eject, mount-state detection, read-only behavior, UAC handling, progress/cancellation, errors and stale-state recovery.

No Mount button becomes active before the underlying service is proven by integration tests.
