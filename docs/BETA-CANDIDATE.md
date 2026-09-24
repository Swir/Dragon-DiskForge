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
7. build the hash-bound core beta QA kit plus the live-session, desktop-witness, portable evidence-archive and real-Explorer witness companions,
8. retain the ZIP, checksum, candidate metadata and all hash-bound QA companions only when the fail-closed retention policy explicitly selects an exact `main` run,
9. record interactive evidence with the exact manual-QA script carried by that retained candidate while the live-session companion verifies session continuity without auto-passing any human gate,
10. use the release-proof verifier to bind source commit, candidate metadata, package and completed evidence before publication,
11. do **not** create a tag or public GitHub Release until every independent release gate passes.

`scripts/beta-candidate.ps1 -Mode prepare` operates only on the current build workspace. It is deliberately safe to run when the repository is already on the expected beta suffix; it is not evidence that a public release exists.

## Workflow

`.github/workflows/beta-candidate.yml` runs on pull requests and on `main` pushes, and it can also be invoked manually. Every run builds and verifies the exact `0.5.0-beta.1` package contract, but routine runs are verification-only and intentionally do not retain another large artifact.

The fail-closed retention policy permits retention only for an exact `main` run selected by one of two deliberate mechanisms:

1. a `main` push whose commit message contains:

```text
[beta-candidate]
```

2. a manual `workflow_dispatch` on `main` with `retain_candidate=true`.

Pull requests, non-`main` branches, ordinary pushes and manual verification-only dispatches never retain a candidate. `scripts/beta-candidate-retention-policy.ps1` self-tests this decision matrix under PowerShell 7 and Windows PowerShell 5.1.

