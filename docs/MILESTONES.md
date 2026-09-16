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

## 0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅

Development version remains **0.5.0-alpha.1** until the independent public-beta gate is satisfied.

Required automated engineering scope: **100% complete**.

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
14. **Final guest partition structure hardening** ✅

### 0.5 exit

The automated engineering exit criteria are satisfied. Public beta publication remains a separate release decision gated by `docs/BETA-RELEASE.md`: clean-machine launch/regression, normal-user UAC, real cross-process drag-out, final beta suffix/package verification and Release checksum publication.

## 0.6 Create + Convert + Verify — IN PROGRESS 🚧

Current engineering completion: approximately **40%** based on 2 of 5 top-level roadmap deliverables completed and validated.

### Completed execution slices

1. **Dual SHA-256/SHA-512 verification foundation** ✅
   - `ImageVerificationInfo` returns SHA-256, SHA-512 and exact hashed byte count
   - the combined API computes both digests in a single bounded sequential file pass
   - existing SHA-256 callers remain source-compatible through `ComputeSha256Async`
   - dedicated `ComputeSha512Async` API is available
   - progress remains bounded/monotonic; cancellation and missing-file behavior are explicit
   - generated smoke coverage validates multi-buffer data, empty files, both digests, byte count, progress, cancellation and missing files
   - PR #41 implementation run #292 passed the verification gate and the complete Windows regression/build/package path ✅

2. **Temporary output + atomic finalization foundation** ✅
   - reusable `SafeOutputService` stages output in a unique temporary file beside the destination
   - explicit `FailIfExists` and `ReplaceExisting` policies
   - completed temporary output is flushed before final commit
   - racing destinations are handled deterministically rather than overwritten accidentally
   - writer failure and cancellation preserve existing destinations and clean temporary output when possible
   - missing destination directories and unknown overwrite policy values fail closed
   - generated tests cover new output, replacement, cancellation, writer failure, destination races and temp cleanup
   - PR #42 implementation run #296 and final run #297 passed the safe-output gate and the complete Windows regression/build/package path ✅

### Remaining execution slices

- image creation and conversion pipeline
- split/join and sparse/compression handling
- cancellation/rollback safety for real mutating pipelines

The next implementation work should build a real format-producing pipeline on top of `SafeOutputService` so rollback and cancellation are tested through actual mutation orchestration rather than only the shared transaction primitive. A bounded guest-image-to-RAW export over the proven QCOW2/VMDK readers is the preferred next conversion slice.

A capability becomes user-visible only after its real backing path and tests exist.
