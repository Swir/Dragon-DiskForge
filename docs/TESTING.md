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

Fixtures are generated as bounded/sparse temporary images at runtime. The suite proves:

- FAT12, FAT16 and FAT32 BPB/cluster-count classification
- FAT label, serial, logical-sector and allocation-unit metadata
- exFAT OEM/geometry/serial recognition
- NTFS OEM/BPB/serial/cluster recognition
- ext2/ext3/ext4 superblock + feature classification, label, UUID and block size
- ISO9660 primary descriptor recognition
- Joliet variant and UCS-2 volume label handling
- ordered UDF `BEA01` / `NSR02|NSR03` / `TEA01` VRS recognition
- filesystem scanning inside provider-reported partition ranges
- preservation of partition index and physical offset in evidence
- blank recognized images do not receive guessed filesystems
- unrecognized images are rejected at the provider gate
- structurally invalid partition layouts are rejected before filesystem probing
- pre-cancelled recognition stops immediately

The recognition tests intentionally do not pretend physical bytes in VMDK/QCOW/DMG containers are guest filesystem sectors.

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
7. All dedicated image-provider smoke tests.
8. Mount-history and drag-out safety smoke tests.
9. Provider-backed ISO direct-browse integration.
10. Native Windows ISO/VHD/VHDX + mounted Explorer integration.
11. Configure MSBuild.
12. Restore solution.
13. Build WinUI Release x64.
14. Upload Windows x64 artifact.

A feature is not complete because code was committed. Its relevant test path and the required full regression/build gate must pass first.

## Manual desktop validation

GitHub-hosted Windows runners execute as administrators and cannot faithfully emulate all interactive desktop behavior. Required UAC and cross-process drag-out cases are maintained in `docs/MANUAL-VALIDATION.md`.

## Test-data rule

Do not commit large real disk images. Metadata/signature tests generate minimal or sparse temporary fixtures, and mount/direct-browse integration creates disposable images at runtime with Windows-native tooling.
