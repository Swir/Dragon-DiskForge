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

Current milestone completion is approximately **95%**.

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

### FFU execution slice ✅

- `.ffu` container recognition
- `IFfuMetadataProvider` + `FfuMetadataInfo`
- truthful `ContainerMetadata` capability
- common 32-byte `SignedImage ` security-header validation
- common 24-byte `ImageFlash ` + NUL image-header validation
- SHA-256 algorithm metadata id `0x0000800C`
- chunk geometry and alignment validation
- bounded catalog/hash-table and image/manifest regions
- common 248-byte store metadata parsing
- PlatformID, block size and descriptor count/length metadata
- all declared metadata ranges bounded against the physical file
- cancellation propagation
- no write-descriptor destination interpretation
- no device access, payload application, sector writing, Direct Browse, Mount or Convert
- dedicated Windows CI smoke tests

### Remaining execution slice

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
- PR #25 / run #222 — WIM/ESD code head passed full provider/native/build/artifact path
- PR #26 / run #225 — FFU code head passed full provider/native/build/artifact path

A capability becomes user-visible only after its real backing path and tests exist.
