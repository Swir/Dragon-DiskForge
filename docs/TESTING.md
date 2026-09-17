# Dragon DiskForge — Testing

Dragon DiskForge treats a green Windows build, Core smoke tests, provider/intelligence gates and real Windows integration tests as requirements for milestone progress.

## Core + registry gates

```powershell
dotnet run --project tests/DragonDiskForge.Core.SmokeTests/DragonDiskForge.Core.SmokeTests.csproj -c Release
dotnet run --project tests/DragonDiskForge.ProviderRegistry.SmokeTests/DragonDiskForge.ProviderRegistry.SmokeTests.csproj -c Release
```

These validate base detection/verification behavior plus provider descriptor validation, deterministic resolution, extension normalization, truthful capability inference, failure isolation and cancellation.

## File-producing safety gates

```powershell
dotnet run --project tests/DragonDiskForge.Verification.SmokeTests/DragonDiskForge.Verification.SmokeTests.csproj -c Release
dotnet run --project tests/DragonDiskForge.SafeOutput.SmokeTests/DragonDiskForge.SafeOutput.SmokeTests.csproj -c Release
dotnet run --project tests/DragonDiskForge.RawImagePipelines.SmokeTests/DragonDiskForge.RawImagePipelines.SmokeTests.csproj -c Release
dotnet run --project tests/DragonDiskForge.SplitCompression.SmokeTests/DragonDiskForge.SplitCompression.SmokeTests.csproj -c Release
```

These prove dual SHA-256/SHA-512 verification, atomic single-file output publication, bounded RAW creation/guest export, transactional split/join and bounded whole-file gzip transport handling. They do not imply that Core file-producing APIs are automatically exposed by the WinUI application.

## Physical-media safety and execution contract

```powershell
dotnet run --project tests/DragonDiskForge.PhysicalMediaSafety.SmokeTests/DragonDiskForge.PhysicalMediaSafety.SmokeTests.csproj -c Release
```

The generated-file/injected-sink suite proves the non-device safety contract:

- hard refusal of system disks, ambiguous identity, unknown capacity, physical-device sources and oversized images
- exact destination-bound confirmation
- successful bounded sequential transfer through `IPhysicalMediaWriteSink`
- destination stable-identity swap / TOCTOU refusal before destination I/O
- source-length drift refusal before destination I/O
- monotonic progress and SHA-256 evidence for chunks accepted successfully by the sink
- cancellation before the first write versus cancellation after a successful prefix write
- explicit recovery requirement after cancellation/failure once destination mutation may have begun
- flush failure cannot be reported as successful completion
- throwing progress observers are isolated from destructive I/O

This suite deliberately does **not** validate a Windows physical-device writer. No such writer is exposed yet. Real physical-write validation remains gated on dedicated disposable media.

The native Windows integration suite additionally verifies query-only physical-disk enumeration, system-volume disk-extent evidence and refusal of every detected system disk.

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

The generated-fixture suite proves bounded El Torito discovery, BIOS/EFI catalog parsing, physical load-range validation, hybrid bootability evidence, Windows/Linux installer markers, fallback architecture hints, malformed-catalog refusal, Direct Browse capability enforcement and cancellation.

The service also bounds Direct Browse traversal and rejects reparse-point or traversal-style evidence.

## Unified image intelligence

```powershell
dotnet run --project tests/DragonDiskForge.ImageIntelligence.SmokeTests/DragonDiskForge.ImageIntelligence.SmokeTests.csproj -c Release
```

Generated fixtures prove partition/filesystem identity aggregation, exFAT/NTFS/ext health evidence, WIM/ESD/FFU identity aggregation, truthful capability boundaries and cancellation. Every filesystem health read is bounded to a filesystem region already accepted by recognition and then bounded again to the physical file. These tests never perform repair.

## Image-provider gates

Dedicated smoke projects cover:

- RAW/IMG MBR/EBR/GPT
- IMA/floppy geometry/BPB
- BIN/CUE
- MDF/MDS
- NRG
- CCD/IMG/SUB
- VMDK sparse metadata and proven hosted-sparse guest-byte reading
- QCOW/QCOW2 metadata and proven standard-uncompressed guest-byte reading
- DMG/UDIF metadata
- WIM/ESD metadata
- FFU metadata
- common guest-relative partition/filesystem intelligence

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

## Native Windows mount / Explorer / physical inventory integration

Run on an elevated Windows development session:

```powershell
dotnet run --project tests/DragonDiskForge.Windows.IntegrationTests/DragonDiskForge.Windows.IntegrationTests.csproj -c Release
```

Disposable runtime images validate native ISO/VHD/VHDX lifecycle, read-only behavior, drive/inventory detection, cancellation, mounted-volume Explorer operations and Preview/content behavior. The suite also exercises real query-only physical-disk inventory and verifies fail-closed refusal of the runner's detected system disk.

Mounted inventory is intentionally derived from current Windows Storage state rather than remembered application state.

## Windows x64 validation

GitHub Actions restores and builds the real Windows application with `Release|x64`, then creates and independently verifies the clean package candidate before publishing workflow artifacts.

```powershell
msbuild DragonDiskForge.sln /restore /p:Configuration=Release /p:Platform=x64
msbuild DragonDiskForge.sln /m /p:Configuration=Release /p:Platform=x64
```

## Current CI order

1. Checkout repository and install .NET 10.
2. Core, dual-verification, safe-output, RAW-pipeline, split/join + gzip and physical-media safety smoke gates.
3. Provider registry, partition/filesystem depth, boot/install, unified intelligence and reporting gates.
4. Every dedicated provider and guest-reader gate.
5. Mount-history and drag-out safety gates.
6. Provider-backed ISO direct-browse integration.
7. Native Windows ISO/VHD/VHDX + mounted Explorer + query-only physical inventory integration.
8. Configure MSBuild, restore and build WinUI `Release|x64`.
9. Build and independently verify the clean Windows x64 package candidate.
10. Publish clean-package and engineering artifacts.

PR #46 implementation run #319 passed the physical-media execution-contract head together with this complete Windows regression/build/package sequence before documentation synchronization.

A feature is not complete because code was committed. Its relevant test path and the required full regression/build gate must pass first.

## Manual desktop validation

GitHub-hosted Windows runners execute as administrators and cannot faithfully emulate all interactive desktop behavior. Required clean-machine runtime, normal-user UAC and cross-process drag-out cases are maintained in `docs/MANUAL-VALIDATION.md` and `docs/BETA-RELEASE.md`.

## Test-data rule

Do not commit large real disk images. Metadata/signature tests generate minimal or sparse temporary fixtures, mount/direct-browse integration creates disposable images at runtime with Windows-native tooling, and future physical-write tests must use dedicated disposable media rather than a developer/system disk.
