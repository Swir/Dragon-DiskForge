# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

**0.4 Extended Image Providers — COMPLETE ✅**

## Current development version

**0.4.0-alpha.1**

## Overall project progress

**44% toward 1.0** — milestones 0.1 through 0.4 have completed their required engineering scope.

## 0.4 final state

**100% complete.** The provider foundation, eleven additional image families and the provider-contract hardening gate are implemented and validated.

### Proven 0.4 slices

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
13. Provider-contract hardening ✅

### Provider-contract hardening proven scope

- provider descriptor snapshot created at registration time
- provider IDs validated as stable restricted identifiers
- null, empty, path-like and duplicate normalized extensions rejected
- intentional signature-only providers with zero extensions preserved
- blank capability-specific display names fall back to provider ID
- registry matching uses immutable normalized descriptor extensions
- equal-priority providers are ordered deterministically by ID
- extension-first resolution, fallback, failure isolation and cancellation behavior preserved

## Proven validation checkpoints

- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy
- PR #18 / run #172 — BIN/CUE
- PR #19 / run #182 — MDF/MDS
- PR #20 / run #185 — NRG
- PR #21 / run #198 — CCD/IMG/SUB
- PR #22 / run #205 — VMDK
- PR #23 / run #212 — QCOW/QCOW2
- PR #24 / run #219 — DMG/UDIF
- PR #25 / run #222 — WIM/ESD
- PR #26 / run #226 — final FFU head + full regression/build/artifact
- PR #27 / run #228 — provider-contract hardening + complete provider/native/build/artifact regression

## Next milestone

**0.5 Partitions + File Systems + Image Intelligence — NEXT**

The first 0.5 work will build cross-provider image intelligence on the hardened 0.4 contract. The planned first public beta remains `0.5.0-beta.1` and is not considered ready until the agreed 0.5 scope and beta gates are proven.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers validate metadata offsets/ranges against the physical image and reject contradictory or unknown states instead of guessing.

Closing 0.4 does not create a stable public plugin API and does not enable any new destructive operation.

Interactive UAC and the real cross-process Explorer drag gesture remain manual QA cases in `docs/MANUAL-VALIDATION.md`.
