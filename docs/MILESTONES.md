# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines the product direction. GitHub Issues and pull requests track concrete execution and validation work.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅

0.1 exit criteria are satisfied on `main`: Windows x64 CI is green, Core smoke tests pass, the Dragon UI compiles cleanly, verification lives in Core with progress/cancellation, accessibility-aware themes exist and the final Windows icon is packaged.

## 0.2 Native Mount + Unmount — COMPLETE ✅

0.2 exit criteria are satisfied on `main`: ISO/VHD/VHDX mount/unmount uses the native Windows path, read-only is the default, drive letters and attached state are detected from Windows, and disposable integration tests are green.

The visible normal-user UAC prompt remains a manual desktop QA case in `docs/MANUAL-VALIDATION.md`.

## 0.3 Dragon Explorer — COMPLETE ✅

Released version: **0.3.0**.

Completed slices:

1. Mounted-volume Explorer ✅
2. Preview + Image Library ✅
3. Mounted history + multi-image workspace ✅
4. Safe Copy-only drag-out ✅
5. Provider-backed direct ISO browsing ✅

Validation checkpoints include PR #5/#6/#7/#10/#12 with real Windows integration and Release x64 artifacts.

## 0.4 Extended Image Providers — IN PROGRESS 🚧

Development version: **0.4.0-alpha.1**.

Current milestone completion is approximately **71%**.

### Completed execution slices

1. **Provider registry foundation** ✅
   - centralized `ProviderRegistry`
   - explicit capability reporting
   - deterministic extension-first resolution plus fallback
   - probe/inspection failure isolation
   - cancellation hard-stop semantics
   - diagnostics and duplicate-ID protection

2. **IMG / RAW partition provider** ✅
   - MBR/EBR/GPT inspection
   - 512/4096 GPT probing
   - strict boundary checks
   - explicit `PartitionTable` capability

3. **IMA / floppy media provider** ✅
   - standard raw floppy geometry recognition
   - FAT-style BPB inspection with capacity/CHS validation
   - explicit `MediaGeometry` capability

4. **BIN / CUE track-layout provider** ✅
   - bounded CUE parsing
   - AUDIO/MODE1/MODE2 layouts
   - payload/index/range validation
   - explicit `TrackLayout` capability

5. **MDF / MDS CD track-layout provider** ✅
   - bounded MDS descriptor/session/track parsing
   - explicit MDF byte offsets
   - safe payload resolution
   - DVD-style metadata intentionally rejected in this CD-only slice

6. **NRG v1/v2 track-layout provider** ✅
   - NERO/NER5 footer handling
   - CUES/CUEX positions
   - DAOI/DAOX physical ranges
   - cue/DAO consistency and END! validation

7. **CCD / IMG / SUB track-layout provider** ✅
   - CloneCD MODE/INDEX parsing
   - 2352-byte IMG alignment
   - optional 96-byte-per-sector SUB validation
   - safe `.img` fallback to RAW when CCD validation does not succeed

8. **VMDK sparse metadata provider** ✅
   - `IVirtualDiskMetadataProvider` and `VirtualDiskMetadata` capability
   - hosted sparse header version 1 validation
   - magic, flags, virtual capacity, grain size, descriptor location, GTE count, RGD/GD offsets and overhead parsing
   - sector-offset overflow and physical-file bounds checks
   - newline/compression/unclean-shutdown metadata validation
   - bounded embedded descriptor parsing for version/createType/CID/parentCID/extent count
   - text-only descriptors and unproven sparse versions not claimed
   - no grain translation, virtual-sector reads, Direct Browse, Mount or Convert
   - dedicated VMDK smoke tests in Windows CI

### Next execution slices

- QCOW/QCOW2 provider
- DMG provider
- WIM/ESD provider
- FFU provider
- continued hardening of the internal provider contract before any public plugin/API stability promise

### 0.4 validation checkpoints

- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy + full regression/build/artifact
- PR #18 / run #172 — BIN/CUE + full regression/build/artifact
- PR #19 / run #182 — MDF/MDS + full regression/build/artifact
- PR #20 / run #185 — NRG + full regression/build/artifact
- PR #21 / run #198 — final CCD/IMG/SUB head + full regression/build/artifact
- PR #22 / run #200 — VMDK sparse metadata + all previous provider tests, Explorer safety, ISO/native Windows integration, Release x64 build and artifact publication

`docs/ROADMAP.md` remains the source of truth for provider order and completion. A capability becomes user-visible only after its backing operation and tests exist.
