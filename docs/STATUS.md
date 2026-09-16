# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

## Current version

**0.4.0-alpha.1**

## Overall project progress

**43% toward 1.0** — 0.1, 0.2 and 0.3 are complete. Milestone 0.4 now has its provider foundation plus eleven additional image families implemented and validated.

## Current milestone

**0.4 Extended Image Providers — IN PROGRESS 🚧**

Current milestone completion is approximately **95%**.

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
10. DMG/UDIF metadata provider ✅
11. WIM/ESD metadata provider ✅
12. FFU metadata provider ✅

### FFU proven scope

- standalone `.ffu` containers in the proven common-header slice
- `IFfuMetadataProvider` + `FfuMetadataInfo`
- truthful `ContainerMetadata` capability
- common 32-byte `SignedImage ` security header validation
- common 24-byte `ImageFlash ` + NUL image-header validation
- SHA-256 algorithm metadata id `0x0000800C`
- chunk-size and chunk-alignment validation
- bounded catalog/hash-table and image/manifest regions
- common 248-byte store metadata parsing
- bounded PlatformID, block size and descriptor count/length metadata
- all declared metadata regions bounded before use
- no write-descriptor destination interpretation
- no payload-to-device mapping, physical-device access, sector writing, image application, Direct Browse, Mount or Convert

## Proven validation checkpoints

- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy
- PR #18 / run #172 — BIN/CUE
- PR #19 / run #182 — MDF/MDS
- PR #20 / run #185 — NRG
- PR #21 / run #198 — CCD/IMG/SUB
- PR #22 / run #205 — final VMDK head + full regression/build/artifact
- PR #23 / run #212 — final QCOW/QCOW2 head + full regression/build/artifact
- PR #24 / run #219 — final DMG/UDIF head + full regression/build/artifact
- PR #25 / run #222 — WIM/ESD + all previous providers, Explorer safety, ISO/native Windows integration, Release x64 build and artifact
- PR #26 / run #225 — FFU + all previous providers, Explorer safety, ISO/native Windows integration, Release x64 build and artifact

## Remaining 0.4 gate

**Provider-contract hardening** — tighten registry, descriptor, capability and deterministic-ordering invariants before 0.4 can close. This does not declare a stable public plugin API.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers validate metadata offsets/ranges against the physical image and reject contradictory or unknown states instead of guessing.

VMDK, QCOW and DMG expose metadata only. WIM/ESD and FFU expose bounded container metadata only. FFU never interprets write destinations or performs device writes.

Interactive UAC and the real cross-process Explorer drag gesture remain manual QA cases in `docs/MANUAL-VALIDATION.md`.
