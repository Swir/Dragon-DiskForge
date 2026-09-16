# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

**0.4 Extended Image Providers — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

## Overall project progress

**52% toward 1.0** — milestones 0.1 through 0.4 are complete. Milestone 0.5 now has validated cross-provider partition intelligence, bounded filesystem recognition, bounded boot/installer intelligence, and a unified identity/health intelligence foundation.

## Current milestone

**0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧**

Current milestone completion is approximately **70%**.

## Proven 0.5 slices

1. Cross-provider partition intelligence ✅
2. Bounded filesystem-recognition foundation ✅
3. Boot + installer intelligence foundation ✅
4. Unified identity + health intelligence foundation ✅

### Partition intelligence proven scope

- provider-agnostic analysis through `ProviderRegistry` + `PartitionTable`
- duplicate index / zero length / LBA overflow / byte geometry / bounds / overlap findings
- stable findings and explicit severities
- capability-driven test providers
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

- bounded El Torito boot-record and boot-catalog discovery in direct-browse physical ISO images
- validation-entry checksum/key validation
- default and section-entry parsing with bounded catalog windows
- BIOS and UEFI bootability only from real boot catalog platform evidence
- boot-image load ranges bounded against the physical image
- Direct Browse tree traversal limited by directory count, entry count and virtual depth
- only real non-reparse files become installer evidence
- traversal-style virtual paths are rejected
- Windows install media requires setup + boot WIM + install payload evidence
- Linux evidence covers casper, Debian-style and Anaconda-style media layouts
- architecture hints from standard EFI fallback filenames and bounded installer directory evidence
- filesystem markers never fabricate BIOS/UEFI bootability
- no execution, extraction, mount, repair or writes

### Unified image intelligence proven scope

- `ImageIntelligenceService` composes only provider/intelligence paths backed by truthful capabilities
- partition names plus filesystem labels and identifiers retain evidence source and partition provenance
- WIM/ESD container GUID and FFU PlatformID aggregation
- architecture-hint aggregation from the bounded boot/installer layer
- partition structural findings mapped into a shared image-health model
- exFAT dirty and media-failure flags reported from bounded boot metadata
- ext clean/error state reported from the bounded superblock
- NTFS and FAT32 primary/backup boot-metadata consistency checks
- every filesystem health read remains inside the recognized filesystem region and physical image
- metadata-only sparse/compressed/container providers remain excluded from guest filesystem probing
- generated exFAT, NTFS, ext, WIM and FFU fixtures plus byte-mapping refusal and cancellation tests
- no repair, mount, extraction, guest-sector guessing or writes

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
- PR #28 / run #232 — docs-synchronized cross-provider partition intelligence + full regression/build/artifact
- PR #29 / run #235 — docs-synchronized bounded filesystem recognition + full regression/build/artifact
- PR #30 / run #242 — docs-synchronized boot/installer intelligence + full regression/build/artifact
- PR #31 / run #245 — unified identity/health code + dedicated tests + all existing provider/Explorer/native Windows/Release x64/artifact checks before documentation synchronization

## Next engineering focus

**Deeper bounded filesystem reader/health evidence for FAT/exFAT/NTFS/UDF**, followed by independently tested guest-sector readers before filesystem intelligence is extended into sparse/compressed virtual disks.

The planned first public beta remains `0.5.0-beta.1` and is not ready until the agreed 0.5 scope and beta gates are proven.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers and intelligence services validate metadata offsets/ranges against the physical image and reject contradictory or unknown states instead of guessing.

Filesystem recognition does not imply filesystem traversal, repair or write support. Health findings cover only explicitly implemented metadata checks; absence of a finding is not a whole-filesystem health guarantee. Installer evidence does not imply code execution. El Torito metadata is the only current source for BIOS/UEFI bootability claims.

Interactive UAC and the real cross-process Explorer drag gesture remain manual QA cases in `docs/MANUAL-VALIDATION.md`.
