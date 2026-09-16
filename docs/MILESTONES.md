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
Required 0.4 engineering scope: **100% complete**.

Completed provider/foundation slices: registry foundation, IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB, VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD, FFU and provider-contract hardening.

Final checkpoints: PR #26 / run #226 FFU; PR #27 / run #228 provider-contract hardening.

## 0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧

Development version: **0.5.0-alpha.1**.

Current milestone completion is approximately **99%**.

### Completed execution slices

1. **Cross-provider partition intelligence** ✅
2. **Bounded filesystem-recognition foundation** ✅
3. **Boot + installer intelligence foundation** ✅
4. **Unified identity + health intelligence foundation** ✅
5. **Windows Analyze + text/JSON reporting surface** ✅
6. **Deeper bounded filesystem evidence** ✅
7. **NTFS metadata + architecture-reconciliation hardening** ✅
8. **Clean Windows x64 package-candidate path** ✅
9. **Central version + independent clean-package verification gate** ✅
10. **Bounded UDF root-directory traversal** ✅
11. **Bounded QCOW2 standard guest-byte reader** ✅
12. **Bounded hosted-sparse VMDK guest-byte reader** ✅
13. **Common bounded guest partition/filesystem intelligence** ✅

### Common guest partition/filesystem intelligence ✅

- `GuestPartitionTableReader` parses bounded guest MBR/EBR/GPT metadata through `IGuestByteReader`
- existing partition-layout geometry/overlap/range hardening is reused against virtual guest size
- `GuestFileSystemRecognitionService` scans bounded guest regions for FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS evidence
- dedicated guest-relative detection models prevent physical/guest offset ambiguity
- QCOW2 v2/v3 standard-uncompressed and VMDK hosted-sparse standard-uncompressed readers feed the common analysis path
- text/JSON reports expose guest analysis separately from physical-container analysis
- generated QCOW2/VMDK fixtures prove MBR + FAT12 recognition, OOB refusal and cancellation
- no Direct Browse, filesystem traversal, extraction, Mount, repair or write path is enabled by this analysis surface
- PR #39 implementation run #286 passed the new guest-intelligence gate and complete provider/intelligence/Explorer/native Windows/Release/clean-package verification and artifact publication ✅

### Remaining execution slices

- final 0.5 beta-scope hardening/documentation and release-gate synchronization
- clean-machine launch/open/mount/explore/verify/analyze, normal-user UAC and real cross-process drag-out manual QA
- promote to `0.5.0-beta.1` only when all release gates are actually satisfied

A capability becomes user-visible only after its real backing path and tests exist.
