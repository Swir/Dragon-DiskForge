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

## 0.4 Extended Image Providers — IN PROGRESS 🚧

Development version: **0.4.0-alpha.1**.

Current milestone completion is approximately **59%**.

### Completed execution slices

1. **Provider registry foundation** ✅
   - centralized `ProviderRegistry`
   - explicit capability reporting
   - extension-first resolution plus provider/signature fallback
   - probe and inspection failure isolation
   - cancellation preserved across provider fallback
   - provider diagnostics and duplicate-ID protection
   - ISO9660/Joliet direct browsing migrated to registry resolution

2. **IMG / RAW partition provider** ✅
   - read-only `.img`, `.raw` and `.dd` support
   - MBR primary partitions
   - bounded EBR logical partitions with loop detection
   - GPT partition tables with 512/4096-byte logical-sector probing
   - partition type/name metadata
   - strict image-boundary validation
   - explicit `PartitionTable` capability
   - Direct Browse, Mount and Convert remain disabled because those backends are not implemented for RAW
   - dedicated RAW/IMG smoke tests in Windows CI

3. **IMA / floppy media provider** ✅
   - read-only `.ima` and `.flp` support
   - standard raw floppy capacities from 160 KB through 2.88 MB
   - exact geometry metadata
   - FAT-style BPB parsing where present
   - BPB capacity and CHS validation against the physical image geometry
   - blank/unformatted standard-size images supported without fabricated filesystem claims
   - explicit `MediaGeometry` capability
   - Direct Browse, Mount and Convert remain disabled until the real filesystem/native backends exist
   - dedicated IMA/floppy smoke tests in Windows CI

4. **BIN / CUE track-layout provider** ✅
   - read-only `.cue` and same-name companion `.bin` support
   - `ITrackLayoutProvider` plus explicit `TrackLayout` capability
   - BINARY CUE parsing for AUDIO, MODE1/2048, MODE1/2352, MODE2/2336 and MODE2/2352
   - single-file and multi-file CUE layouts
   - bounded CUE size, line count and track count
   - referenced BIN existence, sector alignment, INDEX 00/01 ordering and track-range validation
   - payload paths confined to the CUE directory
   - mixed sector sizes within one BIN rejected instead of guessing offsets
   - Direct Browse, Mount and Convert remain disabled until real backends exist
   - dedicated BIN/CUE smoke tests in Windows CI

5. **MDF / MDS CD track-layout provider** ✅
   - read-only `.mds` plus companion `.mdf` support
   - reuses `ITrackLayoutProvider` and `TrackLayout`
   - `MEDIA DESCRIPTOR` signature/version/medium validation
   - bounded session and track-block traversal
   - explicit sector-size, start-sector and MDF byte-offset parsing
   - mixed-sector CD layouts supported through explicit byte offsets rather than inference
   - same-name MDF plus footer-based ASCII/UTF-16 payload resolution including `*.mdf`
   - descriptor and payload bounds validation
   - rooted/path-traversal payload references rejected
   - DVD-style MDS explicitly rejected until separately implemented and tested
   - Direct Browse, Mount and Convert remain disabled until real backends exist
   - dedicated MDF/MDS smoke tests in Windows CI

6. **NRG track-layout provider** ✅
   - read-only `.nrg` support through `ITrackLayoutProvider` / `TrackLayout`
   - classic `NERO` v1 footer and 32-bit chunk-table offset
   - `NER5` v2 footer and 64-bit chunk-table offset
   - CUES v1 BCD MSF-to-LBA decoding
   - CUEX v2 signed-LBA decoding
   - DAOI v1 and DAOX v2 bounded track byte ranges
   - cue index 1 provides logical StartSector while DAO provides physical byte offsets
   - cue audio/data control cross-checked against DAO mode
   - malformed BCD/MSF, unsupported sector/mode, missing cue metadata and metadata overlap rejection
   - terminating empty `END!` required; trailing metadata rejected
   - cancellation propagation
   - Direct Browse, Mount and Convert remain disabled until real backends exist
   - dedicated NRG v1/v2 smoke tests in Windows CI

### Next execution slices

- CCD/IMG/SUB provider
- VMDK provider
- QCOW/QCOW2 provider
- remaining virtual-disk/container providers from `docs/ROADMAP.md`
- continued hardening of the internal provider contract before any public plugin/API stability promise

### 0.4 validation checkpoints

- PR #16 / run #153 — new RAW/IMG provider tests passed before the final UI/docs synchronization pass
- PR #17 / run #165 — IMA/floppy provider plus full ISO/native-mount regression, WinUI Release x64 build and artifact publication passed
- PR #18 / run #172 — BIN/CUE provider plus all previous provider tests, ISO/native-mount regression, WinUI Release x64 build and artifact publication passed
- PR #19 / run #182 — final MDF/MDS CD branch head plus explicit DVD-scope safety, all previous provider tests, ISO/native-mount regression, WinUI Release x64 build and artifact publication passed
- PR #20 / run #185 — NRG v1/v2 provider plus all previous provider tests, ISO/native-mount regression, WinUI Release x64 build and artifact publication passed before docs synchronization

`docs/ROADMAP.md` remains the source of truth for exact provider order and completion state. A provider capability becomes user-visible only after its backing operation and tests exist.
