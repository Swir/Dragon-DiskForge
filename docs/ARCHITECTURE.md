# Dragon DiskForge architecture

## Goals

Dragon DiskForge is a Windows-native disk-image workstation. The WinUI shell and Core engine are separated so inspection and intelligence logic can be reused by the desktop app, future CLI and automated workflows without embedding UI assumptions in parsers.

The architectural rule is simple: **capabilities must be truthful**. A provider or intelligence service may expose only operations backed by a real, bounded and testable byte path.

## Projects

### `DragonDiskForge.App`

WinUI 3 desktop front end. It owns presentation, navigation, drag/drop, user confirmations, image workspace state and Windows shell integration.

### `DragonDiskForge.Core`

UI-independent engine. It owns detection, provider contracts, image metadata, partition/filesystem intelligence, verification and provider-backed Explorer abstractions.

Native Windows storage lifecycle remains isolated from portable Core parsing.

## Provider model

`ProviderRegistry` is the central image-family resolver. `ProviderRegistration` snapshots a validated immutable descriptor containing provider ID, display name, normalized extensions, priority and inferred capabilities.

Current capability families include:

- Inspect
- Direct Browse / Search / Copy Out
- Partition Table
- Media Geometry
- Track Layout
- Virtual Disk Metadata
- Container Metadata

Resolution is extension-first but still permits signature/provider fallback. Equal-priority registrations are deterministic. Probe and inspection failures are isolated; cancellation is never swallowed as a parser failure.

A provider must never claim a capability it cannot perform safely.

## 0.5 intelligence layers

### Partition intelligence

`PartitionIntelligenceService` consumes the truthful `PartitionTable` capability rather than knowing RAW/IMG internals. It validates provider-reported layout structure against the physical image and emits stable findings for duplicate indexes, zero-length entries, arithmetic overflow, byte-geometry mismatches, physical bounds and overlaps.

It is an analysis layer, not a partition editor.

### Filesystem recognition

`FileSystemRecognitionService` builds on provider resolution and physical byte boundaries:

1. Resolve the image through `ProviderRegistry`.
2. If the provider exposes `PartitionTable`, read the partition table and pass it through `PartitionIntelligenceService`.
3. Refuse filesystem probing if the partition layout contains structural errors.
4. Scan only the validated physical partition byte ranges.
5. For recognized non-partition providers whose bytes map directly to the physical file, scan the whole bounded image region.
6. Return evidence-backed `FileSystemDetectionInfo` records; an absence of evidence remains an empty result, never a guess.

Current bounded recognition covers FAT12/FAT16/FAT32, exFAT, supported NTFS boot metadata, ext2/ext3/ext4, ISO9660/Joliet and UDF VRS.

### Important virtual-disk boundary

Physical file offsets are **not** guest-sector offsets for sparse/compressed/container formats. VMDK, QCOW/QCOW2, DMG and similar formats therefore keep their proven metadata-only capabilities until a real guest-sector translation layer exists. Filesystem recognition must not scan arbitrary container bytes and label them as a guest filesystem.

This is a deliberate safety/correctness boundary, not a missing fallback.

## Explorer architecture

Two proven read paths coexist:

- mounted-volume Explorer for Windows-native ISO/VHD/VHDX mounts
- provider-backed direct ISO9660/Joliet browsing without mounting

Both remain Copy-out oriented. Existing destination conflicts and traversal/reparse-point hazards are rejected rather than overwritten or followed blindly.

## Safety boundaries

Read-only inspection is the default. Operations that can modify images, physical disks or removable media stay separate from inspection/intelligence services and require dedicated validation before they may become user-visible.

Current intelligence services never:

- write or repair partition tables
- repair filesystems
- mount as a side effect
- translate unsupported virtual guest sectors
- claim filesystem health from a signature alone
- infer a capability because a filename extension looks plausible

## Current milestone mapping

- `0.1` — foundation + Dragon UI ✅
- `0.2` — native Mount + Unmount ✅
- `0.3` — Dragon Explorer ✅
- `0.4` — extended image providers + provider-contract hardening ✅
- `0.5` — partitions + filesystems + image intelligence 🚧
- `0.6` — create + convert + verify
- `0.7` — physical media tools
- `0.8` — Windows integration + power tools
- `0.9` — quality/security/beta hardening
- `1.0` — production release

`docs/ROADMAP.md` is the source of truth for feature status. `docs/STATUS.md` and `docs/MILESTONES.md` record the validated execution state.
