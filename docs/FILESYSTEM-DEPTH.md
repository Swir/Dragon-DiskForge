# Dragon DiskForge — Filesystem Depth Evidence

This document defines the bounded read-only filesystem checks implemented by the milestone 0.5 depth layer: `FileSystemDepthService` for exFAT/FAT32/UDF evidence, `NtfsMetadataDepthService` for deeper supported NTFS metadata, and `UdfTraversalService` for a deliberately narrow non-recursive UDF root-directory path.

## Contract

The depth layer is **not** a general filesystem driver. It may run only after `FileSystemRecognitionService` has already produced a structurally recognized filesystem region backed by a truthful physical byte mapping.

Every depth read must remain inside both:

1. the recognized filesystem region; and
2. the physical image file.

The generic depth services do not mount, repair or modify images. `UdfTraversalService` adds only the specifically proven bounded root-directory read described below; it does not enable general Direct Browse, extraction or recursive tree walking.

## exFAT depth

For recognized exFAT volumes the service can inspect the bounded 24-sector main + backup boot-region layout.

Implemented evidence:

- main 11-sector boot-region checksum validation
- backup 11-sector boot-region checksum validation
- checksum-sector validation across the full checksum sector
- comparison of main/backup boot copies while excluding the mutable `VolumeFlags` and `PercentInUse` bytes from equality requirements
- validation that `PercentInUse` is `0xFF` (unknown) or within 0–100
- explicit truncation/error findings when both 12-sector boot regions do not fit the recognized filesystem region

This complements the existing exFAT `VolumeDirty` and `MediaFailure` evidence in `ImageIntelligenceService`.

## FAT32 depth

For recognized FAT32 volumes the service validates the BPB-declared FSInfo sector before reading it.

Implemented evidence:

- FSInfo sector must be inside the FAT32 reserved-sector area
- FSInfo read must fit the recognized filesystem region
- lead signature `0x41615252`
- structure signature `0x61417272`
- trail signature `0xAA550000`
- `Free_Count` may be unknown (`0xFFFFFFFF`) or must not exceed the bounded data-cluster count
- `Nxt_Free` may be unknown (`0xFFFFFFFF`) or must identify a valid data-cluster hint

This complements the existing FAT32 primary/backup boot-sector consistency check.

## NTFS metadata depth

For a recognized NTFS volume, `NtfsMetadataDepthService` validates selected boot-derived locations and the first mirrored FILE record without claiming general NTFS traversal.

Implemented evidence:

- sector/cluster geometry is revalidated before deeper reads
- boot-declared volume size must fit the already-recognized NTFS region
- `$MFT` and `$MFTMirr` Logical Cluster Numbers must lie inside the bounded volume
- signed clusters-per-FILE-record encoding is decoded conservatively; accepted FILE record sizes are power-of-two values from 512 bytes through 1 MiB
- clusters-per-index-buffer encoding receives the same bounded size sanity check
- first `$MFT` and `$MFTMirr` FILE records must fit the recognized region
- `FILE` signature validation
- Update Sequence Array offset/count/range validation
- USA count must match the number of sectors in the bounded FILE record
- every sector trailer must match the Update Sequence Number before fixup restoration
- record header `first attribute`, `bytes in use` and `bytes allocated` fields must remain internally bounded
- after validated fixup restoration, the first `$MFT` and `$MFTMirr` records are compared conservatively; divergence is reported as a warning

The service never repairs an Update Sequence Array, writes replacement words back to disk, follows NTFS attributes or walks the directory tree. The restored record exists only in memory for validation/comparison.

## UDF depth

Basic UDF recognition begins with a bounded Volume Recognition Sequence (`BEA01`, `NSR02`/`NSR03`, `TEA01`). The depth layer adds independently validated metadata before accepting deeper UDF evidence.

Implemented metadata evidence:

