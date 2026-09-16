# Guest-byte readers

Dragon DiskForge separates **container metadata parsing** from **guest-visible byte translation**. A virtual-disk provider may recognize and validate a container without claiming that Core can safely read its guest sectors.

## Contract

`IGuestByteReader` exposes only:

- the bounded guest-visible length
- exact read-only random access by guest byte offset
- cancellation
- asynchronous disposal

A reader must fail closed when the requested mapping cannot be translated truthfully. The existence of a reader does **not** automatically grant `DirectBrowse`, Mount, extraction, filesystem traversal or mutation capabilities.

## QCOW2 proven slice

`Qcow2GuestByteReader` currently supports a deliberately narrow QCOW2 v2/v3 subset:

- active L1/L2 table translation
- standard uncompressed allocated clusters
- explicit v3 zero-cluster descriptors
- unallocated clusters only when no backing file is configured, in which case guest bytes are zero
- reads that cross guest-cluster boundaries
- strict virtual-size and physical-file bounds
- reserved-bit and cluster-alignment validation before following table entries

The reader refuses QCOW v1 guest mapping, backing-file chains, encryption, dirty active metadata, external data files, non-default compression state, extended L2 entries and compressed cluster descriptors.

PR #37 implementation run #277 passed the QCOW2 guest-reader gate and the complete Windows regression/package path.

## VMDK hosted sparse proven slice

`VmdkSparseGuestByteReader` supports a deliberately narrow hosted sparse v1 subset:

- one embedded `monolithicSparse` extent
- clean metadata only
- no parent chain (`parentCID=ffffffff`)
- no compression or stream-optimized marker semantics
- active redundant-vs-primary grain-directory selection from the sparse header
- grain-directory and grain-table translation for standard uncompressed allocated grains
- unallocated grains only when no parent chain exists, in which case guest bytes are zero
- reads that cross guest-grain boundaries
- strict guest-capacity and physical-file bounds
- grain-directory/grain-table confinement to declared metadata overhead
- allocated grain data must remain outside metadata overhead

The reader refuses split sparse create types, parent/backing chains, unclean metadata, compressed grains, stream-optimized markers, zeroed-grain-entry overloading and unknown sparse-header flag semantics.

PR #38 / run #284 passed the dedicated VMDK reader gate plus the complete Windows regression/build/package path.

## Common guest partition/filesystem intelligence

The proven readers now feed a bounded analysis layer without changing their capability claims:

- `GuestPartitionTableReader` reads guest-visible MBR, bounded EBR chains and GPT metadata
- partition layouts reuse `PartitionIntelligenceService` geometry, overlap and range validation against the virtual guest size
- `GuestFileSystemRecognitionService` recognizes FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS evidence in bounded guest regions
- `GuestFileSystemDetectionInfo` uses `GuestOffsetBytes`; physical `FileSystemDetectionInfo.PhysicalOffsetBytes` is never reused for virtual offsets
- `GuestImageIntelligenceService` selects only the explicitly proven QCOW2 and hosted-sparse VMDK reader implementations
- `ImageReportService` exposes a separate `GuestAnalysis` JSON object and explicit guest-address-space sections in text reports
- unsupported optional container semantics continue to fail at reader open/translation rather than being approximated

PR #39 implementation run #286 passed generated QCOW2/VMDK MBR + FAT12 fixtures, out-of-range guest partition refusal, cancellation and the full Windows regression/package path.

## Validation

Generated QCOW2 and VMDK smoke fixtures cover allocated guest data, zero/sparse mappings where semantically proven, cross-boundary reads, guest OOB access, physical OOB mappings, malformed table/directory entries, unsupported state refusal and cancellation. Guest-intelligence fixtures additionally prove that partition/filesystem evidence is derived from the virtual address space rather than physical container offsets.

## Capability boundary

Guest analysis is still **analysis-only**. It does not enable Direct Browse, extraction, Mount, repair or writes for QCOW2/VMDK. Filesystem traversal and any future compressed/backing variants require separate implementation, tests and truthful capability review.

## References

QCOW2 mapping semantics are implemented against the current QEMU QCOW2 interoperability specification: <https://www.qemu.org/docs/master/interop/qcow2.html>.

Hosted sparse VMDK mapping follows the documented sparse-header/grain-directory/grain-table model and is intentionally limited to the tested uncompressed, no-parent subset.