Selected candidates are retained for 14 days and use a name like:

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
beta-qa-kit-verify.ps1.sha256
beta-qa-session.ps1
beta-qa-session.ps1.sha256
BETA-QA-KIT.md
BETA-QA-KIT.md.sha256
BETA-MANUAL-VALIDATION.md
BETA-MANUAL-VALIDATION.md.sha256
beta-qa-live-kit.json
beta-qa-live-kit.json.sha256
beta-qa-live-kit-verify.ps1
beta-qa-live-kit-verify.ps1.sha256
beta-qa-live-session.ps1
beta-qa-live-session.ps1.sha256
BETA-QA-LIVE-SESSION.md
BETA-QA-LIVE-SESSION.md.sha256
beta-qa-witness-kit.json
beta-qa-witness-kit.json.sha256
beta-qa-witness-kit-verify.ps1
beta-qa-witness-kit-verify.ps1.sha256
beta-qa-desktop-witness.ps1
beta-qa-desktop-witness.ps1.sha256
BETA-QA-DESKTOP-WITNESS.md
BETA-QA-DESKTOP-WITNESS.md.sha256
beta-qa-archive-kit.json
beta-qa-archive-kit.json.sha256
beta-qa-archive-kit-verify.ps1
beta-qa-archive-kit-verify.ps1.sha256
beta-qa-archive.ps1
beta-qa-archive.ps1.sha256
BETA-QA-EVIDENCE-ARCHIVE.md
BETA-QA-EVIDENCE-ARCHIVE.md.sha256
beta-qa-explorer-kit.json
beta-qa-explorer-kit.json.sha256
beta-qa-explorer-kit.ps1
beta-qa-explorer-kit.ps1.sha256
beta-qa-explorer-witness.ps1
beta-qa-explorer-witness.ps1.sha256
BETA-QA-EXPLORER-WITNESS.md
BETA-QA-EXPLORER-WITNESS.md.sha256
installer/DragonDiskForge-0.5.0-beta.1-win-x64-setup.exe
installer/DragonDiskForge-0.5.0-beta.1-win-x64-setup.exe.sha256
```

`beta-candidate.json` records the exact source commit, workflow run id, beta version, architecture, package SHA-256, package-manifest schema, desktop entry-point SHA-256 and packaged manual-QA-tool SHA-256. Canonical `retained-beta-candidate.json` additionally binds the versioned per-user installer filename and SHA-256. `publicRelease` is explicitly `false`.

A green beta-candidate workflow proves that the source commit can produce a correctly versioned and independently verified `0.5.0-beta.1` package. It does **not** prove the remaining interactive Windows checks and does **not** authorize publication. A retained artifact is only a selected engineering candidate; it is still not a release.

## Selecting one retained candidate

Select a retained candidate only after all engineering changes intended for that candidate have passed the complete exact-head gate. The preferred automatic route is a meaningful final `main` commit carrying `[beta-candidate]`. When the exact desired `main` commit is already present and green, the manual `workflow_dispatch` route may retain that same commit without manufacturing a no-op source change.

The marker or manual dispatch is a retention decision, not a release approval. Do not create empty/no-op commits solely to obtain an artifact, and do not mark ordinary hourly development commits. Pair candidate selection with a real release-process, packaging, QA-tool or documentation synchronization decision so the selected source state is intentional and auditable.

Before starting manual QA, confirm that the retained artifact's `beta-candidate.json` names the exact selected `main` commit and that its package SHA-256 matches `DragonDiskForge-win-x64.zip.sha256`. If any product code, packaging, release tooling or beta-gate behavior changes afterward, select a new retained candidate and repeat package-bound manual evidence rather than carrying observations forward.

### Current retained checkpoint

The authoritative retained candidate is **Beta Candidate run #278 (`36045422063`)**, artifact `DragonDiskForge-0.5.0-beta.1-win-x64-candidate-36045422063`, built from `main` commit `0b7ff27adc97790508f23023234ebeff1412fe61`. The nested package SHA-256 is `e229760388eb6ab3108b6f1fd3d23428344cddc7ba580dd9760507a0a4074e55`; the per-user installer `DragonDiskForge-0.5.0-beta.1-win-x64-setup.exe` is bound as `1d198d927d83b88f6aeb3e351ebff37685283148ac32d1464adae36277cfb31f`; the GitHub artifact SHA-256 is `9adefec4d0c60e10de5709b9c4e41741c3eb0844c36c3fbfa26b592a33ffe389`.

Independent artifact read-back verified every supplied SHA-256 sidecar, package manifest schema 6, x64 architecture, `.NET=self-contained`, `WindowsAppSDK=self-contained`, `VisualCpp=app-local`, exactly one desktop executable, no PDB payloads, the desktop entry-point hash `50b5da1e2f675819aead01ba07e055790d53a7fa69d6d47a1418f4cc5bf9d039`, the packaged manual-QA-tool hash `525f632943ece5e2ae72ca350015bab49d2a8149a54aa98e9a8e1fd1788a6d94`, the packaged UAC-witness-tool hash `19897638afa6015b94355927ae07001b9ae6750242e510339966f0a2d288e431` and the packaged UAC before/after pair-verifier hash `e3e7f08cb278bf35e38bc425e5b4006f9da90da18a8bef2e718363b190833500`.

Canonical retained evidence remains schema v2. It is installer-bound to the verified per-user setup executable, witness-bound to the packaged desktop witness companion, archive-bound to the portable evidence-archive companion, live-session-bound to the package-specific live continuity manifest/verifier/helper/guide, Explorer-witness-bound to the package-specific verifier/helper/guide used to capture real File Explorer process/window/path evidence, and package-UAC-witness-bound to the exact normal-user UAC helper plus before/after pair verifier shipped inside the retained ZIP. None of these bindings claims a human gate.

The authoritative machine-readable record is [`retained-beta-candidate.json`](retained-beta-candidate.json). It deliberately keeps `publicRelease=false` and `betaReady=false`.

Earlier candidate checkpoints remain historical evidence only. Candidates that predate the same-session binding, witness binding, portable archive kit, explicit retention policy, retained-candidate currency guard, live-session continuity kit, process-start binding, package-bound Explorer witness companion, package-bound UAC witness or package-bound UAC pair verifier must not be mixed with the current manual-QA evidence path.

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

That means manual QA must be launched with the script extracted from the retained candidate itself. Do not use a repository checkout, an older candidate tool or a copied script whose hash differs. The retained candidate's `beta-qa-live-session.ps1` companion may be used to prepare and continuously verify the exact package/session context, but it cannot record or auto-pass any human observation.

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
