# Dragon DiskForge — Testing

Dragon DiskForge treats a green Windows build, Core smoke tests and real Windows integration tests as requirements for milestone progress.

## Core smoke tests

Run from the repository root:

```powershell
dotnet run --project tests/DragonDiskForge.Core.SmokeTests/DragonDiskForge.Core.SmokeTests.csproj -c Release
```

The current harness validates:

- case-insensitive extension lookup
- known/unknown extension behavior
- VHDX signature detection
- QCOW/QCOW2 signature detection
- ISO-9660 `CD001` signature detection
- signature priority over a misleading extension
- extension fallback for known formats
- native-mount capability reporting for ISO
- mount-request safe defaults
- SHA-256 verification through `ImageVerificationService`
- SHA-256 progress and cancellation
- missing-file error behavior

The harness returns a non-zero process exit code if any check fails, so GitHub Actions can stop before Windows integration/build work.

## Native Windows mount integration

Run on an elevated Windows development session:

```powershell
dotnet run --project tests/DragonDiskForge.Windows.IntegrationTests/DragonDiskForge.Windows.IntegrationTests.csproj -c Release
```

The integration suite creates disposable test images at runtime; large binary fixtures are not committed to the repository.

It currently validates:

- VHD creation with DiskPart
- VHDX creation with DiskPart
- ISO creation with Windows IMAPI2FS
- ISO/VHD/VHDX capability routing
- detached initial state
- pre-cancelled mount safety
- native mount and unmount
- read-only VHD/VHDX mount behavior
- NoDriveLetter behavior
- automatic drive-letter detection
- mounted ISO file access
- live Mounted inventory appearance after mount
- live Mounted inventory removal after unmount
- friendly unsupported-format errors
- progress completion reporting

Mounted inventory is intentionally derived from current Windows Storage state rather than remembered application state.

## Windows x64 validation

GitHub Actions builds the real Windows application with the `Release|x64` solution configuration.

Equivalent commands on a Windows developer machine are:

```powershell
msbuild DragonDiskForge.sln /restore /p:Configuration=Release /p:Platform=x64
msbuild DragonDiskForge.sln /m /p:Configuration=Release /p:Platform=x64
```

## CI order

1. Checkout repository.
2. Install .NET 10 SDK.
3. Run Core smoke tests.
4. Run native Windows ISO/VHD/VHDX mount integration tests.
5. Configure MSBuild.
6. Restore the solution.
7. Build the WinUI application in Release x64.

A feature should not be marked complete in `docs/ROADMAP.md` merely because code was committed. Its relevant real test/build path must pass first.

## Manual desktop validation

GitHub-hosted Windows runners execute as administrators, so interactive normal-user UAC behavior cannot be proven faithfully in CI. The required desktop validation matrix is maintained in `docs/MANUAL-VALIDATION.md`.

## Test-data rule

Do not commit large real disk images to the repository. Signature tests generate minimal temporary files, and mount integration creates disposable images at runtime with Windows-native tooling.
