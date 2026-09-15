# Dragon DiskForge — Testing

Dragon DiskForge treats a green Windows build, Core smoke tests and real Windows integration tests as requirements for milestone progress.

## Core smoke tests

Run from the repository root:

```powershell
dotnet run --project tests/DragonDiskForge.Core.SmokeTests/DragonDiskForge.Core.SmokeTests.csproj -c Release
```

The current harness validates format/signature detection, safe mount defaults, SHA-256 behavior, Explorer listing/search/copy-out safety, Preview classification and Image Library persistence behavior.

## Mounted-history smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.MountHistory.SmokeTests/DragonDiskForge.MountHistory.SmokeTests.csproj -c Release
```

These tests validate bounded local history, Mount/Unmount event retention and the separation between local metadata and live Windows state.

## Drag-out safety smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.DragOut.SmokeTests/DragonDiskForge.DragOut.SmokeTests.csproj -c Release
```

The drag-out validator currently proves:

- an existing file inside the mounted root is accepted
- an existing folder inside the mounted root is accepted
- a path outside the mounted root is rejected
- a listed reparse point/junction is rejected before transfer
- a stale/missing source is rejected
- the WinUI payload path is compiled as Copy-only, never Move

The actual human gesture from Dragon Explorer into Windows Explorer/Desktop remains a manual desktop QA case because GitHub Actions cannot reliably emulate cross-process pointer drag/drop.

## Native Windows mount / Explorer integration

Run on an elevated Windows development session:

```powershell
dotnet run --project tests/DragonDiskForge.Windows.IntegrationTests/DragonDiskForge.Windows.IntegrationTests.csproj -c Release
```

The integration suite creates disposable images at runtime. It validates native ISO/VHD/VHDX lifecycle, read-only behavior, drive/inventory detection, cancellation safety, mounted-volume Explorer list/search/Copy out, mounted-ISO content verification and Preview against a real IMAPI-generated ISO.

Mounted inventory is intentionally derived from current Windows Storage state rather than remembered application state.

## Windows x64 validation

GitHub Actions builds the real Windows application with the `Release|x64` solution configuration.

Equivalent commands on a Windows developer machine are:

```powershell
msbuild DragonDiskForge.sln /restore /p:Configuration=Release /p:Platform=x64
msbuild DragonDiskForge.sln /m /p:Configuration=Release /p:Platform=x64
```

Every green CI run also uploads `DragonDiskForge-win-x64` as a temporary workflow artifact for desktop/manual validation.

## CI order

1. Checkout repository.
2. Install .NET 10 SDK.
3. Run Core smoke tests.
4. Run mounted-history smoke tests.
5. Run drag-out safety smoke tests.
6. Run native Windows ISO/VHD/VHDX + Explorer/Preview integration tests.
7. Configure MSBuild.
8. Restore the solution.
9. Build the WinUI application in Release x64.
10. Publish the Windows x64 workflow artifact.

A feature should not be marked complete in `docs/ROADMAP.md` merely because code was committed. Its relevant real test/build path must pass first.

## Manual desktop validation

GitHub-hosted Windows runners execute as administrators and cannot faithfully emulate all interactive desktop behavior. The required UAC and cross-process drag-out matrix is maintained in `docs/MANUAL-VALIDATION.md`.

## Test-data rule

Do not commit large real disk images to the repository. Signature tests generate minimal temporary files, and mount integration creates disposable images at runtime with Windows-native tooling.
