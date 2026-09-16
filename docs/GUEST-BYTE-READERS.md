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

These restrictions are intentional. Supporting one container generation does not imply support for every optional QCOW2 feature.

## Validation

Generated smoke fixtures cover:

- allocated guest data
- explicit zero clusters
- unallocated clusters with no backing file
- a read crossing an allocated/zero cluster boundary
- virtual out-of-range access
- physical out-of-range mappings
- reserved L1 bits
- unsupported backing/encryption/dirty/compressed states
- QCOW2 v2 misuse of the v3 zero flag
- cancellation

PR #37 implementation run #277 passed the guest-reader gate and the complete existing Windows regression, Release x64 build, clean-package verification and artifact path.

## Next steps

1. Add a similarly bounded VMDK hosted sparse v1 reader for the safe uncompressed subset.
2. Introduce a common guest-byte source resolver that selects only proven readers.
3. Allow partition/filesystem intelligence to operate over that abstraction without confusing guest offsets with physical container offsets.
4. Keep Direct Browse and extraction disabled until filesystem traversal itself is separately implemented and tested for those guest mappings.

## Reference

QCOW2 mapping semantics are implemented against the current QEMU QCOW2 interoperability specification: <https://www.qemu.org/docs/master/interop/qcow2.html>.
