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

Current milestone completion is approximately **95%**.

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

### QCOW2 guest-byte reader ✅

- generic `IGuestByteReader` read-only contract separates guest-visible addressing from container metadata
- QCOW2 v2/v3 active L1/L2 tables are translated for standard uncompressed mappings
- allocated clusters are read only after table-entry, alignment and physical-range validation
- explicit v3 zero clusters return zeroes; unallocated clusters return zeroes only with no backing file
- backing chains, encryption, dirty active metadata, external data files, non-default compression metadata, extended L2 entries and compressed cluster descriptors fail closed
- guest reads are bounded by declared virtual size and can cross cluster boundaries safely
- generated fixtures cover allocated/zero/unallocated clusters, cross-boundary reads, physical OOB mappings, reserved bits, unsupported states and cancellation
- no Direct Browse, extraction, repair, write or filesystem-analysis capability is enabled by this engine primitive
- PR #37 / implementation run #277 passed the new reader gate plus the complete provider, Explorer, native Windows, Release x64, clean-package verification and artifact path ✅

### Remaining execution slices

- VMDK hosted sparse v1 guest-byte translation for the safe uncompressed subset
- common bounded guest-byte source integration into partition/filesystem intelligence
- final 0.5 beta-scope hardening/documentation synchronization
- clean-machine launch/open/mount/explore/verify/analyze, normal-user UAC and real cross-process drag-out manual QA
- promote to `0.5.0-beta.1` only when all release gates are actually satisfied

A capability becomes user-visible only after its real backing path and tests exist.
