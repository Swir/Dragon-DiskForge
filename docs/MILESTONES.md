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

Development version: **0.4.0-alpha.1**.

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

### Provider-contract hardening ✅

- validated provider registration and stable provider IDs
- immutable normalized descriptor snapshots
- invalid/duplicate extension declarations rejected early
- signature-only provider support retained
- safe display-name fallback to provider ID
- deterministic priority + provider-ID ordering
- descriptor-based extension matching
- cancellation, fallback and failure isolation preserved
- dedicated registry tests plus complete existing regression path

### Validation checkpoints

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
- PR #26 / run #226 — final FFU docs-synchronized head
- PR #27 / run #228 — hardening code head passed provider tests, Explorer safety, ISO/native Windows integration, Release x64 build and artifact

Closing 0.4 completes the internal engineering contract only. It does not declare a stable public plugin API.

## 0.5 Partitions + File Systems + Image Intelligence — NEXT ⬜

The next execution work begins from the hardened provider layer. A capability becomes user-visible only after its real backing path and tests exist.
