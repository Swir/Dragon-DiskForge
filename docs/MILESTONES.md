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

## 0.4 Extended Image Providers — COMPLETE ✅

Development version at milestone close: **0.4.0-alpha.1**.

Required 0.4 engineering scope: **100% complete**.

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
12. **FFU metadata provider** ✅
13. **Provider-contract hardening** ✅

### Final 0.4 validation

- PR #26 / run #226 — final FFU docs-synchronized head
- PR #27 / run #228 — hardening code head passed provider tests, Explorer safety, ISO/native Windows integration, Release x64 build and artifact

Closing 0.4 completes the internal engineering contract only. It does not declare a stable public plugin API.

## 0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧

Development version: **0.5.0-alpha.1**.

Current milestone completion is approximately **11%**.

### Completed execution slices

1. **Cross-provider partition intelligence** ✅

### Cross-provider partition intelligence ✅

- capability-driven `PartitionIntelligenceService`
- provider resolution through `ProviderRegistry` + `PartitionTable`
- stable finding codes and severities
- duplicate index and zero-length checks
- LBA arithmetic overflow checks
- byte offset/size consistency checks
- physical image-bound checks
- overlapping partition-range checks
- bootable partition count preserved without overstating health
- capability-driven fake providers used in smoke tests
- read-only analysis only; no mount, write or repair path

### 0.5 validation checkpoints

- PR #28 / run #231 — cross-provider partition intelligence code head passed dedicated tests, all provider regressions, Explorer safety, ISO/native Windows integration, Release x64 build and artifact before documentation synchronization

### Next execution slices

- bounded ISO9660/UDF filesystem recognition and metadata
- FAT/FAT32/exFAT recognition and metadata
- NTFS metadata where supported
- ext-family recognition
- bootability and BIOS/UEFI intelligence
- Windows/Linux installer recognition
- architecture, label and UUID/GUID intelligence
- health/corruption warnings grounded in proven metadata

A capability becomes user-visible only after its real backing path and tests exist.
