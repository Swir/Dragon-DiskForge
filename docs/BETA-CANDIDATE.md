# Dragon DiskForge — Exact Beta Candidate Pipeline

The first public beta target is `0.5.0-beta.1`. This document describes how Dragon DiskForge produces an exact, auditable **non-public** Windows x64 release candidate without prematurely publishing a GitHub Release.

## Why this exists

Committed development metadata is currently `0.5.0-beta.1`, but that version label is **candidate metadata, not release approval**. Manual clean-desktop, normal-user UAC and real cross-process Explorer drag-out checks must be performed against the exact package that would be published. Rebuilding after those checks would invalidate the evidence.

The beta candidate pipeline solves that boundary explicitly:

1. checkout one exact source commit,
2. normalize the disposable CI workspace to the expected `0.5.0-beta.1` metadata and fail closed on an unexpected version prefix,
3. build the WinUI Release x64 application from that workspace,
4. build the normal clean package using the same packaging script as engineering CI,
5. independently verify package version, hashes, icon, package hygiene, CLI, shell helper and packaged manual-QA tool,
6. bind candidate metadata to the exact source commit and ZIP SHA-256,
7. build a hash-bound beta QA kit,
8. retain the ZIP, checksum, candidate metadata and QA kit only when a `main` commit is deliberately marked `[beta-candidate]`,
9. record interactive evidence with the exact manual-QA script carried by that retained candidate,
10. use the release-proof verifier to bind source commit, candidate metadata, package and completed evidence before publication,
11. do **not** create a tag or public GitHub Release until every independent release gate passes.

`scripts/beta-candidate.ps1 -Mode prepare` operates only on the current build workspace. It is deliberately safe to run when the repository is already on the expected beta suffix; it is not evidence that a public release exists.

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

The retained artifact contains the package, checksum, exact candidate metadata and the complete hash-bound beta QA kit, including:

```text
DragonDiskForge-win-x64.zip
DragonDiskForge-win-x64.zip.sha256
beta-candidate.json
beta-candidate.json.sha256
beta-qa-kit.json
beta-qa-kit.json.sha256
beta-qa-kit-verify.ps1
beta-qa-session.ps1
BETA-QA-KIT.md
BETA-MANUAL-VALIDATION.md
```

`beta-candidate.json` records the exact source commit, workflow run id, beta version, architecture, package SHA-256, package-manifest schema, desktop entry-point SHA-256 and packaged manual-QA-tool SHA-256. `publicRelease` is explicitly `false`.

A green beta-candidate workflow proves that the source commit can produce a correctly versioned and independently verified `0.5.0-beta.1` package. It does **not** prove the remaining interactive Windows checks and does **not** authorize publication. A retained artifact is only a selected engineering candidate; it is still not a release.

## Selecting one retained candidate

Select a retained candidate only after all engineering changes intended for that candidate have passed the complete exact-head PR gate. The final merge commit on `main` must contain `[beta-candidate]`; the subsequent `main` workflow is then the authoritative build that retains the ZIP, checksum, metadata and QA kit.

The marker is a retention decision, not a release approval. Do not create empty/no-op commits solely to obtain an artifact, and do not mark ordinary hourly development commits. Pair candidate selection with a real release-process, packaging, QA-tool or documentation synchronization change so the selected source state is intentional and auditable.

Before starting manual QA, confirm that the retained artifact's `beta-candidate.json` names the exact selected `main` commit and that its package SHA-256 matches `DragonDiskForge-win-x64.zip.sha256`. If any product code, packaging, release tooling or beta-gate behavior changes afterward, select a new retained candidate and repeat package-bound manual evidence rather than carrying observations forward.

### Current retained checkpoint

The authoritative retained candidate is **Beta Candidate run #95 (`35305773840`)**, artifact `DragonDiskForge-0.5.0-beta.1-win-x64-candidate-35305773840`, built from `main` commit `89ab37f6c221ab19d44bc4c3b83f38241bb4d9d3`. The nested package SHA-256 is `5aca974660703ab423237a7e34e29f7210ff0e9eaa85bab526ac972d2a1591da`; the GitHub artifact SHA-256 is `f3cdf99f9d2503389d4f9ed6b000ba7772d3d10bfed74a26ccd9c7bcba4732b2`.

This candidate was intentionally reselected after PR #82 strengthened the packaged manual-QA path so passing observations are bound to the same interactive Windows desktop session as the evidence baseline. The exact PR head `357bda52a2bfa18c49ba1bb241b9c1596ddd07c2` passed the required pull-request workflows; the selected `main` commit then passed Build #488 and Beta Candidate #95. Independent artifact read-back verified every supplied SHA-256 sidecar, package manifest schema 5, x64 architecture, `.NET=self-contained`, `WindowsAppSDK=self-contained`, `VisualCpp=app-local`, the desktop entry-point hash and the packaged manual-QA-tool hash.

The authoritative machine-readable record is [`retained-beta-candidate.json`](retained-beta-candidate.json). It deliberately keeps `publicRelease=false` and `betaReady=false`.

Earlier candidate checkpoints remain historical evidence only. In particular, the pre-PR-#69 candidate that failed visible startup testing must not be reused, and candidates produced before the PR #82 session-binding change must not be mixed with the current manual-QA evidence path.

This reselection does not complete or waive any manual release gate. Clean-desktop visible WinUI launch/basic regression, normal-user UAC behavior and real cross-process Explorer/Desktop drag-out still require human observations against the exact retained package, and the separate 0.7 physical-writer gate still requires dedicated disposable media. Until that evidence exists, the candidate remains non-public and `0.5.0-beta.1` must not be published as a GitHub Release.

## Candidate contract script

`scripts/beta-candidate.ps1` has three modes.

### Self-test

```powershell
.\scripts\beta-candidate.ps1 -Mode self-test
```

This verifies workspace beta-suffix normalization, fail-closed version-prefix handling and exact source-commit validation. CI executes the self-test in both PowerShell 7 and Windows PowerShell 5.1.

### Prepare

```powershell
.\scripts\beta-candidate.ps1 -Mode prepare
```

This normalizes the current workspace copy of `Directory.Build.props` to `0.5.0-beta.1` only after confirming the expected `0.5.0` version prefix. It is intended for disposable build workspaces. Do not use it as a release-approval mechanism.

### Metadata

After the package has been built and verified:

```powershell
.\scripts\beta-candidate.ps1 -Mode metadata `
  -SourceCommit <40-character-commit-sha> `
  -WorkflowRunId <run-id>
```

Metadata mode re-runs the clean-package verifier before writing candidate metadata. It fails closed on an invalid source SHA, package/checksum mismatch, wrong package version, wrong architecture or package manifest older than the beta-QA contract.

## Manual QA must use one exact candidate

Choose one successful **retained** candidate artifact and keep its files together. Do not rename only one file without updating its checksum sidecar contract.

The manual-QA evidence format is **schema v3**. Schema v3 does more than verify that a trusted QA script exists inside the ZIP: initialization, every recorded observation and final verification refuse to continue unless the SHA-256 of the script that is **currently executing** exactly matches `tools/beta-manual-qa.ps1` from that candidate package. The current session hardening additionally requires passing observations to remain bound to the same interactive Windows desktop session recorded by the evidence baseline.

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

A record operation fails closed if the ZIP, package checksum, desktop entry point, packaged QA tool, currently running QA tool, Windows build, process architecture or desktop session no longer matches the evidence baseline. Passing observations additionally require an interactive unelevated session with UAC enabled and a non-service session id. This prevents stale or externally generated evidence from being filled in while a different candidate, different QA tool or different desktop session is actually under test.

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
