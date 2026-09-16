# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

**0.4 Extended Image Providers — COMPLETE ✅**

## Current development version

**0.5.0-alpha.1**

## Overall project progress

**45% toward 1.0** — milestones 0.1 through 0.4 are complete and the first validated 0.5 intelligence slice is now in place.

## Current milestone

**0.5 Partitions + File Systems + Image Intelligence — IN PROGRESS 🚧**

Current milestone completion is approximately **11%**.

## Proven 0.5 slices

1. Cross-provider partition intelligence ✅

### Cross-provider partition intelligence proven scope

- provider-agnostic `PartitionIntelligenceService`
- resolves through `ProviderRegistry` using the truthful `PartitionTable` capability
- stable structural findings with explicit severity
- duplicate partition indexes detected
- zero-length partitions detected
- LBA arithmetic overflow detected
- byte offset and byte size checked against provider-reported LBA geometry
- partition ranges bounded against the physical image
- overlapping partition byte ranges detected
- bootable partition count and partition-table metadata preserved
- capability-driven fake providers prove the service is not tied to one image format
- no filesystem-health claims, writes, repairs or mounts

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
- PR #26 / run #226 — final FFU head + full regression/build/artifact
- PR #27 / run #228 — provider-contract hardening + complete provider/native/build/artifact regression

### 0.5
- PR #28 / run #231 — partition-intelligence code head + dedicated tests + full provider/Explorer/native Windows/Release x64/artifact regression before documentation synchronization

## Next engineering focus

**Bounded filesystem recognition and metadata.** The next slice should build on the provider/capability layer and avoid mount-only assumptions. Planned filesystem scope includes ISO9660/UDF, FAT/FAT32/exFAT, NTFS metadata where supported and ext-family recognition, followed by boot/install intelligence and corruption warnings.

The planned first public beta remains `0.5.0-beta.1` and is not considered ready until the agreed 0.5 scope and beta gates are proven.

## Current safety state

Inspection remains read-only-first. Unsupported capabilities stay disabled. Parsers and intelligence services validate metadata offsets/ranges against the physical image and reject contradictory or unknown states instead of guessing.

Partition intelligence reports structural metadata findings only; it does not claim filesystem health or modify partition tables.

Interactive UAC and the real cross-process Explorer drag gesture remain manual QA cases in `docs/MANUAL-VALIDATION.md`.
