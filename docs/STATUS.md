# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

## Current version

**0.4.0-alpha.1**

## Overall project progress

**36% toward 1.0** — milestones 0.1, 0.2 and 0.3 are complete and proven, while 0.4 now has its provider foundation plus four additional image families implemented. The visible README progress bar must be updated whenever real roadmap progress changes.

## Current milestone

**0.4 Extended Image Providers — IN PROGRESS 🚧**

Current milestone completion is approximately **52%**.

### Proven 0.4 slices

**Provider foundation**

- central Core `ProviderRegistry`
- explicit provider capability reporting
- deterministic priority and extension-first resolution
- provider/signature fallback when an extension candidate does not accept an image
- probe and inspection failure isolation
- cancellation preserved as a hard stop
- provider diagnostics and duplicate-ID protection
- ISO9660/Joliet direct browsing resolved through the registry

**IMG / RAW partition provider**

- read-only `.img`, `.raw` and `.dd` provider
- MBR primary-partition parsing
- bounded EBR logical-partition traversal with loop protection
- GPT parsing with 512/4096-byte logical-sector probing
- common MBR and GPT partition-type decoding
- GPT partition-name decoding
- strict partition-range checks against the image length
- malformed/fake image rejection
- explicit `PartitionTable` capability
- no fake Direct Browse, Mount or Convert capability
- dedicated RAW/IMG smoke tests in Windows CI

**IMA / floppy media provider**

- read-only `.ima` and `.flp` provider
- exact standard floppy geometry recognition from 160 KB through 2.88 MB
- optional FAT-style BIOS Parameter Block parsing
- BPB capacity validation against the real image length
- CHS consistency validation against the recognized physical geometry
- filesystem hint, OEM string, media descriptor and volume-label metadata when present
- blank/unformatted standard-size images recognized without inventing filesystem metadata
- malformed-size, contradictory BPB and foreign-extension rejection
- explicit `MediaGeometry` capability
- no fake Direct Browse, Mount or Convert capability
- dedicated IMA/floppy smoke tests in Windows CI

**BIN / CUE track-layout provider**

- read-only `.cue` and companion `.bin` provider
- explicit `TrackLayout` capability via `ITrackLayoutProvider`
- BINARY CUE parsing for AUDIO, MODE1/2048, MODE1/2352, MODE2/2336 and MODE2/2352
- single-file and multi-file layouts
- INDEX 00/01 parsing and bounded track-range calculation
- referenced BIN existence and sector-alignment validation
- same-name companion-CUE requirement for direct `.bin` resolution
- absolute/path-traversal payload references blocked
- unsupported FILE/track modes and mixed sector sizes within one BIN rejected rather than guessed
- no fake Direct Browse, Mount or Convert capability
- dedicated BIN/CUE smoke tests in Windows CI

**MDF / MDS CD track-layout provider**

- read-only `.mds` and companion `.mdf` provider
- reuses `ITrackLayoutProvider` and the explicit `TrackLayout` capability
- validates the `MEDIA DESCRIPTOR` signature, version, medium type and session/track tables
- parses track number, sector size, start sector and explicit MDF byte offset
- supports mixed-sector CD layouts without guessed offsets
- resolves same-name MDF payloads plus footer names in ASCII or UTF-16, including `*.mdf`
- descriptor offsets, footer strings and track ranges are bounded against the real files
- payload paths are confined to the MDS directory; rooted and traversal paths are rejected
- missing/empty MDF files, unsupported sector sizes, duplicate tracks and malformed metadata are rejected
- DVD-style MDS media is explicitly rejected until its distinct layout is separately implemented and tested
- no fake Direct Browse, Mount or Convert capability
- dedicated MDF/MDS smoke tests in Windows CI

### Proven 0.3 slices

**Mounted-volume Explorer**

- real Core Explorer contract and filesystem-backed service
- mounted ISO/VHD/VHDX browsing
- folders-first listing, navigation, breadcrumbs and metadata
- recursive search with cancellation and result limits
- safe Copy out with overwrite protection and reparse-point safety
- trust warning before shell-opening executable/script content
- real mounted-ISO integration coverage

**Preview + Image Library**

- bounded read-only text Preview with cancellation and truncation indication
- safe image Preview without shell execution
- PDF/media metadata-only Preview
- binary/unsupported metadata fallback
- local Recent Images + Favorites
- atomic JSON persistence with Windows path deduplication
- real Images view with Open / Favorite / Unfavorite / Remove actions

