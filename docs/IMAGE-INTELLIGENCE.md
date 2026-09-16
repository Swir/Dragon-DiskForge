# Dragon DiskForge — Unified Image Intelligence

`ImageIntelligenceService` is the 0.5 aggregation layer that combines already-proven read-only evidence without inventing capabilities that a provider does not expose.

## Evidence sources

The service resolves the image once through `ProviderRegistry` and then activates only the intelligence paths backed by the resolved descriptor:

- `PartitionTable` → `PartitionIntelligenceService`
- truthful physical filesystem byte mapping → `FileSystemRecognitionService`
- `DirectBrowse` → `BootInstallerIntelligenceService`
- `ContainerMetadata` + `IWimMetadataProvider` → WIM/ESD container GUID
- `ContainerMetadata` + `IFfuMetadataProvider` → FFU PlatformID

Metadata-only sparse/compressed/container providers do not gain filesystem probing merely because their container bytes happen to resemble a filesystem signature.

## Cross-source identity aggregation

Current identity evidence includes:

- partition names from validated provider partition tables
- filesystem labels from bounded recognized filesystems
- filesystem identifiers such as volume serials or UUIDs when the recognizer has proven their structure
- WIM/ESD container GUIDs
- FFU PlatformID metadata
- architecture hints already proven by the boot/installer intelligence layer

Evidence is normalized and de-duplicated while retaining its source and partition index when applicable. Partition type IDs are not treated as unique volume identifiers.

## Health and corruption evidence

Health findings are evidence-backed and read-only. They currently include:

- all structural partition findings, mapped into the shared image-health model
- exFAT `VolumeDirty` as a warning
- exFAT `MediaFailure` as an error
- ext superblock error state as an error
- ext non-clean state as a warning
- NTFS primary/backup boot-metadata mismatch as a warning
- NTFS backup boot-sector range overflow/out-of-range as an error
- FAT32 primary/backup boot-metadata mismatch as a warning
- FAT32 backup boot-sector out-of-range as an error

A clean result means that none of these implemented checks produced a finding. It is **not** a promise that the entire filesystem is healthy.

## Bounded-read rules

Filesystem health reads are allowed only inside the physical region already accepted by filesystem recognition. Every read is bounded both against that filesystem region and the physical file.

The service does not:

- repair a filesystem or partition table
- follow unproven guest-sector mappings
- mount an image as a side effect
- execute or extract installer content
- convert health evidence into destructive action
- treat a missing check as proof of health

## Current mapping boundary

Whole-file/partition filesystem probing is currently enabled only when the provider exposes a capability that proves a direct physical-byte path for the current intelligence layer: `PartitionTable`, `MediaGeometry`, or `DirectBrowse`.

VMDK, QCOW/QCOW2, DMG and similar metadata-only virtual/container formats remain outside guest filesystem intelligence until a dedicated guest-sector reader exists and is independently tested.

## Validation

`tests/DragonDiskForge.ImageIntelligence.SmokeTests` uses generated fixtures to prove:

- partition-name + filesystem-ID aggregation
- exFAT dirty/media-failure health findings
- NTFS backup boot-metadata mismatch detection
- ext error-state detection and label aggregation
- WIM container GUID aggregation
- FFU PlatformID aggregation
- refusal to scan coincidental filesystem-like bytes in metadata-only providers
- cancellation as a hard stop

The dedicated gate runs before the existing provider/native Windows/Release x64 regression path in GitHub Actions.
