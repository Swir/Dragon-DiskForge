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

Final 0.4 checkpoints:
- PR #26 / run #226 — final FFU docs-synchronized head
- PR #27 / run #228 — provider-contract hardening + full provider/native/build/artifact regression

Closing 0.4 completes the internal engineering contract only. It does not declare a stable public plugin API.

## 0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧

Development version: **0.5.0-alpha.1**.

Current milestone completion is approximately **93%**.

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

### Cross-provider partition intelligence ✅

- capability-driven `PartitionIntelligenceService`
- stable structural findings for duplicate indexes, zero length, arithmetic overflow, geometry, bounds and overlap
- read-only analysis only
- PR #28 / run #232 docs-synchronized full regression/build/artifact ✅

### Bounded filesystem-recognition foundation ✅

- provider-integrated `FileSystemRecognitionService`
- physical whole-file or structurally validated partition-region scanning only
- FAT12/FAT16/FAT32, exFAT, supported NTFS, ext2/ext3/ext4, ISO9660/Joliet and UDF VRS recognition
- generated fixtures, false-positive and cancellation coverage
- no sparse/compressed guest-sector translation
- PR #29 / run #235 docs-synchronized full regression/build/artifact ✅

### Boot + installer intelligence foundation ✅

- El Torito record/catalog and physical boot-image bounds
- explicit BIOS/UEFI evidence
- bounded Direct Browse traversal
- Windows/Linux installer evidence and EFI fallback architecture hints
- no execution, extraction, repair or writes
- PR #30 / run #242 docs-synchronized full regression/build/artifact ✅

### Unified identity + health intelligence foundation ✅

- `ImageIntelligenceService` composes existing truthful capabilities
- partition/filesystem/container/platform identity evidence
- partition structural findings plus selected exFAT/ext/NTFS/FAT32 health checks
- no invented physical mapping for metadata-only sparse/compressed/container formats
- PR #31 / run #245 implementation head passed full regression/build/artifact before docs synchronization ✅

### Windows Analyze + reporting surface ✅

- bounded Analyze action in the WinUI result card
- `ImageReportService` text/JSON reporting and Save JSON
- unknown-input, serialization and cancellation tests
- `by Swir` + GitHub footer
- PR #32 / run #260 complete Windows CI and Release x64 artifact ✅

### Deeper bounded filesystem evidence ✅

- `FileSystemDepthService` operates only on recognized physical filesystem regions
- exFAT main/backup boot-region checksum and redundancy checks
- FAT32 FSInfo placement, signatures, free-count and next-free validation
- UDF block-256 primary anchor validation
- UDF descriptor tag checksum/location/CRC validation
- UDF main descriptor-sequence inspection capped at 16 MiB
- validated UDF primary/logical volume identity strings flow into the analysis/report path
- PR #33 / run #262 implementation head passed the new gate plus complete prior regression/build/artifact ✅

### NTFS metadata + architecture reconciliation hardening ✅

- bounded NTFS `$MFT` / `$MFTMirr` cluster/range validation
- signed FILE-record size decoding with strict size bounds
- Update Sequence Array geometry and sector-trailer validation
- fixup-normalized first-record mirror comparison with explicit corruption/divergence findings
- no NTFS repair, attribute following or directory traversal
- boot-path + installer-path architecture hints are preserved and de-duplicated
- direct single-source disagreement is surfaced as `ARCHITECTURE_EVIDENCE_CONFLICT`
- generated valid/corrupt/out-of-range NTFS and architecture/cancellation tests
- PR #34 / run #266 implementation head passed new hardening plus complete Windows regression/build path ✅

### Versioned clean Windows package candidate ✅

- product version metadata centralized in `Directory.Build.props`
- CI creates `DragonDiskForge-win-x64.zip` after the Release x64 build
- package staging requires one application EXE and excludes PDB/test-only payloads
- canonical Dragon icon guaranteed in the package root
- manifest records semantic version, executable ProductVersion/FileVersion, x64, entry point, icon and entry-point SHA-256
- SHA-256 sidecar generated by CI
- independent `verify-package.ps1` reopens the ZIP and fails on checksum/manifest/version/architecture/hash/icon/content mismatches
- run #270 passed all existing tests, Explorer/native Windows integration, Release build, clean-package build, independent package verification and both artifact uploads
- downloaded run #270 clean artifact confirmed sidecar ZIP checksum, manifest `0.5.0-alpha.1`, matching EXE SHA, one app EXE and zero PDB files
- this remains a beta candidate, not a public release; final beta suffix, clean-machine/manual and public Release gates remain

### Bounded UDF root-directory traversal ✅

- `UdfTraversalService` operates only on already-recognized physical UDF regions
- validated Type 1 partition maps are translated only through matching validated Partition Descriptors
- virtual/sparable/metadata Type 2 partition maps are refused instead of guessed
- File Set Descriptor and root File Entry descriptor tags/CRC/location are validated before following addresses
- first slice accepts one recorded short allocation extent for the root directory
- root traversal is capped at 8 MiB and 4096 entries
- File Identifier Descriptor checksum/location/CRC and padded bounds are validated before names are accepted
- validated File Set Identifier becomes identity evidence
- OSTA compressed Unicode IDs 8/16 are decoded only after descriptor validation
- no recursion, extraction, Direct Browse capability, repair or write path
- generated healthy/corrupt/out-of-range/unsupported-map/cancellation fixtures
- PR #36 / implementation run #274 passed the new traversal gate plus the complete provider/Explorer/native Windows/Release/package-verification/artifact path before documentation synchronization ✅

### Remaining execution slices

- truthful guest-sector reader foundations before sparse/compressed virtual-disk filesystem inspection
- final 0.5 beta-scope hardening and documentation synchronization
- clean-machine launch/open/mount/explore/verify/analyze, normal-user UAC and real cross-process drag-out manual QA
- promote to `0.5.0-beta.1` only when all release gates are actually satisfied

A capability becomes user-visible only after its real backing path and tests exist.
