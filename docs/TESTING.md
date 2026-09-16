# Dragon DiskForge — Testing

Dragon DiskForge treats a green Windows build, Core/provider smoke tests and real Windows integration tests as requirements for milestone progress.

## Core smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.Core.SmokeTests/DragonDiskForge.Core.SmokeTests.csproj -c Release
```

The Core harness validates format/signature detection, safe mount defaults, SHA-256 behavior, Explorer listing/search/copy-out safety, Preview classification and Image Library persistence behavior.

## Provider registry smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.ProviderRegistry.SmokeTests/DragonDiskForge.ProviderRegistry.SmokeTests.csproj -c Release
```

These tests validate:

- provider capability reporting
- priority + extension-first selection
- fallback beyond an extension candidate
- probe-failure isolation
- inspection-failure isolation with continued fallback
- diagnostic retention
- duplicate provider-ID rejection
- cancellation as a hard stop

## IMG / RAW provider smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.RawProvider.SmokeTests/DragonDiskForge.RawProvider.SmokeTests.csproj -c Release
```

The IMG/RAW gate proves:

- valid flat `.img` and `.raw` candidates are accepted read-only
- implausibly small images are rejected
- non-512-byte-aligned candidates are rejected
- a structurally valid ISO renamed to `.img` is rejected by RAW and resolved by the ISO provider through registry fallback
- missing images fail safely during probing
- inspection reports exact source size and leaves Browse/Mount/Convert disabled
- SHA-256 before/after inspection is unchanged
- pre-cancelled provider probing stops safely

## Mounted-history smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.MountHistory.SmokeTests/DragonDiskForge.MountHistory.SmokeTests.csproj -c Release
```

These tests validate bounded local history, Mount/Unmount event retention and the separation between local metadata and live Windows state.

## Drag-out safety smoke tests

```powershell
dotnet run --project tests/DragonDiskForge.DragOut.SmokeTests/DragonDiskForge.DragOut.SmokeTests.csproj -c Release
```

The drag-out validator proves root containment, stale-source handling, reparse-point/junction rejection and Copy-only transfer semantics. The actual human cross-process pointer gesture remains manual desktop QA.

## Provider-backed ISO direct-browse integration

```powershell
dotnet run --project tests/DragonDiskForge.DirectBrowse.IntegrationTests/DragonDiskForge.DirectBrowse.IntegrationTests.csproj -c Release
```

This suite creates a disposable ISO with Windows IMAPI2FS and validates the managed ISO9660/Joliet provider without mounting the image. It proves real recognition, list/navigation/search, safe file/folder Copy out, cancellation, overwrite/path/extent safety and that the image remains detached.

## Native Windows mount / Explorer integration

Run on an elevated Windows development session:

```powershell
dotnet run --project tests/DragonDiskForge.Windows.IntegrationTests/DragonDiskForge.Windows.IntegrationTests.csproj -c Release
```

The integration suite creates disposable images at runtime. It validates native ISO/VHD/VHDX lifecycle, read-only behavior, drive/inventory detection, cancellation safety, mounted-volume Explorer list/search/Copy out and Preview against a real IMAPI-generated ISO.

Mounted inventory is intentionally derived from current Windows Storage state rather than remembered application state.

## Windows x64 validation

Equivalent commands on a Windows developer machine are:

```powershell
msbuild DragonDiskForge.sln /restore /p:Configuration=Release /p:Platform=x64
msbuild DragonDiskForge.sln /m /p:Configuration=Release /p:Platform=x64
```

Every green CI run uploads `DragonDiskForge-win-x64` as a temporary workflow artifact for desktop/manual validation.

## CI order

1. Checkout repository.
2. Install .NET 10 SDK.
3. Run Core smoke tests.
4. Run provider-registry smoke tests.
5. Run IMG/RAW provider smoke tests.
6. Run mounted-history smoke tests.
7. Run drag-out safety smoke tests.
8. Run provider-backed ISO direct-browse integration while the image remains detached.
9. Run native Windows ISO/VHD/VHDX + mounted Explorer/Preview integration tests.
10. Configure MSBuild.
11. Restore the solution.
12. Build the WinUI application in Release x64.
13. Publish the Windows x64 workflow artifact.

A feature should not be marked complete in `docs/ROADMAP.md` merely because code was committed. Its relevant real test/build path must pass first.

## Manual desktop validation

GitHub-hosted Windows runners execute as administrators and cannot faithfully emulate all interactive desktop behavior. The required UAC and cross-process drag-out matrix is maintained in `docs/MANUAL-VALIDATION.md`.

Direct ISO browsing itself is automatically validated because it does not depend on a human shell gesture; its integration test explicitly confirms the image remains detached.

## Test-data rule

Do not commit large real disk images to the repository. Provider/signature tests generate minimal temporary files, and mount/direct-browse integration creates disposable images at runtime with Windows-native tooling.
