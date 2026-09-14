# Dragon DiskForge — Testing

Dragon DiskForge treats a green Windows build and real Core smoke tests as a requirement for milestone progress.

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
- SHA-256 verification through `ImageVerificationService`
- missing-file error behavior

The harness returns a non-zero process exit code if any check fails, so GitHub Actions can stop before the WinUI build.

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
4. Configure MSBuild.
5. Restore the solution.
6. Build the WinUI application in Release x64.

A feature should not be marked complete in `docs/ROADMAP.md` merely because code was committed. Its relevant test/build path must pass first.

## Test-data rule

Do not commit large real disk images to the repository. Signature tests should generate minimal temporary files at runtime. Larger integration images, when introduced, must use documented external fixtures or generated sparse/test data.