- primary Anchor Volume Descriptor Pointer at logical block 256
- descriptor-tag checksum validation
- descriptor tag-location validation
- descriptor CRC-16 validation where a descriptor CRC is present
- main Volume Descriptor Sequence extent bounds and arithmetic-overflow checks
- maximum inspected descriptor-sequence size of **16 MiB**
- validated Primary Volume Descriptor volume identifier
- validated Logical Volume Descriptor logical-volume identifier
- logical block-size sanity validation
- expected `*OSTA UDF Compliant` domain identifier check
- explicit findings when validated Primary or Logical Volume Descriptors are missing from the bounded inspected sequence

OSTA compressed Unicode d-strings with compression IDs 8 and 16 are decoded only after the containing descriptor tag has passed its bounded validation.

### Bounded UDF root-directory traversal

`UdfTraversalService` adds a deliberately narrow traversal path without promoting UDF to a general browser.

Implemented scope:

- runs only on an already-recognized UDF region that maps directly to physical image bytes
- validates the primary anchor and bounded main descriptor sequence before following traversal metadata
- accepts only validated **Type 1** partition maps and translates them through a matching validated Partition Descriptor
- refuses Type 2 virtual, sparable and metadata partition maps rather than guessing a guest-sector mapping
- validates the File Set Descriptor before accepting its File Set Identifier or Root Directory ICB
- validates the root File Entry tag/checksum/CRC/location, directory file type, supported ICB strategy and allocation-descriptor bounds
- first slice accepts exactly one **recorded short allocation descriptor** for the root directory
- root-directory data is capped at **8 MiB**
- root traversal is capped at **4096 File Identifier Descriptors**
- every accepted File Identifier Descriptor must fit the bounded root extent and pass tag checksum, tag location and descriptor CRC validation
- OSTA compressed Unicode file identifiers with compression IDs 8 and 16 are decoded only after descriptor validation
- deleted and parent entries are not exposed as ordinary root names
- traversal is **non-recursive**; child directory ICBs are not followed
- no extraction, repair, writes or Direct Browse capability are enabled

The validated UDF File Set Identifier is merged into user-facing analysis/report identity evidence. Root entry names are currently testable service output; the existing UI does not advertise generalized UDF browsing.

## Evidence flow

`ImageReportService` runs the generic depth layer only when the existing image-intelligence result contains recognized filesystems. For recognized UDF it then runs `UdfTraversalService` and merges validated identity/health evidence. For recognized NTFS it runs the NTFS-specific depth service. Identity and health evidence are merged with deterministic de-duplication and ordering.

This lets the user-facing Analyze / JSON report expose deeper exFAT/FAT32/UDF and NTFS evidence while keeping unsupported capabilities disabled.

## Safety limitations

- No repair or write operations.
- UDF traversal is limited to the validated root-directory slice described above; it is not a generalized file-tree reader.
- UDF Type 2 virtual/sparable/metadata partition maps are not followed.
- No NTFS attribute/MFT traversal is claimed beyond the explicitly validated first mirrored FILE record.
- No guest-sector translation for VMDK/QCOW/DMG or other sparse/compressed mappings.
- A missing finding is **not** proof that a filesystem is fully healthy.
- The checks intentionally validate only metadata structures explicitly implemented and tested.

## Validation

Generated fixtures cover:

- valid exFAT main/backup boot regions and checksums
- corrupted exFAT main boot metadata
- FAT32 FSInfo values outside the bounded cluster geometry
- valid UDF VRS + anchor + PVD + LVD + terminating descriptor
- corrupted UDF primary-anchor tag checksum
- valid Type 1 UDF partition map + File Set Descriptor + root File Entry + root File Identifier Descriptor
- corrupted UDF root File Identifier Descriptor checksum
- out-of-range UDF root ICB
- unsupported Type 2 UDF partition map refusal
- valid `$MFT` / `$MFTMirr` first FILE records
- corrupted NTFS Update Sequence Array sector trailer
- logically valid but divergent `$MFT` / `$MFTMirr` first records
- out-of-range `$MFTMirr` LCN
- cancellation as a hard stop

PR #33 implementation head was validated by Windows CI run #262 for exFAT/FAT32/UDF metadata depth. PR #34 implementation run #266 added the NTFS/architecture hardening gate. PR #36 implementation run #274 added bounded UDF root traversal and passed the complete provider, Explorer, native Windows, Release x64, clean-package verification and artifact path before documentation synchronization.
