# Dragon DiskForge — Current Status

## Completed milestones

**0.1 Foundation + Dragon Visual Identity — COMPLETE ✅**

**0.2 Native Mount + Unmount — COMPLETE ✅**

**0.3 Dragon Explorer — COMPLETE ✅**

## Current version

**0.4.0-alpha.1**

## Overall project progress

**39% toward 1.0** — milestones 0.1, 0.2 and 0.3 are complete and proven. Milestone 0.4 now has its provider foundation plus seven additional image families implemented and validated.

## Current milestone

**0.4 Extended Image Providers — IN PROGRESS 🚧**

Current milestone completion is approximately **71%**.

## Proven 0.4 slices

### Provider foundation

- central Core `ProviderRegistry`
- explicit provider capability reporting
- deterministic priority and extension-first resolution
- provider/signature fallback
- probe and inspection failure isolation
- cancellation preserved as a hard stop
- provider diagnostics and duplicate-ID protection
- ISO9660/Joliet direct browsing resolved through the registry

### IMG / RAW partition provider ✅

- read-only `.img`, `.raw`, `.dd`
- MBR primary partitions and bounded EBR logical partitions
- GPT with 512/4096-byte logical-sector probing
- partition type/name metadata and strict image-boundary checks
- explicit `PartitionTable` capability
- dedicated Windows CI tests

### IMA / floppy provider ✅

- read-only `.ima`, `.flp`
- standard raw floppy geometry recognition
- optional FAT-style BPB parsing
- capacity and CHS validation
- explicit `MediaGeometry` capability
- dedicated Windows CI tests

### BIN / CUE track-layout provider ✅

- bounded BINARY CUE parsing
- AUDIO and MODE1/MODE2 supported sector layouts
- single-file and multi-file payload handling
- INDEX 00/01 range validation
- payload path containment and alignment checks
- explicit `TrackLayout` capability

### MDF / MDS CD track-layout provider ✅

- `MEDIA DESCRIPTOR` signature/version/medium/session/track validation
- explicit MDF byte offsets for mixed-sector layouts
- safe footer-based MDF resolution
- descriptor/payload bounds checks
- DVD-style handling deliberately rejected until separately proven

### NRG v1/v2 track-layout provider ✅

- `NERO` v1 and `NER5` v2 footer handling
- CUES/CUEX track positions
- DAOI/DAOX byte ranges
- cue/DAO consistency validation
- required empty `END!` terminator

### CCD / IMG / SUB track-layout provider ✅

- CloneCD `.ccd`, same-name `.img`, optional validated `.sub`
- MODE 0/1/2 and INDEX 0/1 handling
- 2352-byte IMG alignment
- 96-byte-per-sector SUB validation
- CCD-vs-RAW `.img` fallback preserved

### VMDK sparse metadata provider ✅

- read-only hosted sparse VMDK header version 1 inspection
- `IVirtualDiskMetadataProvider` + `VirtualDiskMetadata` capability
- validates `0x564D444B` magic and version 1
- parses virtual capacity, grain size, descriptor offset/size, grain-table entry count, redundant grain-directory offset, grain-directory offset and overhead
- validates all sector-based metadata offsets against the physical VMDK file
- validates unclean-shutdown, newline-detection and compression fields
- optional embedded descriptor limited to 1 MiB
- descriptor metadata: version, createType, CID, parentCID and extent count
- malformed/conflicting descriptor values rejected
- text-only VMDK descriptors and unproven sparse header versions intentionally not claimed
- no virtual-sector translation, Direct Browse, Mount or Convert in this slice
- dedicated VMDK smoke tests passed in PR #22 / run #200

## Proven validation checkpoints

- PR #16 / run #153 — RAW/IMG
- PR #17 / run #165 — IMA/floppy + full regression/build/artifact
- PR #18 / run #172 — BIN/CUE + full regression/build/artifact
- PR #19 / run #182 — MDF/MDS + full regression/build/artifact
- PR #20 / run #185 — NRG + full regression/build/artifact
- PR #21 / run #198 — final CCD/IMG/SUB head + full provider/native regression/build/artifact
- PR #22 / run #200 — VMDK sparse metadata + all previous providers, Explorer safety, ISO/native Windows integration, Release x64 build and artifact

## Next 0.4 provider

**QCOW / QCOW2** — begin with bounded container-header metadata inspection. Cluster translation, virtual-sector reads and filesystem browsing stay disabled until implemented and proven.

## Current safety state

Inspection paths remain read-only-first. Unsupported capabilities stay disabled. Metadata offsets and lengths are validated before reads; parsers reject contradictory structures rather than guessing.

VMDK currently exposes only proven sparse-v1 metadata and a bounded embedded descriptor. It does not expose grain-table translation, virtual disk sectors, filesystem browsing, Mount or Convert.

The interactive UAC prompt and the real cross-process Windows Explorer drag gesture remain manual desktop QA cases in `docs/MANUAL-VALIDATION.md`; CI does not fabricate those human-interaction results.
