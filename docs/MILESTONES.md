# Dragon DiskForge — Milestone Execution

`docs/ROADMAP.md` defines product direction. Pull requests and CI runs prove execution.

## 0.1 Foundation + Dragon Visual Identity — COMPLETE ✅

Windows x64 CI, Core smoke tests, Dragon UI, SHA-256 verification, responsive/accessibility resources and application icon are proven.

## 0.2 Native Mount + Unmount — COMPLETE ✅

Native Windows ISO/VHD/VHDX read-only-first Mount/Unmount, state detection and disposable integration tests are proven. Normal-user UAC remains a manual QA gate.

## 0.3 Dragon Explorer — COMPLETE ✅

Released version: **0.3.0**.

- Mounted-volume Explorer ✅
- Preview + Image Library ✅
- Mounted history + multi-image workspace ✅
- Safe Copy-only drag-out ✅
- Provider-backed direct ISO browsing ✅

## 0.4 Extended Image Providers — IN PROGRESS 🚧

Development version: **0.4.0-alpha.1**.

Current milestone completion is approximately **89%**.

### Completed execution slices

1. **Provider registry foundation** ✅
2. **IMG / RAW partition provider** ✅
3. **IMA / floppy media provider** ✅
4. **BIN / CUE track-layout provider** ✅
5. **MDF / MDS CD track-layout provider** ✅
6. **NRG v1/v2 track-layout provider** ✅
7. **CCD / IMG / SUB track-layout provider** ✅
8. **VMDK sparse metadata provider** ✅
9. **QCOW / QCOW2 metadata provider** ✅
10. **DMG / UDIF metadata provider** ✅
11. **WIM / ESD metadata provider** ✅

### WIM / ESD execution slice ✅

- `.wim` and `.esd` standalone Part 1/1 containers
- `IWimMetadataProvider` + `WimMetadataInfo`
- truthful `ContainerMetadata` capability
- 208-byte little-endian `MSWIM\0\0\0` header validation
- standard WIM version `68864` and solid/ESD version `3584`
- flags, chunk size, GUID, part/image counts and boot-index validation
- lookup-table, XML, boot-metadata and integrity resource descriptors
- resource stored-size/flag packing validated
- all resource ranges bounded against the physical file
- split/spanned and `WRITE_IN_PROGRESS` states rejected
- unknown flags, invalid chunk geometry and invalid boot metadata rejected
- cancellation propagation
- no decompression, file-tree traversal/extraction, encrypted ESD handling, Direct Browse, Mount or Convert
- dedicated Windows CI smoke tests

### Next execution slices

- FFU provider
- provider-contract hardening before any public stability promise

### Validation checkpoints

- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy
- PR #18 / run #172 — BIN/CUE
- PR #19 / run #182 — MDF/MDS
- PR #20 / run #185 — NRG
- PR #21 / run #198 — CCD/IMG/SUB final head
- PR #22 / run #205 — VMDK final head
- PR #23 / run #212 — QCOW/QCOW2 final head
- PR #24 / run #219 — DMG/UDIF final head
- PR #25 / run #222 — WIM/ESD code head passed provider tests, Explorer safety, ISO/native Windows integration, Release x64 build and artifact before documentation synchronization

A capability becomes user-visible only after its real backing path and tests exist.
