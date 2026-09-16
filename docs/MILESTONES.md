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

Current milestone completion is approximately **77%**.

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

### QCOW/QCOW2 execution slice ✅

- `.qcow` and `.qcow2`
- QCOW v1 plus QCOW2 v2/v3
- `IQcowMetadataProvider` + `QcowMetadataInfo`
- shared `VirtualDiskMetadata` capability
- big-endian `QFI\xFB` header validation
- QCOW v1 cluster/L2/L1/backing metadata validation
- QCOW2 L1/refcount/snapshot offset and range validation
- v3 incompatible/compatible/autoclear feature-mask validation
- v3 refcount-order and header-length checks
- Zstandard compression metadata accepted only when feature and extended header agree
- backing names bounded to 1023 bytes, strict UTF-8 and never followed/opened
- corrupt, external-data, unknown feature and unsupported autoclear states rejected
- cancellation propagation
- no cluster translation, guest-sector I/O, Direct Browse, Mount or Convert
- dedicated Windows CI smoke tests

### Next execution slices

- DMG provider
- WIM/ESD provider
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
- PR #23 / run #207 — QCOW/QCOW2 code head passed provider tests, Explorer safety, ISO/native Windows integration, Release x64 build and artifact before documentation synchronization

A capability becomes user-visible only after its real backing path and tests exist.
