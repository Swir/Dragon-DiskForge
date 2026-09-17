# Dragon DiskForge — Exact Beta Candidate Pipeline

The first public beta target is `0.5.0-beta.1`. This document describes how Dragon DiskForge produces an exact, auditable **non-public** Windows x64 release candidate without prematurely changing the committed engineering version or publishing a GitHub Release.

## Why this exists

The committed development version remains `0.5.0-alpha.1` until the independent beta release gate is satisfied. Manual clean-desktop, normal-user UAC and real cross-process Explorer drag-out checks must be performed against the exact package that would be published. Rebuilding after those checks would invalidate the evidence.

The beta candidate pipeline solves that boundary explicitly:

1. checkout one exact source commit,
2. temporarily promote only the CI workspace version suffix from `alpha.1` to `beta.1`,
3. build the WinUI Release x64 application from that workspace,
4. build the normal clean package using the same packaging script as engineering CI,
5. independently verify package version, hashes, icon, package hygiene, CLI, shell helper and packaged manual-QA tool,
6. bind candidate metadata to the exact source commit and ZIP SHA-256,
7. retain the ZIP, checksum and metadata only when a `main` commit is deliberately marked `[beta-candidate]`,
8. do **not** create a tag or public GitHub Release.

The committed `Directory.Build.props` is not changed by the workflow. The version rewrite occurs only inside the ephemeral Actions workspace.

## Workflow

`.github/workflows/beta-candidate.yml` runs on pull requests and on `main` pushes. Every run builds and verifies the exact `0.5.0-beta.1` package contract, but routine runs are verification-only and intentionally do not retain another 160+ MiB artifact.

A package is retained only for a deliberately selected `main` commit whose commit message contains:

```text
[beta-candidate]
```

This explicit marker prevents normal development and hourly hardening work from filling GitHub Actions artifact storage with redundant release candidates. Selected candidates are retained for 14 days and use a name like:

```text
DragonDiskForge-0.5.0-beta.1-win-x64-candidate-<run-id>
```

The retained artifact contains:

```text
DragonDiskForge-win-x64.zip
DragonDiskForge-win-x64.zip.sha256
beta-candidate.json
beta-candidate.json.sha256
```

`beta-candidate.json` records the exact source commit, workflow run id, beta version, architecture, package SHA-256, package-manifest schema, desktop entry-point SHA-256 and packaged manual-QA-tool SHA-256. `publicRelease` is explicitly `false`.

A green beta-candidate workflow proves that the source commit can produce a correctly versioned and independently verified `0.5.0-beta.1` package. It does **not** prove the remaining interactive Windows checks and does **not** authorize publication. A retained artifact is only a selected engineering candidate; it is still not a release.

## Candidate contract script

`scripts/beta-candidate.ps1` has three modes.

### Self-test

```powershell
.\scripts\beta-candidate.ps1 -Mode self-test
```

This verifies workspace-only suffix promotion, fail-closed version-prefix handling and exact source-commit validation. CI executes the self-test in both PowerShell 7 and Windows PowerShell 5.1.

### Prepare

```powershell
.\scripts\beta-candidate.ps1 -Mode prepare
```

This rewrites the current workspace copy of `Directory.Build.props` to `0.5.0-beta.1` only after confirming the expected `0.5.0` version prefix. It is intended for disposable build workspaces. Do not commit the rewritten file during normal development.

### Metadata

After the package has been built and verified:

```powershell
.\scripts\beta-candidate.ps1 -Mode metadata `
  -SourceCommit <40-character-commit-sha> `
  -WorkflowRunId <run-id>
```

Metadata mode re-runs the clean-package verifier before writing candidate metadata. It fails closed on an invalid source SHA, package/checksum mismatch, wrong package version, wrong architecture or package manifest older than the beta-QA contract.

## Manual QA must use one exact candidate

Choose one successful **retained** candidate artifact and keep all four files together. Do not rename only one file without updating its checksum sidecar contract.

On a clean supported Windows desktop, extract or copy the candidate files and initialize the packaged evidence tool from a normal unelevated interactive PowerShell session:

```powershell
.\tools\beta-manual-qa.ps1 -Mode new `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -EvidencePath .\beta-manual-qa.json
```

The current manual-QA evidence format is **schema v2**. It records the clean-desktop Windows build, process architecture, interactive/elevation state and UAC availability at initialization. Every observation is then rebound to the exact package before it can be saved.

Record a required observation only after physically performing it, and always supply the same ZIP/checksum pair again:

```powershell
.\tools\beta-manual-qa.ps1 -Mode record `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -EvidencePath .\beta-manual-qa.json `
  -Check uac.iso-no-prompt `
  -Result pass `
  -HumanConfirmed `
  -Note "ISO mounted and detached with no elevation prompt."
```

A record operation fails closed if the ZIP, package checksum, desktop entry point, packaged QA tool, Windows build or process architecture no longer matches the evidence baseline. Passing observations additionally require an interactive unelevated session with UAC enabled when Windows exposes that setting. This prevents a stale evidence file from being filled in while a different candidate is actually under test.

Use `-Mode list` to see the remaining checks. When all observations are complete, verify the evidence against the **same** ZIP/checksum pair:

```powershell
.\tools\beta-manual-qa.ps1 -Mode verify `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -EvidencePath .\beta-manual-qa.json
```

The evidence is bound to the package SHA-256, packaged entry-point identities and each individual recorded observation. Schema-v1 evidence must be reinitialized and repeated; it is intentionally not auto-upgraded because doing so would fabricate package binding that was not captured at observation time.

## Publication rule

A candidate artifact is not a release. Do not publish a GitHub pre-release until all required items in `docs/BETA-RELEASE.md` are complete, including the package-bound interactive evidence.

When the gate is complete, the public beta must use the exact verified candidate ZIP and checksum rather than silently rebuilding a different package. The eventual tag/release notes must identify the source commit recorded in `beta-candidate.json` and preserve the known-limitations truthfulness rules.