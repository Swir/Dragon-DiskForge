# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

## Current version

**0.4.0-alpha.2**

## Overall project progress

**32% toward 1.0** — milestones 0.1, 0.2 and 0.3 are complete. Milestone 0.4 is active with its provider foundation and IMG/RAW provider proven. The visible README progress bar is updated only after real roadmap/test progress.

## Current milestone

**0.4 Extended Image Providers — IN PROGRESS 🚧**

### Completed 0.4 slices

**Provider foundation ✅**

- central Core `ProviderRegistry`
- explicit provider capabilities
- deterministic extension-first selection and priority
- fallback to additional providers when a candidate does not accept the image
- probe and inspection failure isolation with diagnostics
- cancellation preserved as a hard stop
- duplicate provider-ID protection
- existing ISO9660/Joliet direct browsing resolves through the registry
- dedicated registry/fallback/isolation smoke tests
- PR #14 / run #146 full Windows regression and x64 artifact

**IMG / RAW provider ✅**

- conservative read-only `.img` / `.raw` provider
- minimum 512-byte size and 512-byte sector-alignment guard
- structured-signature guard so known ISO/VHD/VHDX/QCOW2/WIM/DMG content is not incorrectly claimed as RAW
- registry fallback proves a real ISO renamed to `.img` is handed to the ISO provider
- source SHA-256 before/after proves inspection is read-only
- cancellation coverage
- unsupported Browse/Mount/Convert remain disabled
- PR #15 / run #151 full functional regression, WinUI Release x64 and artifact

### Next 0.4 slice

**IMA / floppy images** — planned next. It must receive its own provider and tests before any capability is enabled.

## Beta direction

The first public GitHub beta is targeted as **`0.5.0-beta.1`** after the required 0.4 providers and the agreed 0.5 partition/filesystem/image-intelligence beta scope are proven. The release gate is tracked in `docs/BETA-RELEASE.md`.

## Current safety state

Inspection, hashing, mounted-volume browsing, provider-backed ISO browsing, Preview and default native mounts are read-only-first. Explorer never writes into an image during normal browsing. Copy out writes only to an explicit destination and refuses silent overwrite conflicts. Drag-out is Copy-only and never requests Move.

The IMG/RAW provider is inspection-only in 0.4. It does not pretend to expose partitions, filesystems, browsing, mounting or conversion before those backends exist.

Provider failures are isolated from other provider candidates. Cancellation is not converted into a parser failure. Unsupported capabilities remain disabled.

Recents, Favorites and Mounted history are local per-user metadata. None of those metadata stores controls or substitutes for Windows mount state.

The interactive UAC prompt and the real cross-process Windows Explorer drag gesture remain documented manual desktop QA cases in `docs/MANUAL-VALIDATION.md`; CI does not fabricate those human-interaction results.
