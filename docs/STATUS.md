# Dragon DiskForge — Current Status

## Completed milestones

- **0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**
- **0.2 Native Mount + Unmount — COMPLETE ✅**
- **0.3 Dragon Explorer — COMPLETE ✅**
- **0.4 Extended Image Providers — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

## Overall project progress

**60% toward 1.0.** Milestones 0.1 through 0.4 are complete. Milestone 0.5 now includes validated partition intelligence, bounded physical and guest filesystem recognition, boot/installer intelligence, unified identity/health intelligence, Analyze/reporting, deeper exFAT/FAT32/UDF evidence, bounded NTFS metadata depth, architecture reconciliation, bounded UDF root traversal, a verified clean Windows package-candidate path and truthful QCOW2 plus hosted-sparse VMDK guest-byte readers.

## Current milestone

**0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧**

Current milestone completion is approximately **99%**.

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

### Guest partition/filesystem intelligence proven scope

- shared read-only `IGuestByteReader` contract remains the only source of virtual guest bytes
- `GuestPartitionTableReader` parses guest MBR/EBR/GPT structures with virtual-range bounds and safety limits
- guest partition layouts reuse `PartitionIntelligenceService` geometry, overlap and range validation
- `GuestFileSystemRecognitionService` supports FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS evidence
- dedicated guest-relative models keep guest offsets distinct from physical container offsets
- QCOW2 standard uncompressed and hosted-sparse VMDK standard uncompressed readers feed the common guest-analysis path
- `ImageReportService` exposes guest analysis as a separate JSON object and explicit text-report section
- generated fixtures validate QCOW2/VMDK MBR + FAT12 recognition, out-of-range partition refusal and cancellation
- no Direct Browse, extraction, Mount, repair or write capability is enabled by guest analysis
- PR #39 implementation run #286 passed the dedicated guest-intelligence gate plus complete provider/intelligence/Explorer/native Windows/Release/clean-package verification and artifact publication

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
- PR #39 / implementation run #286 — common guest partition/filesystem intelligence + full Windows regression/package path

## Next engineering focus

**Final 0.5 beta-scope hardening and release-gate synchronization**, followed by the remaining clean-machine/manual QA checkpoints.

The planned first public beta remains `0.5.0-beta.1`. It is **not ready yet**: final 0.5 hardening remains unfinished, the version/package pipeline still carries `0.5.0-alpha.1`, and clean-machine/UAC/cross-process drag-out manual gates in `docs/BETA-RELEASE.md` remain open.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers and intelligence services validate metadata offsets/ranges and reject contradictory or unknown states instead of guessing.

The QCOW2 guest reader remains deliberately narrower than full QCOW2 support: no backing chains, encryption, compressed descriptors, external data files, dirty active metadata or extended L2. The VMDK guest reader remains deliberately narrow: one clean hosted-sparse `monolithicSparse` extent, no parent chain and no compressed/stream-optimized/zeroed-entry semantics. Both may now feed bounded guest partition/filesystem analysis, but neither advertises Direct Browse, extraction or Mount. Physical and guest-relative offsets are represented separately.

Interactive UAC, clean-machine runtime and the real cross-process Explorer drag gesture remain manual QA gates.
