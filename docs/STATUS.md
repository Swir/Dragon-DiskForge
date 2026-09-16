# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

**0.4 Extended Image Providers — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

## Overall project progress

**47% toward 1.0** — milestones 0.1 through 0.4 are complete. Milestone 0.5 now has validated cross-provider partition intelligence plus a bounded filesystem-recognition foundation.

## Current milestone

**0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧**

Current milestone completion is approximately **30%**.

## Proven 0.5 slices

1. Cross-provider partition intelligence ✅
2. Bounded filesystem-recognition foundation ✅

### Partition intelligence proven scope

- provider-agnostic analysis through `ProviderRegistry` + `PartitionTable`
- duplicate index / zero length / LBA overflow / byte geometry / bounds / overlap findings
- stable findings and explicit severities
- capability-driven test providers
- no filesystem-health claims, writes, repair or mount side effects

### Filesystem recognition proven scope

- read-only recognition against bounded physical image regions
- provider-recognized whole-file analysis where physical-byte mapping is truthful
- partition scanning only after provider-reported physical ranges pass partition-intelligence validation
- FAT12/FAT16/FAT32 classification + label/serial/sector/cluster metadata
- exFAT serial + sector/cluster geometry
- NTFS boot metadata + serial + cluster geometry
- ext2/ext3/ext4 recognition + label/UUID/block size
- ISO9660 + Joliet descriptor/label metadata
- UDF VRS recognition through ordered `BEA01`, `NSR02|NSR03`, `TEA01`
- blank images produce no guessed filesystem
- invalid partition layouts stop before filesystem probing
- cancellation remains a hard stop
- no guest-sector translation inside sparse/compressed virtual disks

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
- PR #29 / run #234 — filesystem-recognition code head + dedicated generated-fixture tests + complete provider/Explorer/native Windows/Release x64/artifact regression before documentation synchronization

## Next engineering focus

**Bootability / BIOS / UEFI and installer intelligence**, grounded in existing provider, partition and direct-browse evidence. Richer UDF/FAT/NTFS reader depth remains in 0.5 and must not be confused with the bounded recognition already proven.

The planned first public beta remains `0.5.0-beta.1` and is not ready until the agreed 0.5 scope and beta gates are proven.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers and intelligence services validate metadata offsets/ranges against the physical image and reject contradictory or unknown states instead of guessing.

Filesystem recognition does not imply filesystem traversal, repair or write support, and does not claim access to guest sectors in sparse/compressed virtual-disk formats.

Interactive UAC and the real cross-process Explorer drag gesture remain manual QA cases in `docs/MANUAL-VALIDATION.md`.
