# Filesystem Recognition Contract

Dragon DiskForge 0.5 introduces bounded **filesystem recognition** as an intelligence layer. Recognition is intentionally narrower than filesystem browsing, extraction, repair or full validation.

## What recognition means

A positive `FileSystemDetectionInfo` means Core found a bounded set of metadata that is internally consistent enough to identify a filesystem family/variant in the byte region being inspected.

Current evidence sources:

| Family | Proven evidence |
| --- | --- |
| FAT12 / FAT16 / FAT32 | BPB geometry, FAT metadata geometry, cluster-count classification, boot signature |
| exFAT | `EXFAT   ` OEM field, boot signature, sector/cluster shifts, FAT/heap/root geometry |
| NTFS | `NTFS    ` OEM field, boot signature, bounded BPB geometry, MFT cluster range |
| ext2 / ext3 / ext4 | ext superblock magic, block geometry, journal/ext4 feature flags |
| ISO9660 | `CD001` primary volume descriptor + bounded volume geometry |
| Joliet | supported Joliet supplementary descriptor escape sequence + UCS-2 label |
| UDF | ordered Volume Recognition Sequence: `BEA01` → `NSR02`/`NSR03` → `TEA01` |

Returned metadata may include partition index, physical byte offset/region length, variant, label, serial/UUID, logical block size, allocation unit and a concise evidence description.

## Region-selection rule

Recognition never scans arbitrary offsets just because a format is plausible.

### Partition-capable images

When the resolved provider exposes `PartitionTable`:

1. Core reads the provider's partition table.
2. `PartitionIntelligenceService` validates its reported physical byte geometry against the image.
3. Any structural error stops filesystem recognition for that image.
4. Only the validated physical partition ranges are scanned.

### Direct physical-image formats

For a recognized provider without a partition table, the bounded physical file may be scanned as one region when its provider semantics map directly to physical image bytes. This supports examples such as raw floppy and ISO images.

## Virtual/sparse container boundary

A physical offset inside VMDK, QCOW/QCOW2, DMG or a similar container is generally **not** the same thing as a guest-sector offset.

Therefore the recognition foundation does not claim to inspect guest filesystems inside those containers. A future guest-sector reader must implement the real mapping/decompression/sparse semantics first. Only then may filesystem intelligence consume those translated bytes.

This rule prevents false positives caused by treating container metadata or compressed payload bytes as a guest filesystem.

## Negative results

“No filesystem detected” is a valid result. The service does not fall back to partition type names, filename extensions or guesses.

Examples:

- an MBR type name containing “NTFS/exFAT” is not filesystem proof
- `.iso` extension alone is not ISO9660 proof
- an ext-like label is not an ext superblock
- a blank recognized image remains an empty detection set

## Safety properties

Filesystem recognition is read-only and has no side effects. It does not:

- mount a filesystem
- write or repair metadata
- alter partition tables
- traverse arbitrary filesystem trees
- extract files
- claim filesystem health from a signature alone
- bypass cancellation
- follow unsupported virtual guest-sector mappings

## Test strategy

`DragonDiskForge.FileSystemRecognition.SmokeTests` generates disposable sparse fixtures for every currently supported family and verifies positive evidence, labels/identifiers/geometry where available, partition-scoped recognition, negative/blank cases, bad-layout refusal and cancellation.

Large real disk images are not committed to the repository.

## Future extension rule

New filesystem evidence must be added with:

1. bounded parsing rules,
2. explicit false-positive defenses,
3. generated or disposable fixtures,
4. cancellation coverage where I/O is involved,
5. documentation of what is and is not proven,
6. a green full Windows regression/build/artifact gate before roadmap progress changes.
