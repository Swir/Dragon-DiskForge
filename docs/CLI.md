# Dragon DiskForge CLI

Dragon DiskForge includes an automation CLI built on the same Core services and canonical provider registry as the Windows desktop application.

The clean Windows x64 package places the self-contained executable at:

`cli/dragon-diskforge.exe`

No .NET SDK or Visual Studio is required to run that packaged CLI executable. This statement is verified by the self-contained publish/package path itself; clean-machine end-user validation remains part of the separate beta gate.

## Safety boundary

Image/media commands are read-only:

- `analyze`
- `verify`
- `formats`

The CLI also exposes narrowly scoped Dragon DiskForge **local application-state** tooling:

- `state-show`
- `state-export`
- `state-import`
- `restore-last-image`
- `diagnostics`

These state commands may read or atomically update Dragon DiskForge's settings/session JSON or create a sanitized support ZIP. They do **not** modify inspected images or physical media.

The CLI intentionally does **not** expose:

- Create
- Convert
- split/join or gzip output pipelines
- physical-media writes
- destructive target selection

Adding a command later does not automatically make the corresponding desktop or destructive capability safe for automation; each command requires its own contract and tests.

## Analyze

Run the same provider-backed image-intelligence/reporting path used by the desktop application:

```powershell
.\cli\dragon-diskforge.exe analyze .\sample.iso
.\cli\dragon-diskforge.exe analyze .\sample.iso --format json
```

`--format` accepts `text` or `json` and defaults to `text`.

Unknown or unsupported images remain truthful: a readable file can still produce a report with no selected provider instead of fabricating format support.

## Verify

Compute SHA-256 and SHA-512 in one bounded sequential pass:

```powershell
.\cli\dragon-diskforge.exe verify .\sample.iso
```

Optionally require expected digests:

```powershell
.\cli\dragon-diskforge.exe verify .\sample.iso `
  --sha256 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef `
  --format json
```

Both algorithms may be supplied together:

```powershell
.\cli\dragon-diskforge.exe verify .\sample.iso `
  --sha256 <64-hex-characters> `
  --sha512 <128-hex-characters> `
  --format json
```

Expected digest values must contain exactly the required number of hexadecimal characters. Matching is case-insensitive after validation.

If any supplied expected digest does not match, the CLI still emits the complete verification result but exits with code `4`.

## Formats

List the canonical built-in provider registry:

```powershell
.\cli\dragon-diskforge.exe formats
.\cli\dragon-diskforge.exe formats --format json
```

The output includes provider id, display name, supported extensions, declared capabilities and priority. The WinUI app and CLI consume the same `ProviderRegistryFactory`, preventing independent provider lists from drifting apart.

## Session and settings state

The default state file is:

`%LocalAppData%\DragonDiskForge\app-state.json`

Show the current versioned state:

```powershell
.\cli\dragon-diskforge.exe state-show
.\cli\dragon-diskforge.exe state-show --format json
```

Export and later import the state through the same atomic safe-output boundary used by Core file-producing operations:

```powershell
.\cli\dragon-diskforge.exe state-export .\dragon-state.json
.\cli\dragon-diskforge.exe state-import .\dragon-state.json
```

Control best-effort desktop restoration of the last inspected image:

```powershell
.\cli\dragon-diskforge.exe restore-last-image off
.\cli\dragon-diskforge.exe restore-last-image on
```

For tests, portable workflows or automation that must not touch the normal profile, every state command accepts an isolated `--state` path:

```powershell
.\cli\dragon-diskforge.exe state-show --state .\temporary-state.json --format json
.\cli\dragon-diskforge.exe restore-last-image off --state .\temporary-state.json
```

Imports reject unsupported schema versions, oversized/invalid state files and invalid saved-path data before local state is replaced. A rejected import leaves the existing state unchanged.

## Diagnostics

Export a sanitized diagnostic support ZIP:

```powershell
.\cli\dragon-diskforge.exe diagnostics .\dragon-support.zip
```

The bundle contains:

- `diagnostics.json` — runtime/OS/process-architecture/product-version/provider evidence
- `state-summary.json` — sanitized state summary
- `README.txt` — explicit privacy/scope note

The support ZIP intentionally excludes the full saved image path and image contents. It may include only the saved image's extension, whether a saved session exists, the restore setting and timestamp/runtime/provider evidence.

Use `--state <path>` when diagnostics should summarize an isolated portable state rather than the normal profile.

## Automation contract

Dragon DiskForge CLI is designed to be predictable in PowerShell and other process automation:

- successful command output is written to **stdout**
- JSON mode writes **one complete JSON document** to stdout
- diagnostics/errors are written to **stderr**
- input/usage failures do not mix partial JSON with stdout
- image inspection/verification commands do not modify the inspected image or physical media
- state/settings commands modify only Dragon DiskForge local application state
- no command writes to physical media

### Exit codes

| Code | Meaning |
| ---: | --- |
| `0` | success / all supplied expected digests matched |
| `1` | unexpected internal failure |
| `2` | command-line usage/validation error |
| `3` | input or supported-operation failure |
| `4` | verification completed but an expected digest mismatched |
| `130` | operation cancelled |

PowerShell example:

```powershell
$json = & .\cli\dragon-diskforge.exe verify .\sample.iso --format json
if ($LASTEXITCODE -ne 0) {
    throw "Verification failed with exit code $LASTEXITCODE"
}
$result = $json | ConvertFrom-Json
$result.Sha256
```

Expected-mismatch handling:

```powershell
$json = & .\cli\dragon-diskforge.exe verify .\sample.iso --sha256 $expected --format json
if ($LASTEXITCODE -eq 4) {
    $result = $json | ConvertFrom-Json
    Write-Error "SHA-256 mismatch. Actual: $($result.Sha256)"
}
```

## Package integrity

The Windows packaging pipeline:

1. publishes the CLI as a self-contained `win-x64` single-file executable;
2. excludes debug symbols from the public package staging directory;
3. writes `cliEntryPoint` and `cliEntryPointSha256` to `package-manifest.json`;
4. verifies the CLI SHA-256 after extracting the final ZIP;
5. launches the extracted CLI and requires `formats --format json` to return the canonical provider set;
6. publishes the package ZIP plus its independent SHA-256 sidecar only after verification passes.

PR #48 implementation run #343 validates the original CLI smoke tests, Release x64 build, self-contained CLI packaging and clean-package runtime verification. Disposable Media Guard #15 is green for that checkpoint.

PR #49 implementation run #353 validates the session/settings/diagnostic portability smoke gate together with the full existing Windows regression suite, native integrations, Release x64 build and clean-package verification. Disposable Media Guard #25 is green for the same implementation head.
