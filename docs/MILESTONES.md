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

### Final guest partition structure hardening ✅

- validates primary GPT header CRC32 before trusting guest metadata
- validates the declared GPT partition-entry-array CRC32 with bounded streaming reads
- cross-checks GPT usable range, backup-header placement and primary entry-array placement against guest geometry
- rejects invalid MBR/EBR boot-status bytes
- constrains EBR links and logical partitions to the declared extended-partition container
- generated fixtures cover valid GPT, corrupt header/table checksums, invalid MBR status and escaping EBR/logical ranges
- no Direct Browse, extraction, Mount, repair or write path is enabled
- PR #40 implementation run #289 passed the guest-intelligence gate and complete provider/intelligence/Explorer/native Windows/Release/clean-package verification and artifact publication ✅

### 0.5 exit

The automated engineering exit criteria are satisfied. Public beta publication remains a separate release decision gated by `docs/BETA-RELEASE.md`: clean-machine launch/regression, normal-user UAC, real cross-process drag-out, final beta suffix/package verification and Release checksum publication.

## 0.6 Create + Convert + Verify — NEXT 🚧

Next engineering milestone after the final PR #40 docs-synchronized CI/merge:

- image creation/conversion pipeline
- split/join and sparse/compression handling
- SHA-256/SHA-512 verification
- temporary output + atomic finalization
- cancellation/rollback safety

A capability becomes user-visible only after its real backing path and tests exist.
