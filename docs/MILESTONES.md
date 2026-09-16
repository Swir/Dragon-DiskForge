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

Current engineering completion: approximately **20%** based on 1 of 5 top-level roadmap deliverables completed and validated.

### Completed execution slice

1. **Dual SHA-256/SHA-512 verification foundation** ✅
   - `ImageVerificationInfo` returns SHA-256, SHA-512 and exact hashed byte count
   - the combined API computes both digests in a single bounded sequential file pass
   - existing SHA-256 callers remain source-compatible through `ComputeSha256Async`
   - dedicated `ComputeSha512Async` API is available
   - progress remains bounded/monotonic; cancellation and missing-file behavior are explicit
   - generated smoke coverage validates multi-buffer data, empty files, both digests, byte count, progress, cancellation and missing files
   - PR #41 implementation run #292 passed the verification gate and the complete Windows regression/build/package path ✅

### Remaining execution slices

- image creation and conversion pipeline
- split/join and sparse/compression handling
- temporary output + atomic finalization
- cancellation/rollback safety for mutating pipelines

The next implementation work should establish the safe output-transaction foundation before user-visible creation/conversion is enabled: temporary output, same-volume atomic commit where supported, explicit overwrite policy, cancellation cleanup and rollback-oriented tests.

A capability becomes user-visible only after its real backing path and tests exist.