**Mounted history + multi-image workspace**

- local mounted-history contract and atomic JSON persistence
- distinct Mount/Unmount history events with bounded newest-first retention
- live Windows mount state kept independent from history metadata
- separate Mounted dashboard history section
- Clear History cannot alter live mounted state
- dedicated mounted-history smoke tests in CI
- multi-image Dragon Explorer workspace using WinUI tabs
- one independent Explorer session per mounted image/root
- duplicate-tab prevention and stale-tab pruning
- closing a tab never unmounts the image
- successful unmount closes tabs backed by the image

**Safe drag-out to Windows Explorer**

- mounted Explorer rows expose native WinUI drag-out
- drag payload uses Windows Storage items and advertises Copy only
- root containment is revalidated immediately before transfer
- stale/missing sources are blocked
- listed and runtime reparse points/junctions are blocked
- asynchronous StorageItem resolution uses a `DragStarting` deferral
- dedicated drag-out safety smoke tests are green

**Provider-backed direct ISO browsing**

- `IDirectBrowseProvider` provider contract
- managed ISO9660/Joliet parser
- list, nested navigation and recursive search directly from ISO extents
- file/folder Copy out without mounting
- cancellation, overwrite, path traversal, filename and extent-boundary safety
- dedicated **Direct ISO** workspace tab marked **NO MOUNT**
- direct capability button enabled only after positive provider detection
- real IMAPI integration proves the image stays detached through browse/search/Copy out

### Proven checkpoints

- PR #5 / run #56 — first mounted Explorer slice
- PR #6 / run #72 — Preview + Image Library
- PR #7 / run #86 — Mounted history + multi-image workspace
- PR #10 / run #103 — safe drag-out + x64 artifact
- PR #12 / run #128 — provider-backed ISO direct browse + native mount regression + WinUI build + artifact
- PR #12 / run #131 — full regression remained green after README project-progress synchronization
- PR #16 / run #153 — RAW/IMG provider smoke tests passed before final UI/docs synchronization
- PR #17 / run #165 — IMA/floppy provider, full regression, Release x64 build and artifact all passed
- PR #18 / run #172 — BIN/CUE provider, full prior-provider regression, Release x64 build and artifact all passed
- PR #19 / run #181 — MDF/MDS CD provider, explicit DVD-scope rejection, full provider/native regression, Release x64 build and artifact all passed

### Next 0.4 provider

**NRG** — Nero v1/v2 optical track-layout inspection through bounded `NERO`/`NER5` footer and DAOI/DAOX chunk parsing, without claiming extraction, mounting or filesystem browsing until those paths are real.

### Current safety state

Inspection, hashing, mounted-volume browsing, provider-backed ISO browsing, RAW partition-table inspection, floppy geometry/BPB inspection, optical track-layout inspection, Preview and default native mounts are read-only-first. Explorer never writes into an image during normal browsing. Copy out writes only to an explicit destination and refuses silent overwrite conflicts. Drag-out is Copy-only and never requests Move.

Preview text reads are bounded. Image Preview renders without shell execution. PDF and media Preview are metadata-only. Executable/script content requires a trust warning before shell-open. Reparse points and junctions are not recursively traversed during mounted-volume search, Copy out or drag-out.

Direct ISO browsing uses virtual `/` paths and validates metadata extents against the image bounds. Direct mode does not expose Open/Preview/drag-out until provider-backed implementations exist for those actions.

RAW parsing validates MBR/EBR/GPT metadata against the file boundary, bounds extended-chain traversal and does not enable filesystem browsing until the filesystem layer is real and tested.

Floppy inspection validates exact standard media size and, when a BPB exists, verifies capacity and CHS geometry before exposing metadata. It does not expose FAT directory/file browsing yet.

BIN/CUE inspection confines referenced payloads to the CUE directory, validates BIN existence/alignment and track/index ranges, and refuses ambiguous mixed-sector offsets instead of inventing them.

MDF/MDS inspection bounds descriptor structures and MDF track ranges, confines footer-resolved payloads to the descriptor directory and accepts only the CD-style layout currently proven by tests. DVD-style MDS remains disabled rather than being interpreted through CD assumptions.

Recents, Favorites and Mounted history are local per-user metadata. None of those metadata stores controls or substitutes for Windows mount state.

The interactive UAC prompt and the real cross-process Windows Explorer drag gesture remain documented manual desktop QA cases in `docs/MANUAL-VALIDATION.md`; CI does not fabricate those human-interaction results.
