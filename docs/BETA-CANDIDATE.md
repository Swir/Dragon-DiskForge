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
8. record interactive evidence with the exact manual-QA script carried by that retained candidate,
9. use the release-proof verifier to bind source commit, candidate metadata, package and completed evidence before publication,
10. do **not** create a tag or public GitHub Release until every independent release gate passes.

The committed `Directory.Build.props` is not changed by the workflow. The version rewrite occurs only inside the ephemeral Actions workspace.

## Workflow

`.github/workflows/beta-candidate.yml` runs on pull requests and on `main` pushes. Every run builds and verifies the exact `0.5.0-beta.1` package contract, but routine runs are verification-only and intentionally do not retain another large artifact.

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

## Selecting one retained candidate

Select a retained candidate only after all engineering changes intended for that candidate have passed the complete exact-head PR gate. The final merge commit on `main` must contain `[beta-candidate]`; the subsequent `main` workflow is then the authoritative build that retains the ZIP, checksum and candidate metadata.

The marker is a retention decision, not a release approval. Do not create empty/no-op commits solely to obtain an artifact, and do not mark ordinary hourly development commits. Pair candidate selection with a real release-process or documentation synchronization change so the selected source state is intentional and auditable.

Before starting manual QA, confirm that the retained artifact's `beta-candidate.json` names the exact selected `main` commit and that its package SHA-256 matches `DragonDiskForge-win-x64.zip.sha256`. If any code, packaging, release tooling or beta-gate behavior changes afterward, select a new retained candidate and repeat package-bound manual evidence rather than carrying observations forward.

### Current reselection checkpoint

The previous retained candidate is invalid for further beta QA because a real Windows test found that double-clicking `DragonDiskForge.App.exe` could exit without showing the application. PR #69 replaces that candidate boundary with a runtime-complete desktop package: .NET and Windows App SDK are self-contained, the supported VC143 CRT is staged app-local, required runtime payload is verified before packaging, and early managed startup failures become fail-visible through a bounded local log plus native Windows error dialog. The package helper contract was also corrected to stage the actual `dragon-diskforge.exe` and `dragon-diskforge-shell.exe` assembly names.

PR #69 exact head `e4475b049f25bfdc898c30efa58e2f7ac3ee2074` passed the complete ten-workflow exact-head gate, including Build, Desktop Runtime Contract, Clean Machine Runtime, Beta Candidate, security, accessibility and beta-proof contracts, before merge. The following `main` checkpoint `43d660602d6524753f68fadabb30b6d421a940c9` then passed all nine push workflows after the runtime-complete packaging fix was present on `main`.

This documentation synchronization deliberately selects the **first post-PR-#69 retained candidate**. The authoritative candidate source is the resulting `main` merge commit that carries `[beta-candidate]`; no earlier artifact or SHA may be reused for the new manual-QA session. The retained candidate workflow must itself finish successfully and its metadata/checksums must bind to that exact merge commit before human testing begins.

This reselection does not complete or waive any manual release gate. Clean-desktop visible WinUI launch/basic regression, normal-user UAC behavior and real cross-process Explorer/Desktop drag-out still require human observations against the exact retained package, and the separate 0.7 physical-writer gate still requires dedicated disposable media. Until that evidence exists, the candidate remains non-public and `0.5.0-beta.1` must not be published as a GitHub Release.

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

The manual-QA evidence format is now **schema v3**. Schema v3 does more than verify that a trusted QA script exists inside the ZIP: initialization, every recorded observation and final verification refuse to continue unless the SHA-256 of the script that is **currently executing** exactly matches `tools/beta-manual-qa.ps1` from that candidate package.

That means manual QA must be launched with the script extracted from the retained candidate itself. Do not use a repository checkout, an older candidate tool or a copied script whose hash differs.

On a clean supported Windows desktop, extract the candidate ZIP and initialize evidence from a normal unelevated interactive PowerShell session using the packaged tool:

```powershell
.\tools\beta-manual-qa.ps1 -Mode new `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -EvidencePath .\beta-manual-qa.json
```

Initialization records the clean-desktop Windows build, process architecture, interactive/elevation state, session id, UAC availability, exact package identity and the exact running QA-tool SHA-256.

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

A record operation fails closed if the ZIP, package checksum, desktop entry point, packaged QA tool, currently running QA tool, Windows build or process architecture no longer matches the evidence baseline. Passing observations additionally require an interactive unelevated session with UAC enabled and a non-service session id. This prevents stale or externally generated evidence from being filled in while a different candidate or a different QA tool is actually under test.

Use `-Mode list` to see the remaining checks. When all observations are complete, verify the evidence against the **same** ZIP/checksum pair with the same packaged tool:

```powershell
.\tools\beta-manual-qa.ps1 -Mode verify `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -EvidencePath .\beta-manual-qa.json
```

Schema-v1 and schema-v2 evidence must be reinitialized and repeated; they are intentionally not auto-upgraded because doing so would fabricate the stronger per-observation and running-tool binding after the fact.

## Final exact-candidate release proof

After the human checklist is genuinely complete, use `scripts/beta-release-proof.ps1` from the matching source state as a final fail-closed proof step. The verifier independently binds:

- the exact 40-character source commit expected for publication,
- `beta-candidate.json` and its SHA-256 sidecar,
- the exact Windows x64 ZIP and its SHA-256 sidecar,
- package manifest version, architecture, desktop entry point and packaged QA-tool hashes,
- the candidate workflow run id,
- completed schema-v3 manual-QA evidence and its SHA-256 sidecar.

It then extracts the supplied candidate and invokes **that candidate's packaged manual-QA verifier** to validate the completed evidence. It does not create a release and cannot manufacture missing human observations.

```powershell
.\scripts\beta-release-proof.ps1 -Mode verify `
  -CandidateMetadataPath .\beta-candidate.json `
  -CandidateMetadataChecksumFile .\beta-candidate.json.sha256 `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -PackageChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -EvidencePath .\beta-manual-qa.json `
  -ExpectedVersion 0.5.0-beta.1 `
  -ExpectedSourceCommit <exact-selected-main-sha>
```

`.github/workflows/beta-release-proof-contract.yml` self-tests the release-proof contract under PowerShell 7 and Windows PowerShell 5.1. A green contract workflow validates the verifier logic; it still does not count as human UAC, WinUI or Explorer drag-out evidence.

## Publication rule

A candidate artifact is not a release. Do not publish a GitHub pre-release until all required items in `docs/BETA-RELEASE.md` are complete, including the package-bound interactive evidence.

When the gate is complete, the public beta must use the exact verified candidate ZIP and checksum rather than silently rebuilding a different package. The eventual tag/release notes must identify the source commit recorded in `beta-candidate.json`, and the release-proof verifier must succeed against the exact candidate/evidence set intended for publication. Preserve the known-limitations truthfulness rules throughout the release.
