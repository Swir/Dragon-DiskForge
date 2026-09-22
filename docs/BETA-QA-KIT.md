# Dragon DiskForge — Retained Beta QA Kit

The retained `0.5.0-beta.1` candidate is an engineering test artifact, not a public release. Its purpose is to let the remaining interactive Windows checks run against one exact package identity without requiring a repository checkout to obtain the verifier, session helper, manual checklist, partial-gate status verifier, objective desktop-witness helper or evidence-archive tooling.

## What the retained artifact contains

An explicitly selected `[beta-candidate]` build retains the exact candidate package and core QA kit together with the standalone manual-gate status companion and separate hash-bound desktop-witness and evidence-archive companions:

- `DragonDiskForge-win-x64.zip`
- `DragonDiskForge-win-x64.zip.sha256`
- `beta-candidate.json`
- `beta-candidate.json.sha256`
- `beta-qa-kit.json`
- `beta-qa-kit.json.sha256`
- `beta-qa-kit-verify.ps1`
- `beta-qa-kit-verify.ps1.sha256`
- `beta-qa-session.ps1`
- `beta-qa-session.ps1.sha256`
- `BETA-QA-KIT.md`
- `BETA-QA-KIT.md.sha256`
- `BETA-MANUAL-VALIDATION.md`
- `BETA-MANUAL-VALIDATION.md.sha256`
- `beta-manual-gate-status.ps1`
- `beta-manual-gate-status.ps1.sha256`
- `BETA-MANUAL-GATE-STATUS.md`
- `BETA-MANUAL-GATE-STATUS.md.sha256`
- `beta-qa-witness-kit.json`
- `beta-qa-witness-kit.json.sha256`
- `beta-qa-witness-kit-verify.ps1`
- `beta-qa-witness-kit-verify.ps1.sha256`
- `beta-qa-desktop-witness.ps1`
- `beta-qa-desktop-witness.ps1.sha256`
- `BETA-QA-DESKTOP-WITNESS.md`
- `BETA-QA-DESKTOP-WITNESS.md.sha256`
- `beta-qa-archive-kit.json`
- `beta-qa-archive-kit.json.sha256`
- `beta-qa-archive-kit-verify.ps1`
- `beta-qa-archive-kit-verify.ps1.sha256`
- `beta-qa-archive.ps1`
- `beta-qa-archive.ps1.sha256`
- `BETA-QA-EVIDENCE-ARCHIVE.md`
- `BETA-QA-EVIDENCE-ARCHIVE.md.sha256`

Core QA-kit manifest schema v2 binds the exact source commit/workflow run, candidate package SHA-256, candidate-metadata SHA-256, desktop entry-point SHA-256, packaged manual-QA tool SHA-256, standalone verifier SHA-256, external session-helper SHA-256, start-guide SHA-256 and manual-validation guide SHA-256. Every retained companion file carries a conventional SHA-256 sidecar.

The manual-gate status companion is staged from the same exact candidate source after the core kit is built. Its script and guide each receive a SHA-256 sidecar, and the staged script runs its fail-closed self-test under both PowerShell 7 and Windows PowerShell 5.1 before a selected artifact can be uploaded. It is intentionally a read-only convenience companion rather than a new source of truth: only the packaged `tools/beta-manual-qa.ps1` may record authoritative human observations.

The separate desktop-witness companion uses schema v1. It binds its standalone verifier, desktop-witness helper and witness guide to the exact core QA-kit manifest SHA-256, source commit, workflow run and candidate package SHA-256. It permanently carries `humanGateClaimed=false` and `publicRelease=false`; it cannot elevate objective supporting evidence into a human release-gate claim.

The portable evidence-archive companion uses its own schema v1. It binds the archive verifier, `beta-qa-archive.ps1` and archive guide to the exact core QA-kit manifest, desktop-witness manifest, source commit, workflow run and candidate package SHA-256. It also permanently carries `humanGateClaimed=false` and `publicRelease=false`.

## Verify before interactive testing

Run from an ordinary PowerShell prompt in the extracted retained artifact directory:

```powershell
.\beta-qa-kit-verify.ps1 -ManifestPath .\beta-qa-kit.json
.\beta-manual-gate-status.ps1 -Mode self-test
.\beta-qa-witness-kit-verify.ps1 -ManifestPath .\beta-qa-witness-kit.json
.\beta-qa-archive-kit-verify.ps1 -ManifestPath .\beta-qa-archive-kit.json
```

The core verifier checks its own filename/hash, all core companion files, source/workflow/package identity and candidate bindings. The manual-gate status self-test proves the staged read-only classifier still rejects incomplete, mismatched or non-human-confirmed evidence. The witness-companion verifier independently checks its own filename/hash, the core QA-kit manifest hash, the exact desktop-witness helper and guide, source/workflow/package identity and the permanent false release/human-gate claims. The archive-kit verifier independently checks its own filename/hash, both upstream manifests, the archive helper and guide, the same source/workflow/package identity and the same fail-closed release/human-gate boundary.

