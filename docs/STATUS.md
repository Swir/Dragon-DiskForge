# Dragon DiskForge — Current Status

## Completed milestones

- **0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**
- **0.2 Native Mount + Unmount — COMPLETE ✅**
- **0.3 Dragon Explorer — COMPLETE ✅**
- **0.4 Extended Image Providers — COMPLETE ✅**
- **0.5 Partitions + File Systems + Image Intelligence — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

The 0.5 engineering scope is complete, but the public beta version suffix is intentionally not promoted until the independent beta release gate passes.

## Overall project progress

**61% toward 1.0.** Milestones 0.1 through 0.5 have completed their required automated engineering scope. Public beta publication remains separately gated by clean-machine/manual validation and final package/version promotion.

## Next milestone

**0.6 Create + Convert + Verify — NEXT 🚧**

The first 0.6 implementation work may begin while beta manual gates remain open, provided no public beta is published prematurely and 0.5 release-critical regressions are fixed first.

## Proven 0.5 slices

1. Cross-provider partition intelligence ✅
2. Bounded filesystem-recognition foundation ✅
3. Boot + installer intelligence foundation ✅
4. Unified identity + health intelligence foundation ✅
5. Windows Analyze + text/JSON reporting surface ✅
6. Deeper bounded exFAT/FAT32/UDF evidence ✅
7. NTFS metadata + architecture-reconciliation hardening ✅
8. Clean Windows x64 package-candidate + SHA-256 path ✅
9. Central release version + independent package verification gate ✅
10. Bounded UDF root-directory traversal ✅
11. Bounded QCOW2 standard guest-byte reader ✅
12. Bounded hosted-sparse VMDK guest-byte reader ✅
13. Common bounded guest partition/filesystem intelligence ✅
14. Final guest partition structure hardening ✅

### Final guest partition hardening proven scope

- guest GPT primary-header CRC32 validation
- bounded streaming validation of the complete declared GPT partition-entry-array CRC32
- GPT usable ranges, backup-header placement and primary entry-array placement checked against virtual guest geometry
- invalid MBR/EBR boot-status bytes rejected
- EBR chain links and logical partitions constrained to the declared extended-partition container
- generated tests include a valid GPT, corrupt header/table checksums, invalid MBR status and escaping EBR/logical ranges
- no Direct Browse, extraction, Mount, repair or write capability is introduced
- PR #40 implementation run #289 passed the dedicated guest-intelligence gate plus complete provider/intelligence/Explorer/native Windows/Release/clean-package verification and artifact publication

## Proven validation checkpoints

### 0.4
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
- PR #26 / run #226 — FFU
- PR #27 / run #228 — provider-contract hardening

### 0.5
- PR #28 / run #232 — partition intelligence
- PR #29 / run #235 — filesystem recognition
- PR #30 / run #242 — boot/installer intelligence
- PR #31 / run #245 — unified identity/health implementation
- PR #32 / run #260 — Analyze/reporting surface
- PR #33 / run #262 — deeper filesystem evidence
- PR #34 / run #266 — NTFS/architecture hardening + clean package foundation
- PR #35 / run #270 — central version metadata + independent package verification
- PR #36 / run #274 — bounded UDF root traversal
- PR #37 / run #277 — bounded QCOW2 standard guest-byte reader implementation
- PR #38 / run #284 — bounded hosted-sparse VMDK guest-byte reader + full Windows package path
- PR #39 / run #287 — common guest partition/filesystem intelligence + full Windows regression/package path
- PR #40 / implementation run #289 — final guest GPT/EBR integrity hardening + full Windows regression/package path

## Beta readiness

The planned first public beta remains **`0.5.0-beta.1`** and is **NOT READY YET**. Automated 0.5 engineering gates are complete. Remaining blockers are independent release gates:

- promote version/package metadata from `0.5.0-alpha.1` to the final beta suffix only at release time
- launch and exercise the package on a clean supported Windows machine
- confirm no developer SDK/Visual Studio requirement for a normal user
- complete normal-user UAC validation
- complete real cross-process Explorer drag-out validation
- complete basic clean-machine launch/open/mount/explore/verify/analyze regression
- publish the final package SHA-256 and GitHub pre-release only after those checks pass

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers and intelligence services validate metadata offsets/ranges and reject contradictory or unknown states instead of guessing.

The QCOW2 guest reader remains deliberately narrower than full QCOW2 support: no backing chains, encryption, compressed descriptors, external data files, dirty active metadata or extended L2. The VMDK guest reader remains deliberately narrow: one clean hosted-sparse `monolithicSparse` extent, no parent chain and no compressed/stream-optimized/zeroed-entry semantics. Both may feed bounded guest partition/filesystem analysis, but neither advertises Direct Browse, extraction or Mount. Physical and guest-relative offsets are represented separately, and guest GPT checksum/EBR containment checks now run before filesystem probing.

Interactive UAC, clean-machine runtime and the real cross-process Explorer drag gesture remain manual QA gates.
