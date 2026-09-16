# RAW Image Pipelines

Dragon DiskForge 0.6 introduces its first real file-producing image pipelines through `RawImagePipelineService`. Every destination is published through the shared `SafeOutputService` transaction boundary; the source side stays read-only.

## Proven operations

### Blank RAW creation

`CreateBlankAsync` creates a RAW image with an explicit logical byte length.

- output is staged in the destination directory and committed only after the operation completes
- zero-length images are supported
- lengths above the current `FileStream` domain (`long.MaxValue`) fail before mutation
- overwrite behavior is explicit through `FailIfExists` or `ReplaceExisting`
- the resulting file reads as zero-filled logical storage; physical allocation remains a filesystem concern

### Guest-byte export to RAW

`ExportGuestToRawAsync` materializes any proven `IGuestByteReader` source into a flat RAW file.

- reads are sequential and bounded to the guest-visible source length
- a fixed 1 MiB transfer buffer avoids whole-image allocation
- the committed RAW length must exactly match the captured guest-visible source length
- cancellation is checked before reads, after source reads and by the output transaction before final commit
- reader failures roll back the staged output instead of intentionally publishing a partial destination
- progress stays below `1.0` while data is still staged; `1.0` is reported only after the output transaction commits

### QCOW2 to RAW

`ConvertQcow2ToRawAsync` opens the existing `Qcow2GuestByteReader` and materializes its proven guest-visible bytes to RAW.

The converter therefore inherits the reader's fail-closed scope. It supports the already-proven standard uncompressed QCOW2 v2/v3 active L1/L2 mappings and does not broaden support for backing chains, encryption, dirty images, external data files, compressed-cluster descriptors or extended-L2 semantics.

### Hosted-sparse VMDK to RAW

`ConvertVmdkSparseToRawAsync` opens the existing `VmdkSparseGuestByteReader` and materializes its proven guest-visible bytes to RAW.

The converter therefore remains limited to the already-proven clean single-extent hosted-sparse v1 `monolithicSparse` subset with no parent chain and no compressed/stream-optimized semantics. Unsupported VMDK states still fail closed before output is published.

## Safety and rollback

The pipeline does not write to the source image or guest address space. It only creates/replaces the requested destination file through `SafeOutputService`.

Generated validation covers:

- exact blank RAW logical size and zero semantics
- multi-buffer guest export with byte-for-byte boundary checks
- explicit replacement and `FailIfExists` behavior
- cancellation after a real guest read with no partial destination publication
- guest-reader failure while replacing an existing destination, proving preservation of the old file
- transaction temporary-file cleanup
- lengths outside the supported output-file domain
- synthetic QCOW2-to-RAW materialization including allocated, explicit-zero and unallocated guest clusters
- synthetic hosted-sparse VMDK-to-RAW materialization including allocated and unallocated/no-parent grains
- identical source/destination conversion rejection

## What remains disabled

The existence of these Core pipelines does **not** enable Create or Convert in WinUI. A user-visible capability still requires an explicit product surface, format-specific validation, clear overwrite UX and the same full regression/release gates.

The 0.6 split/join and broader sparse/compression work remains separate. Materializing sparse guest mappings into a flat RAW output does not imply support for writing sparse container metadata or decoding compressed container payloads.
