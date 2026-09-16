# Dragon DiskForge — Testing

Dragon DiskForge treats a green Windows build, Core smoke tests, provider/intelligence gates and real Windows integration tests as requirements for milestone progress.

## Core + registry gates

```powershell
dotnet run --project tests/DragonDiskForge.Core.SmokeTests/DragonDiskForge.Core.SmokeTests.csproj -c Release
dotnet run --project tests/DragonDiskForge.ProviderRegistry.SmokeTests/DragonDiskForge.ProviderRegistry.SmokeTests.csproj -c Release
```

These validate base detection/verification behavior plus provider descriptor validation, deterministic resolution, extension normalization, truthful capability inference, failure isolation and cancellation.

## Partition intelligence

```powershell
dotnet run --project tests/DragonDiskForge.PartitionIntelligence.SmokeTests/DragonDiskForge.PartitionIntelligence.SmokeTests.csproj -c Release
```

The suite uses capability-driven providers and verifies clean layouts plus duplicate indexes, zero-length entries, LBA overflow, byte-geometry mismatches, physical bounds, overlaps, recognized providers without `PartitionTable`, and cancellation.

## Filesystem recognition

```powershell
dotnet run --project tests/DragonDiskForge.FileSystemRecognition.SmokeTests/DragonDiskForge.FileSystemRecognition.SmokeTests.csproj -c Release
```

Fixtures are generated as bounded/sparse temporary images at runtime. The suite proves FAT12/FAT16/FAT32, exFAT, supported NTFS boot metadata, ext2/ext3/ext4, ISO9660/Joliet and UDF VRS recognition; partition-scoped physical ranges; false-positive resistance; invalid-layout refusal; and cancellation.

The recognition tests intentionally do not pretend physical bytes in VMDK/QCOW/DMG containers are guest filesystem sectors.

## Boot + installer intelligence

```powershell
dotnet run --project tests/DragonDiskForge.BootInstallerIntelligence.SmokeTests/DragonDiskForge.BootInstallerIntelligence.SmokeTests.csproj -c Release
```

The generated-fixture suite proves:

- bounded El Torito boot-record/catalog discovery
- validation-entry checksum/key checks
- BIOS default-entry and EFI section-entry parsing
- physical boot-image load-range validation
- hybrid BIOS + UEFI bootability evidence
- Windows setup/boot-WIM/install-payload evidence
- Linux casper installer/live evidence
- standard EFI fallback architecture hints
- filesystem markers without a boot catalog do not fabricate bootability
- malformed catalog checksums fail closed
- out-of-file boot load ranges fail closed
- providers without truthful Direct Browse support are rejected
- cancellation remains a hard stop

The service also bounds Direct Browse traversal and rejects reparse-point or traversal-style evidence.

## Unified image intelligence

```powershell
dotnet run --project tests/DragonDiskForge.ImageIntelligence.SmokeTests/DragonDiskForge.ImageIntelligence.SmokeTests.csproj -c Release
```

Generated fixtures prove the aggregation and health-evidence contract rather than only compilation:

- partition-name + filesystem identifier aggregation from a structurally valid partition region
- exFAT `VolumeDirty` warning and `MediaFailure` error
- NTFS primary/backup boot-metadata mismatch detection
- ext filesystem error-state detection and label aggregation
- WIM/ESD container GUID aggregation through `IWimMetadataProvider`
- FFU PlatformID aggregation through `IFfuMetadataProvider`
- metadata-only providers cannot gain filesystem probing from coincidental filesystem-like physical bytes
- unsupported partition/boot surfaces remain absent instead of guessed
- cancellation remains a hard stop

Every filesystem health read is bounded to the filesystem region that recognition already accepted and then bounded again to the physical file. These tests do not claim whole-filesystem health and never perform repair.

## Image-provider gates

Dedicated smoke projects cover:

- RAW/IMG MBR/EBR/GPT
- IMA/floppy geometry/BPB
- BIN/CUE
- MDF/MDS
- NRG
- CCD/IMG/SUB
- VMDK sparse metadata
- QCOW/QCOW2 metadata
- DMG/UDIF metadata
- WIM/ESD metadata
- FFU metadata

Every new provider is exercised independently before the broader Windows regression/build steps.

## Mounted-history smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.MountHistory.SmokeTests/DragonDiskForge.MountHistory.SmokeTests.csproj -c Release
```

These validate bounded local history, Mount/Unmount event retention and separation between local metadata and live Windows state.

## Drag-out safety smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.DragOut.SmokeTests/DragonDiskForge.DragOut.SmokeTests.csproj -c Release
```

The validator proves accepted in-root files/folders, rejection of outside-root paths, reparse points and stale sources, and Copy-only WinUI payload behavior. The actual cross-process human drag gesture remains manual QA.

## Provider-backed ISO direct-browse integration

Run on Windows:

```powershell
dotnet run --project tests/DragonDiskForge.DirectBrowse.IntegrationTests/DragonDiskForge.DirectBrowse.IntegrationTests.csproj -c Release
```

The suite creates a disposable ISO with Windows IMAPI2FS and validates ISO9660/Joliet list/navigation/search/Copy out without mounting, overwrite/path-traversal protection, cancellation, fake-extension rejection and detached-state preservation.

## Native Windows mount / Explorer integration

Run on an elevated Windows development session:

```powershell
dotnet run --project tests/DragonDiskForge.Windows.IntegrationTests/DragonDiskForge.Windows.IntegrationTests.csproj -c Release
```

Disposable runtime images validate native ISO/VHD/VHDX lifecycle, read-only behavior, drive/inventory detection, cancellation, mounted-volume Explorer operations and Preview/content behavior.

Mounted inventory is intentionally derived from current Windows Storage state rather than remembered application state.

## Windows x64 validation

GitHub Actions builds the real Windows application with `Release|x64`:

```powershell
msbuild DragonDiskForge.sln /restore /p:Configuration=Release /p:Platform=x64
msbuild DragonDiskForge.sln /m /p:Configuration=Release /p:Platform=x64
```

Every green CI run uploads `DragonDiskForge-win-x64` as a temporary workflow artifact for desktop/manual validation.

## Current CI order

1. Checkout repository.
2. Install .NET 10 SDK.
3. Core smoke tests.
4. Provider-registry smoke tests.
5. Partition-intelligence smoke tests.
6. Filesystem-recognition smoke tests.
7. Boot + installer intelligence smoke tests.
8. Unified image-intelligence smoke tests.
9. All dedicated image-provider smoke tests.
10. Mount-history and drag-out safety smoke tests.
11. Provider-backed ISO direct-browse integration.
12. Native Windows ISO/VHD/VHDX + mounted Explorer integration.
13. Configure MSBuild.
14. Restore solution.
15. Build WinUI Release x64.
16. Upload Windows x64 artifact.

PR #31 / run #245 proved the unified image-intelligence code/test head together with the complete provider, Explorer/native Windows, Release x64 and artifact regression path before documentation synchronization.

A feature is not complete because code was committed. Its relevant test path and the required full regression/build gate must pass first.

## Manual desktop validation

GitHub-hosted Windows runners execute as administrators and cannot faithfully emulate all interactive desktop behavior. Required UAC and cross-process drag-out cases are maintained in `docs/MANUAL-VALIDATION.md`.

## Test-data rule

Do not commit large real disk images. Metadata/signature tests generate minimal or sparse temporary fixtures, and mount/direct-browse integration creates disposable images at runtime with Windows-native tooling.
