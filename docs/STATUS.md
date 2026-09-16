# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

**0.4 Extended Image Providers — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

## Overall project progress

**56% toward 1.0** — milestones 0.1 through 0.4 are complete. Milestone 0.5 now includes validated partition intelligence, bounded filesystem recognition, boot/installer intelligence, unified identity/health intelligence, the user-facing Analyze/report path, deeper exFAT/FAT32/UDF evidence, bounded NTFS `$MFT`/`$MFTMirr` metadata validation, cross-source architecture reconciliation and a versioned independently verified clean Windows package-candidate path.

## Current milestone

**0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧**

Current milestone completion is approximately **90%**.

## Proven 0.5 slices

1. Cross-provider partition intelligence ✅
2. Bounded filesystem-recognition foundation ✅
3. Boot + installer intelligence foundation ✅
4. Unified identity + health intelligence foundation ✅
5. Windows Analyze + text/JSON reporting surface ✅
6. Deeper bounded filesystem evidence for exFAT/FAT32/UDF ✅
7. NTFS metadata + architecture-reconciliation hardening ✅
8. Clean Windows x64 package-candidate + SHA-256 path ✅
9. Central release version + independent package verification gate ✅

### Cross-provider partition intelligence proven scope

- provider-agnostic analysis through `ProviderRegistry` + `PartitionTable`
- duplicate index / zero length / LBA overflow / byte geometry / bounds / overlap findings
- stable findings and explicit severities
- no filesystem-health claims, writes, repair or mount side effects

### Filesystem recognition proven scope

- read-only recognition against bounded physical image regions
- partition scanning only after provider-reported physical ranges pass partition-intelligence validation
- FAT12/FAT16/FAT32, exFAT, supported NTFS boot metadata and ext2/ext3/ext4 recognition
- ISO9660/Joliet descriptor/label metadata and UDF VRS recognition
- blank images produce no guessed filesystem
- invalid partition layouts stop before filesystem probing
- no guest-sector translation inside sparse/compressed virtual disks

### Boot + installer intelligence proven scope

- bounded El Torito boot-record/catalog validation
- BIOS and UEFI bootability only from real boot catalog platform evidence
- bounded provider-backed Direct Browse traversal
- Windows and Linux installer/live-media evidence
- architecture hints from standard EFI fallback filenames and bounded installer evidence
- no execution, extraction, repair or writes

### Unified image intelligence proven scope

- `ImageIntelligenceService` composes only provider/intelligence paths backed by truthful capabilities
- partition names, filesystem labels/identifiers, WIM/ESD container GUID and FFU PlatformID aggregation
- partition structural findings plus selected exFAT/ext/NTFS/FAT32 health findings
- metadata-only sparse/compressed/container providers remain excluded from guest filesystem probing

### Windows analysis/report surface proven scope

- bounded **Analyze** action in the WinUI image result card
- shared `ImageReportService` text/JSON reporting over provider resolution and image intelligence
- Save JSON without enabling unsupported mutation paths
- unknown-input truthfulness, serialization and cancellation smoke tests
- `by Swir` + GitHub navigation footer
- PR #32 / run #260 passed complete Windows CI and Release x64 artifact publication

### Deeper filesystem evidence proven scope

- `FileSystemDepthService` reads only inside already-recognized physical filesystem regions
- exFAT main/backup boot-region checksums and redundant-copy comparison
- FAT32 FSInfo range/signature/free-count/next-free validation
- UDF primary anchor validation at logical block 256
- UDF descriptor tag checksum/location/CRC validation
- bounded UDF main descriptor-sequence inspection capped at 16 MiB
- validated UDF primary/logical volume d-strings exposed as identity evidence through the analysis/report path
- PR #33 / implementation run #262 passed the depth gate plus the full existing regression/build/artifact path

### NTFS + architecture hardening proven scope

- `NtfsMetadataDepthService` is restricted to NTFS regions already recognized as physically mappable
- `$MFT` and `$MFTMirr` cluster/range checks
- signed FILE-record size decoding with strict 512 B–1 MiB bounds
- Update Sequence Array geometry, USN sector-trailer checks and fixup restoration
- normalized first-record `$MFT` / `$MFTMirr` comparison with explicit warning/error findings
- no NTFS repair, attribute following, directory traversal or general filesystem driver claim
- architecture reconciliation preserves independent boot-path and installer-path evidence
- direct one-to-one architecture disagreement becomes an explicit warning; multi-architecture evidence is preserved
- generated healthy/corrupt/out-of-range NTFS fixtures, architecture cases and cancellation coverage
- PR #34 / implementation run #266 passed the new hardening gate and complete Windows regression/build path

### Beta-package + release-metadata hardening proven scope

- clean `DragonDiskForge-win-x64.zip` candidate generated after the Windows Release build
- staging rejects PDB and test-only files and requires exactly one application EXE
- canonical `DragonDiskForge.ico` is guaranteed in the clean package root
- package manifest records repository version, executable ProductVersion/FileVersion, x64 architecture, entry point, icon and entry-point SHA-256
- SHA-256 sidecar generated by CI
- product version metadata centralized in `Directory.Build.props`
- `verify-package.ps1` independently reopens and verifies the candidate before artifact upload
- run #270 passed the complete test/native/build path, clean-package build, package verification and both artifact uploads
- the run #270 clean artifact was independently downloaded: sidecar SHA-256 matched the nested ZIP; manifest version `0.5.0-alpha.1`; executable hash matched; one app EXE and zero PDB files
- final beta suffix promotion and clean-machine/manual QA remain release gates

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
- PR #28 / run #232 — docs-synchronized partition intelligence + full regression/build/artifact
- PR #29 / run #235 — docs-synchronized filesystem recognition + full regression/build/artifact
- PR #30 / run #242 — docs-synchronized boot/installer intelligence + full regression/build/artifact
- PR #31 / run #245 — unified identity/health implementation + full regression/build/artifact before docs synchronization
- PR #32 / run #260 — user-facing Analyze/report surface + complete Windows regression/build/artifact
- PR #33 / run #262 — deeper filesystem evidence implementation + complete Windows regression/build/artifact before docs synchronization
- PR #34 / run #266 — NTFS/architecture hardening + clean package candidate + SHA-256 + complete Windows regression/build path before docs synchronization
- PR #35 / run #270 — centralized version metadata + strict clean-package verification + complete Windows regression/build/package/artifact path before docs synchronization

## Next engineering focus

**Independently bounded UDF traversal where justified, truthful guest-sector reader foundations for sparse/compressed virtual disks, and then final 0.5 beta-scope hardening/manual QA.**

The planned first public beta remains `0.5.0-beta.1`. It is **not ready yet**: the verified version/package pipeline still carries the development suffix `0.5.0-alpha.1`, 0.5 has unfinished UDF/guest-sector engineering scope, and clean-machine/UAC/cross-process drag-out/manual regression gates in `docs/BETA-RELEASE.md` remain open.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers and intelligence services validate metadata offsets/ranges against the physical image and reject contradictory or unknown states instead of guessing.

Filesystem recognition does not imply filesystem traversal, repair or write support. Deeper filesystem checks stay within recognized physical regions. NTFS depth checks do not follow attributes or mutate metadata. Conflicting architecture evidence is preserved rather than collapsed to a guessed winner. Health findings cover only explicitly implemented metadata checks; absence of a finding is not a whole-filesystem health guarantee.

Interactive UAC and the real cross-process Explorer drag gesture remain manual QA cases in `docs/MANUAL-VALIDATION.md`.