A successful result proves only that the retained test inputs are internally consistent with the selected GitHub Actions candidate. It does **not** prove that the WinUI application visibly launched, that UAC behaved correctly, that the user performed a real cross-process Explorer drag gesture or that any manual beta gate passed.

## Prepare the Windows desktop session

After all standalone verifiers pass, use the retained session helper from an interactive **unelevated** Windows session with UAC enabled:

```powershell
.\beta-qa-session.ps1 -Mode prepare `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

The helper independently re-verifies the candidate package/runtime completeness, initializes evidence through the packaged hash-bound `tools/beta-manual-qa.ps1`, performs only a non-authoritative process-liveness preflight and opens an isolated Explorer drop target. It never marks a human gate as passed.

Follow `BETA-MANUAL-VALIDATION.md` for the exact clean-launch/basic-regression, normal-user UAC and Explorer drag-out observations. Passing observations still require explicit human confirmation through the packaged QA tool.

After real observations have been recorded, `beta-manual-gate-status.ps1` can report the three canonical groups independently without waiting for all 14 checks:

```powershell
.\beta-manual-gate-status.ps1 `
  -Mode status `
  -EvidencePath .\beta-manual-qa.json `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

Use `-Mode verify-group -Group desktop`, `uac` or `drag` only after the corresponding real observations are expected to be complete. `COMPLETE` for one group does not authorize a beta release and does not change roadmap progress by itself. See `BETA-MANUAL-GATE-STATUS.md`.

## Objective desktop witness without a repository checkout

The retained `beta-qa-desktop-witness.ps1` can add objective supporting evidence around the drag-out test. It binds to the prepared session and exact candidate, requires the recorded app process to own a visible top-level window, captures an empty Explorer-target baseline, and later records a bounded privacy-preserving hash snapshot of the destination tree.

See `BETA-QA-DESKTOP-WITNESS.md` for the exact `baseline → real human gesture → observe → verify` sequence and safety boundaries. The witness permanently carries `humanGateClaimed=false`: it cannot prove that the user dragged from Dragon DiskForge, that Windows negotiated Copy semantics, that the source remained unchanged, that UAC behaved correctly or that any beta gate passed. Those facts remain explicit human observations recorded through the packaged QA tool.

The witness companion is deliberately a separate manifest rather than a silent mutation of core QA-kit schema v2. That keeps the established candidate/QA-kit contract stable while making the objective witness independently hash-bound and usable from the retained artifact itself.

## Preserve evidence before deleting the session workspace

Evidence preservation no longer requires a repository checkout. After completing or pausing an interactive QA session, use the retained archive helper **before** deleting the disposable workspace:

```powershell
.\beta-qa-archive.ps1 -Mode archive `
  -WorkspacePath <session-workspace> `
  -ArchivePath <path-outside-session-workspace>

.\beta-qa-archive.ps1 -Mode verify `
  -ArchivePath <path-outside-session-workspace>
```

Read `BETA-QA-EVIDENCE-ARCHIVE.md` for the exact preserve → verify → cleanup sequence and default paths. The archive utility does not change the retained candidate identity, does not duplicate the large candidate ZIP and cannot mark any human gate as passed. The desktop witness is supporting evidence and remains separate from authoritative packaged manual-QA evidence unless it is deliberately preserved for audit before cleanup.

## CI contract

The Beta Candidate workflow runs the core QA-kit builder self-test, desktop-witness-companion self-test and portable archive-kit self-test under PowerShell 7 and Windows PowerShell 5.1. It then builds and verifies the real package/candidate metadata, creates the schema-v2 core QA kit, executes the retained standalone core verifier under both PowerShell engines, stages the read-only manual-gate status verifier/guide with SHA-256 sidecars and self-tests the staged verifier under both PowerShell engines, creates the schema-v1 witness companion and verifies it under both engines, then creates the portable archive companion and executes its retained standalone verifier under both engines.

A separate Beta Manual Gate Status Contract exercises the classifier under both PowerShell engines, including fail-closed pending evidence, package/session mismatch and missing human confirmation. A separate Beta QA Witness Kit Contract provides a fast deterministic gate for the witness builder/verifier, including tamper rejection and fail-closed rejection of `humanGateClaimed=true`. A separate Beta QA Archive Kit Contract does the same for the portable archive companion, including tampered-helper and candidate-identity rejection. The Beta Manual QA Contract continues to self-test the actual desktop witness logic under both PowerShell engines.

The independent Beta Release Proof contract continues to require the exact candidate source, workflow, package, core QA kit and packaged human-confirmed evidence. The manual-gate status, witness and portable archive companions strengthen and simplify the real desktop QA session, but none replaces the authoritative human evidence or weakens any final release proof requirement.

A verification-only pull-request run does not retain the large candidate artifact. The artifact is retained only on an explicitly selected `main` commit whose message contains `[beta-candidate]` or an explicit retained workflow-dispatch request accepted by the fail-closed retention policy.

## Release boundary

This QA kit is release-preparation hardening only. It does not change project completion, milestone `0.9` completion or public beta readiness. `0.5.0-beta.1` may be published only after every required interactive gate in `BETA-RELEASE.md` is genuinely completed and final release proof succeeds.
