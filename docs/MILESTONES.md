# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines the product direction. GitHub Issues and pull requests track concrete execution and validation work.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅

0.1 exit criteria were satisfied on `main`: Windows x64 CI is green, Core smoke tests pass, Dragon startup/responsive UI compiles cleanly, verification remains in Core with progress/cancellation, accessibility-aware themes are implemented, the final Windows icon compiles and future actions remain locked until real.

## 0.2 Native Mount + Unmount — COMPLETE ✅

0.2 exit criteria are satisfied on `main`: ISO/VHD/VHDX mount/unmount uses the native Windows Storage path, read-only is the default, drive letters and attached state are detected from Windows, progress/cancellation and friendly errors are surfaced, live Mounted state is refreshed from Windows and disposable VHD/VHDX/ISO integration tests are green.

The visible normal-user UAC prompt remains a manual desktop QA case in `docs/MANUAL-VALIDATION.md` because GitHub-hosted Windows runners execute elevated.

## 0.3 Dragon Explorer — COMPLETE ✅

Released version: **0.3.0**.

Completed execution slices:

1. Mounted-volume Explorer ✅
2. Preview + Image Library ✅
3. Mounted history + multi-image workspace ✅
4. Safe Copy-only drag-out to Windows Explorer ✅
5. Provider-backed ISO9660/Joliet direct browsing without mounting ✅

0.3 remains protected by Core/history/drag-out/direct-ISO/native-mount regression gates in CI.

## 0.4 Extended Image Providers — IN PROGRESS 🚧

Current development version: **0.4.0-alpha.2**.

### Slice 1 — Provider foundation ✅

- central Core `ProviderRegistry`
- provider descriptors, explicit capabilities and priorities
- deterministic extension-first selection with fallback
- probe and inspection failure isolation
- provider diagnostics
- cancellation as a hard stop
- duplicate provider-ID protection
- existing ISO direct-browse path migrated into the registry
- dedicated provider-registry smoke tests
- PR #14 / run #146 full Windows regression + Release x64 artifact

### Slice 2 — IMG / RAW ✅

- conservative read-only `.img` / `.raw` provider
- minimum-size and 512-byte sector-alignment validation
- known structured-image signature guard
- renamed ISO `.img` fallback proven through the registry
- read-only SHA-256 before/after verification
- cancellation coverage
- no fake Browse/Mount/Convert capability
- PR #15 / run #151 full Windows regression + Release x64 artifact

### Next slice

**IMA / floppy images**.

The same execution rule continues: implement a real provider path, add failure/cancellation tests, run the complete Windows regression, update docs/progress, then merge. 0.5 filesystem/partition interpretation is not pulled forward into 0.4 merely to make RAW appear more capable.

## Beta target

The first public GitHub beta is targeted for **`0.5.0-beta.1`**. Before that release, the required 0.4 provider families and the agreed 0.5 partition/filesystem/image-intelligence beta scope must pass their automated and manual release gates from `docs/BETA-RELEASE.md`.
