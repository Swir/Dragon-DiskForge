# Dragon DiskForge — Unified Image Intelligence

`ImageIntelligenceService` is the 0.5 aggregation layer that combines already-proven read-only evidence without inventing capabilities that a provider does not expose. `ImageReportService` adds bounded depth and reconciliation evidence before presenting the user-facing text/JSON analysis.

## Evidence sources

The service resolves the image once through `ProviderRegistry` and then activates only the intelligence paths backed by the resolved descriptor:

- `PartitionTable` → `PartitionIntelligenceService`
- truthful physical filesystem byte mapping → `FileSystemRecognitionService`
- `DirectBrowse` → `BootInstallerIntelligenceService`
- `ContainerMetadata` + `IWimMetadataProvider` → WIM/ESD container GUID
- `ContainerMetadata` + `IFfuMetadataProvider` → FFU PlatformID
- recognized physical filesystem regions → bounded filesystem-depth services in the report path

Metadata-only sparse/compressed/container providers do not gain filesystem probing merely because their container bytes happen to resemble a filesystem signature.

## Cross-source identity aggregation

Current identity evidence includes:

- partition names from validated provider partition tables
- filesystem labels from bounded recognized filesystems
- filesystem identifiers such as volume serials or UUIDs when the recognizer has proven their structure
- validated deeper UDF primary/logical volume strings
- WIM/ESD container GUIDs
- FFU PlatformID metadata

Evidence is normalized and de-duplicated while retaining its source and partition index when applicable. Partition type IDs are not treated as unique volume identifiers.

## Architecture reconciliation

Boot/installer intelligence already provides bounded EFI fallback architecture hints and installer-family architecture hints. `ArchitectureReconciliationService` combines those independent sources for the user-facing analysis instead of silently selecting one source.

Rules:

- non-empty hints are normalized, de-duplicated and preserved
- a single boot-path hint and a single installer-path hint that agree remain one hint
- a direct one-to-one disagreement is preserved as both hints and produces `ARCHITECTURE_EVIDENCE_CONFLICT`
- multi-architecture boot evidence remains multi-architecture and is not automatically treated as a conflict
- the service never guesses which conflicting hint is “correct”

## Health and corruption evidence

Health findings are evidence-backed and read-only. They currently include:

- all structural partition findings, mapped into the shared image-health model
- exFAT `VolumeDirty` as a warning
- exFAT `MediaFailure` as an error
- ext superblock error state as an error
- ext non-clean state as a warning
- NTFS primary/backup boot-metadata mismatch as a warning
- NTFS backup boot-sector range overflow/out-of-range as an error
- bounded NTFS `$MFT` / `$MFTMirr` location, FILE-record size and Update Sequence Array findings
- fixup-normalized first-record `$MFT` / `$MFTMirr` divergence as a warning
- FAT32 primary/backup boot-metadata mismatch as a warning
- FAT32 backup boot-sector out-of-range as an error
- deeper exFAT boot-region checksum/redundancy findings
- FAT32 FSInfo signature/range/count findings
- bounded UDF anchor/descriptor validation findings
- direct architecture-evidence disagreement as a warning

A clean result means that none of these implemented checks produced a finding. It is **not** a promise that the entire filesystem is healthy.

## Bounded-read rules

Filesystem health/depth reads are allowed only inside the physical region already accepted by filesystem recognition. Every read is bounded both against that filesystem region and the physical file.

The services do not:

- repair a filesystem or partition table
- follow unproven guest-sector mappings
- mount an image as a side effect
- execute or extract installer content
- traverse NTFS attributes/directories as part of the current depth layer
- convert health evidence into destructive action
- treat a missing check as proof of health

## Current mapping boundary

Whole-file/partition filesystem probing is currently enabled only when the provider exposes a capability that proves a direct physical-byte path for the current intelligence layer: `PartitionTable`, `MediaGeometry`, or `DirectBrowse`.

VMDK, QCOW/QCOW2, DMG and similar metadata-only virtual/container formats remain outside guest filesystem intelligence until a dedicated guest-sector reader exists and is independently tested.

## Validation

`tests/DragonDiskForge.ImageIntelligence.SmokeTests` and `tests/DragonDiskForge.IntelligenceHardening.SmokeTests` use generated fixtures to prove:

- partition-name + filesystem-ID aggregation
- exFAT dirty/media-failure health findings
- NTFS backup boot-metadata mismatch detection
- bounded NTFS `$MFT` / `$MFTMirr` FILE-record validation and mirror consistency
- ext error-state detection and label aggregation
- WIM container GUID aggregation
- FFU PlatformID aggregation
- architecture-hint preservation and direct conflict reporting
- refusal to scan coincidental filesystem-like bytes in metadata-only providers
- cancellation as a hard stop

The dedicated gates run before the existing provider/native Windows/Release x64 regression path in GitHub Actions. PR #34 implementation run #266 also proved the clean Windows package-candidate/checksum path before documentation synchronization.
