# Dragon DiskForge — Manual Gate Status Verifier

`beta-manual-gate-status.ps1` classifies the existing schema-v3 interactive beta-QA evidence into the three canonical groups used by the current beta gate: clean desktop, normal-user UAC and real Explorer drag-out.

It is a **read-only verifier**. It does not record observations, alter evidence, mark a roadmap checkbox, publish a release or replace `tools/beta-manual-qa.ps1`. Human observations remain authoritative only when they were recorded by the exact QA tool inside the retained candidate package.

The Beta Candidate workflow also stages a standalone copy of this verifier plus this guide in every explicitly retained candidate artifact. Both files receive SHA-256 sidecars and the staged verifier is self-tested under PowerShell 7 and Windows PowerShell 5.1 before artifact upload. This removes the repository-checkout requirement for checking partial manual-gate progress; it does **not** make any human gate automatic.

## Why this exists

The current 0.9 roadmap has two independent open deliverables: normal-user UAC validation and real cross-process Explorer drag-out validation. The release gate also requires clean-desktop launch/basic regression. The existing packaged manual-QA verifier intentionally requires all 14 observations before declaring the full interactive QA set complete.

This companion allows one group to be verified independently after its real human observations are complete, without pretending the other groups or the public beta are ready.

## Inputs

Use the exact retained candidate package, its SHA-256 sidecar and the schema-v3 evidence file produced by the packaged `tools/beta-manual-qa.ps1`:

```text
DragonDiskForge-win-x64.zip
DragonDiskForge-win-x64.zip.sha256
beta-manual-qa.json
beta-manual-qa.json.sha256
```

The verifier rechecks package checksum/manifest/version/x64 identity, desktop entry-point SHA-256, packaged manual-QA-tool SHA-256, evidence sidecar, exact package/tool binding, baseline session/UAC state and every passing observation's human confirmation, note, timestamp and same-session/build/architecture bindings.

## Retained-candidate use without a repository checkout

From the extracted retained candidate artifact directory, first self-test the staged verifier:

```powershell
.\beta-manual-gate-status.ps1 -Mode self-test
```

The retained artifact also contains `beta-manual-gate-status.ps1.sha256` and `BETA-MANUAL-GATE-STATUS.md.sha256`. They bind the staged files to the exact artifact contents; GitHub Actions additionally records the retained artifact digest and source commit. The standalone verifier remains a reporting/checking companion only and cannot create passing observations.

## Show all group states

From a repository checkout use `scripts\beta-manual-gate-status.ps1`; from an extracted retained candidate use the root-level `beta-manual-gate-status.ps1` copy:

```powershell
.\beta-manual-gate-status.ps1 `
  -Mode status `
  -EvidencePath .\beta-manual-qa.json `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

The output reports `COMPLETE`, `PENDING` or `FAILED` separately for:

- `desktop` — 2 checks
- `uac` — 6 checks
- `drag` — 6 checks

## Show the next required observation

To avoid hunting through all 14 checks, ask the standalone verifier for the highest-priority unresolved item:

```powershell
.\beta-manual-gate-status.ps1 `
  -Mode next `
  -EvidencePath .\beta-manual-qa.json `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

`next` never records or auto-passes anything. It verifies the exact package/evidence binding first, prioritizes an existing failed observation ahead of pending observations, then prints one canonical check ID, its acceptance statement and an example record command for the exact packaged `tools\beta-manual-qa.ps1`.

A human must still perform the action, explicitly confirm the result and provide a real observation note in the same unelevated Windows session. The normal `status` mode also lists every unresolved check with its current state and acceptance statement.

## Verify one canonical group

```powershell
.\beta-manual-gate-status.ps1 `
  -Mode verify-group `
  -Group uac `
  -EvidencePath .\beta-manual-qa.json `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

Replace `uac` with `desktop` or `drag` as needed. Verification fails closed unless every canonical check in that group is a valid human-confirmed `pass` bound to the same exact package and desktop session.

A successful group verification is **not** a public-beta authorization. `docs/BETA-RELEASE.md` remains authoritative and the complete release proof still requires all applicable interactive, package, publication and post-release gates.

## Contract test

Repository checkout:

```powershell
.\scripts\beta-manual-gate-status.ps1 -Mode self-test
```

Retained candidate artifact:

```powershell
.\beta-manual-gate-status.ps1 -Mode self-test
```

The dedicated GitHub Actions contract runs the self-test under PowerShell 7 and Windows PowerShell 5.1. It covers complete groups plus fail-closed pending evidence, short notes, cross-session observations, duplicate checks, package mismatch and missing human confirmation. The Beta Candidate workflow repeats the self-test against the staged retained copy before it can upload a selected candidate artifact.
