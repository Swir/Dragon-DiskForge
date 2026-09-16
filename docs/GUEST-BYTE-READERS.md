# Guest-byte readers

Dragon DiskForge separates **container metadata parsing** from **guest-visible byte translation**. A virtual-disk provider may recognize and validate a container without claiming that Core can safely read its guest sectors.

## Contract

`IGuestByteReader` exposes only:

- the bounded guest-visible length
- exact read-only random access by guest byte offset
- cancellation
- asynchronous disposal

A reader must fail closed when the requested mapping cannot be translated truthfully. The existence of a reader does **not** automatically grant `DirectBrowse`, Mount, extraction, filesystem traversal or mutation capabilities.

## QCOW2 first slice

`Qcow2GuestByteReader` currently supports a deliberately narrow QCOW2 v2/v3 subset:

- active L1/L2 table translation
- standard uncompressed allocated clusters
- explicit v3 zero-cluster descriptors
- unallocated clusters only when no backing file is configured, in which case guest bytes are zero
- reads that cross guest-cluster boundaries
- strict virtual-size and physical-file bounds
- reserved-bit and cluster-alignment validation before following table entries

The reader refuses rather than approximates:

- QCOW v1 guest mapping
- backing-file chains
- encryption
- dirty active QCOW2 metadata
- external data files
- non-default compression-type feature state
- extended L2 entries
- compressed cluster descriptors

PR #37 implementation run #277 passed the QCOW2 guest-reader gate and the complete existing Windows regression, Release x64 build, clean-package verification and artifact path.

## VMDK hosted sparse first slice

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
- grain-directory and grain-table confinement to declared metadata overhead
- allocated grain data must remain outside metadata overhead

The reader refuses rather than approximates:

- split sparse create types
- parent/backing chains
- unclean sparse metadata
- compressed grains
- stream-optimized metadata markers
- zeroed-grain-entry overloading
- unknown sparse-header flag semantics

PR #38 implementation head passed the dedicated VMDK reader gate together with the existing provider/intelligence/native regression before documentation synchronization.

## Validation

Generated QCOW2 and VMDK smoke fixtures cover allocated guest data, zero/sparse mappings where semantically proven, cross-boundary reads, guest OOB access, physical OOB mappings, malformed table/directory entries, unsupported state refusal and cancellation. VMDK fixtures additionally verify active redundant/primary directory selection and metadata/data-region separation.

## Next steps

1. Introduce a common guest-byte source resolver that selects only proven readers.
2. Allow partition/filesystem intelligence to operate over that abstraction without confusing guest offsets with physical container offsets.
3. Add bounded integration fixtures proving the same partition/filesystem evidence through RAW, QCOW2 and VMDK byte sources.
4. Keep Direct Browse and extraction disabled until filesystem traversal itself is separately implemented and tested for those guest mappings.

## References

QCOW2 mapping semantics are implemented against the current QEMU QCOW2 interoperability specification: <https://www.qemu.org/docs/master/interop/qcow2.html>.

Hosted sparse VMDK mapping follows the documented sparse-header/grain-directory/grain-table model and is intentionally limited to the tested uncompressed, no-parent subset.
