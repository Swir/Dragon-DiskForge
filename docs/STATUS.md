# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

## Current version

**0.4.0-alpha.1**

## Overall project progress

**40% toward 1.0** — 0.1, 0.2 and 0.3 are complete. Milestone 0.4 now has its provider foundation plus eight additional image families implemented and validated.

## Current milestone

**0.4 Extended Image Providers — IN PROGRESS 🚧**

Current milestone completion is approximately **77%**.

## Proven 0.4 slices

1. Provider registry foundation ✅
2. IMG/RAW partition provider ✅
3. IMA/floppy media provider ✅
4. BIN/CUE track-layout provider ✅
5. MDF/MDS CD track-layout provider ✅
6. NRG v1/v2 track-layout provider ✅
7. CCD/IMG/SUB track-layout provider ✅
8. VMDK sparse metadata provider ✅
9. QCOW/QCOW2 metadata provider ✅

### QCOW/QCOW2 proven scope

- QCOW v1 and QCOW2 v2/v3
- `IQcowMetadataProvider` + `QcowMetadataInfo`
- shared `VirtualDiskMetadata` capability
- `QFI\xFB` magic and big-endian header parsing
- QCOW v1 virtual size, cluster/L2 geometry, modification time, encryption, L1 and backing-name metadata
- QCOW2 cluster size, virtual size, encryption, L1, refcount and snapshot metadata
- QCOW2 v3 feature masks, refcount order and header-length validation
- optional non-default Zstandard compression metadata when v3 feature/header fields agree
- backing filename read only as bounded UTF-8 metadata; never followed/opened
- unknown/unsupported feature states rejected
- corrupt and external-data QCOW2 modes rejected
- no cluster translation, virtual-sector reads, Direct Browse, Mount or Convert

## Proven validation checkpoints

- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy
- PR #18 / run #172 — BIN/CUE
- PR #19 / run #182 — MDF/MDS
- PR #20 / run #185 — NRG
- PR #21 / run #198 — CCD/IMG/SUB
- PR #22 / run #205 — final VMDK head + full regression/build/artifact
- PR #23 / run #207 — QCOW/QCOW2 + all previous providers, Explorer safety, ISO/native Windows integration, Release x64 build and artifact

## Next 0.4 provider

**DMG** — bounded read-only container/trailer metadata inspection first. Decompression and filesystem access stay disabled until separately implemented and tested.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers validate metadata offsets/ranges against the physical image and reject contradictory or unknown states instead of guessing.

VMDK and QCOW currently expose metadata only; neither exposes guest-sector translation, filesystem browsing, Mount or Convert.

Interactive UAC and the real cross-process Explorer drag gesture remain manual QA cases in `docs/MANUAL-VALIDATION.md`.
