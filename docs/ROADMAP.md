# Dragon DiskForge — Roadmap

This roadmap tracks implemented, testable product deliverables. A checkbox is completed only when a real backing path exists and the required verification has passed. Documentation, placeholders and CI-only work do not count as feature completion.

## Overall progress — 77% toward 1.0

`███████████████░░░░░ 77%`

- **0.1 Foundation + Dragon UI — COMPLETE ✅**
- **0.2 Native Mount + Unmount — COMPLETE ✅**
- **0.3 Dragon Explorer — COMPLETE ✅**
- **0.4 Extended Image Providers — COMPLETE ✅**
- **0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅**
- **0.6 Create + Convert + Verify — COMPLETE ✅**
- **0.7 Physical Media Tools — 6/7 (~86%) 🚧**
- **0.8 Windows Integration + Power Tools — 3/4 (75%) 🚧**
- **0.9 Quality, Security + Beta Hardening — planned**
- **1.0 Production Release — planned**

Work may advance out of milestone order when an earlier milestone is blocked by a real hardware/manual validation gate. The remaining 0.7 hardware gate is not weakened by progress in 0.8.

## 0.1 Foundation + Dragon UI — COMPLETE ✅

- [x] WinUI 3 / .NET 10 application foundation
- [x] shared Core separation
- [x] Dragon visual identity and custom application icon
- [x] responsive/accessibility resources
- [x] initial image inspection and SHA verification foundation

## 0.2 Native Mount + Unmount — COMPLETE ✅

- [x] Windows ISO/VHD/VHDX native mount path
- [x] read-only-first mount semantics
- [x] mount/unmount state detection
- [x] progress/cancellation
- [x] Windows integration coverage

## 0.3 Dragon Explorer — COMPLETE ✅

- [x] mounted-volume navigation/search
- [x] Preview and safe Copy out
- [x] Recent Images/Favorites/history
- [x] multi-image workspace
- [x] Copy-only Explorer drag-out safety
- [x] provider-backed direct ISO9660/Joliet browsing

## 0.4 Extended Image Providers — COMPLETE ✅

- [x] deterministic provider registry with failure isolation and truthful capabilities
- [x] IMG/RAW
- [x] IMA/floppy
- [x] BIN/CUE
- [x] MDF/MDS
- [x] NRG
- [x] CCD/IMG/SUB
- [x] VMDK metadata
- [x] QCOW/QCOW2 metadata
- [x] DMG/UDIF metadata
- [x] WIM/ESD metadata
- [x] FFU metadata
- [x] provider-contract hardening

## 0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅

- [x] cross-provider MBR/EBR/GPT partition intelligence
- [x] bounded physical filesystem recognition
- [x] BIOS/UEFI bootability and installer recognition
- [x] architecture/label/UUID/GUID/health intelligence
- [x] Windows Analyze surface with text/JSON reporting
- [x] deeper exFAT/FAT32/UDF/NTFS evidence
- [x] bounded UDF root traversal
- [x] QCOW2 guest-byte reader for the proven uncompressed subset
- [x] hosted-sparse VMDK guest-byte reader for the proven subset
- [x] guest-relative partition/filesystem intelligence
- [x] guest GPT/EBR integrity hardening

Public beta publication is a separate release decision governed by `docs/BETA-RELEASE.md`.

## 0.6 Create + Convert + Verify — COMPLETE ✅

- [x] SHA-256 + SHA-512 in one bounded sequential pass
- [x] safe output transaction boundary
- [x] blank RAW creation + proven guest-byte → RAW materialization
- [x] transactional split/join with versioned SHA-256 manifest
- [x] bounded whole-file gzip transport compression/decompression

Sparse-container writing, QCOW2 compressed clusters, VMDK stream-optimized decoding and DMG `blkx` decompression are not claimed.

## 0.7 Physical Media Tools — IN PROGRESS 🚧

Current scope: **6/7 (~86%)**.

- [x] read-only Windows physical-disk inventory
- [x] capacity/bus/removable/vendor/product/revision/serial/system-disk evidence
- [x] hard refusal policy for system disks, ambiguous identity and unknown capacity
- [x] source/destination write-plan preview with same-device/oversize refusal
- [x] exact destination-bound destructive confirmation contract
- [x] bounded execution/fail-safe recovery contract with a hard-gated Windows writer candidate
- [ ] separately validate the Windows physical writer on dedicated disposable media under the documented safety protocol

PR #47 adds the Windows writer candidate, read-only source/target topology preflight, target-volume lock/dismount, sector-aligned bounded transfer, device flush, read-back SHA-256 and a hard-locked disposable-media harness. Run #338 and Disposable Media Guard #10 passed, but they do **not** substitute for a real destructive-media test. No destructive physical-media action is user-visible.

See `docs/PHYSICAL-MEDIA-SAFETY.md`.

## 0.8 Windows Integration + Power Tools — IN PROGRESS 🚧

Current scope: **3/4 (75%)**.

- [ ] Windows file associations and context-menu integration
- [x] shared-Core read-only image/verification CLI (`analyze`, `verify`, `formats`)
- [x] PowerShell-friendly deterministic text/JSON output, stderr diagnostics and stable exit codes
- [x] session restore + settings import/export + sanitized diagnostic export tooling

The CLI reuses the desktop application's canonical Core provider registry and is shipped as a self-contained x64 executable inside the clean Windows package. State/settings commands are restricted to local Dragon DiskForge application state; no destructive image or physical-media command is exposed.

PR #49 implementation run #353 and Disposable Media Guard #25 passed before the third 0.8 deliverable was marked complete.

See `docs/CLI.md`.

## 0.9 Quality, Security + Beta Hardening — PLANNED

- [ ] clean-machine runtime matrix
- [ ] normal-user UAC validation
- [ ] real cross-process Explorer drag-out validation
- [ ] accessibility/keyboard/screen-reader hardening
- [ ] performance and large-image regression benchmarks
- [ ] security review of parsing, packaging and privileged boundaries
- [ ] crash/diagnostic export and release support bundle

## 1.0 Production Release — PLANNED

- [ ] all production release gates green
- [ ] stable Windows package and checksums
- [ ] final supported-format/capability matrix
- [ ] complete end-user documentation
- [ ] signed/tagged GitHub production release
- [ ] post-release clean installation and runtime verification

## Public beta track

The planned first public beta remains **`0.5.0-beta.1`**. It is not considered ready until every required item in `docs/BETA-RELEASE.md` is actually verified. Green host-side CI alone is insufficient.
