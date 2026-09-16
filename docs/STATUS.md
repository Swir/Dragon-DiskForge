# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

## Current version

**0.4.0-alpha.1**

## Overall project progress

**41% toward 1.0** — 0.1, 0.2 and 0.3 are complete. Milestone 0.4 now has its provider foundation plus nine additional image families implemented and validated.

## Current milestone

**0.4 Extended Image Providers — IN PROGRESS 🚧**

Current milestone completion is approximately **83%**.

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

### DMG / UDIF proven scope

- single-file UDIF `.dmg` images with trailing 512-byte `koly` trailer
- `IDmgMetadataProvider` + `DmgMetadataInfo`
- shared `VirtualDiskMetadata` capability
- big-endian trailer version/header/flags/fork/segment/checksum/XML/image-variant/sector metadata
- all physical ranges bounded before reads and kept before the final trailer
- logical virtual size from the UDIF 512-byte sector count
- optional XML plist limited to 16 MiB
- XML DTD and external resolution disabled
- bounded `blkx` entry counting without decoding `mish` block maps
- multi-segment images explicitly rejected until companion-segment handling exists
- no decompression, guest-sector translation, Direct Browse, Mount or Convert

## Proven validation checkpoints

- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy
- PR #18 / run #172 — BIN/CUE
- PR #19 / run #182 — MDF/MDS
- PR #20 / run #185 — NRG
- PR #21 / run #198 — CCD/IMG/SUB
- PR #22 / run #205 — final VMDK head + full regression/build/artifact
- PR #23 / run #212 — final QCOW/QCOW2 head + full regression/build/artifact
- PR #24 / run #214 — DMG/UDIF + all previous providers, Explorer safety, ISO/native Windows integration, Release x64 build and artifact

## Next 0.4 provider

**WIM / ESD** — bounded read-only WIM header/resource metadata inspection first. Resource decompression, file extraction and encrypted ESD handling stay disabled until separately implemented and tested.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers validate metadata offsets/ranges against the physical image and reject contradictory or unknown states instead of guessing.

VMDK, QCOW and DMG currently expose metadata only; none exposes guest-sector/block translation, filesystem browsing, Mount or Convert.

Interactive UAC and the real cross-process Explorer drag gesture remain manual QA cases in `docs/MANUAL-VALIDATION.md`.
