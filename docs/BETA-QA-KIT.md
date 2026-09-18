# Dragon DiskForge — Retained Beta QA Kit

The retained `0.5.0-beta.1` candidate is an engineering test artifact, not a public release. Its purpose is to let the remaining interactive Windows checks run against one exact package identity without requiring a repository checkout to obtain the verifier, session helper, manual checklist or objective desktop-witness helper.

## What the retained artifact contains

An explicitly selected `[beta-candidate]` build retains the exact candidate package and core QA kit together with a separate hash-bound desktop-witness companion:

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
- `beta-qa-witness-kit.json`
- `beta-qa-witness-kit.json.sha256`
- `beta-qa-witness-kit-verify.ps1`
- `beta-qa-witness-kit-verify.ps1.sha256`
- `beta-qa-desktop-witness.ps1`
- `beta-qa-desktop-witness.ps1.sha256`
- `BETA-QA-DESKTOP-WITNESS.md`
- `BETA-QA-DESKTOP-WITNESS.md.sha256`

Core QA-kit manifest schema v2 binds the exact source commit/workflow run, candidate package SHA-256, candidate-metadata SHA-256, desktop entry-point SHA-256, packaged manual-QA tool SHA-256, standalone verifier SHA-256, external session-helper SHA-256, start-guide SHA-256 and manual-validation guide SHA-256. Every retained companion file carries a conventional SHA-256 sidecar.

The separate desktop-witness companion uses schema v1. It binds its standalone verifier, desktop-witness helper and witness guide to the exact core QA-kit manifest SHA-256, source commit, workflow run and candidate package SHA-256. It permanently carries `humanGateClaimed=false` and `publicRelease=false`; it cannot elevate objective supporting evidence into a human release-gate claim.

## Verify before interactive testing

Run from an ordinary PowerShell prompt in the extracted retained artifact directory:

```powershell
.\beta-qa-kit-verify.ps1 -ManifestPath .\beta-qa-kit.json
.\beta-qa-witness-kit-verify.ps1 -ManifestPath .\beta-qa-witness-kit.json
```

The core verifier checks its own filename/hash, all core companion files, source/workflow/package identity and candidate bindings. The witness-companion verifier independently checks its own filename/hash, the core QA-kit manifest hash, the exact desktop-witness helper and guide, source/workflow/package identity and the permanent false release/human-gate claims.

A successful result proves only that the retained test inputs are internally consistent with the selected GitHub Actions candidate. It does **not** prove that the WinUI application visibly launched, that UAC behaved correctly, that the user performed a real cross-process Explorer drag gesture or that any manual beta gate passed.

## Prepare the Windows desktop session

After both standalone verifiers pass, use the retained session helper from an interactive **unelevated** Windows session with UAC enabled:

```powershell
.\beta-qa-session.ps1 -Mode prepare `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

The helper independently re-verifies the candidate package/runtime completeness, initializes evidence through the packaged hash-bound `tools/beta-manual-qa.ps1`, performs only a non-authoritative process-liveness preflight and opens an isolated Explorer drop target. It never marks a human gate as passed.

Follow `BETA-MANUAL-VALIDATION.md` for the exact clean-launch/basic-regression, normal-user UAC and Explorer drag-out observations. Passing observations still require explicit human confirmation through the packaged QA tool.

## Objective desktop witness without a repository checkout

The retained `beta-qa-desktop-witness.ps1` can add objective supporting evidence around the drag-out test. It binds to the prepared session and exact candidate, requires the recorded app process to own a visible top-level window, captures an empty Explorer-target baseline, and later records a bounded privacy-preserving hash snapshot of the destination tree.

See `BETA-QA-DESKTOP-WITNESS.md` for the exact `baseline → real human gesture → observe → verify` sequence and safety boundaries. The witness permanently carries `humanGateClaimed=false`: it cannot prove that the user dragged from Dragon DiskForge, that Windows negotiated Copy semantics, that the source remained unchanged, that UAC behaved correctly or that any beta gate passed. Those facts remain explicit human observations recorded through the packaged QA tool.

The witness companion is deliberately a separate manifest rather than a silent mutation of core QA-kit schema v2. That keeps the established candidate/QA-kit contract stable while making the new objective witness independently hash-bound and usable from the retained artifact itself.

## Preserve evidence before deleting the session workspace

The retained QA kit remains sufficient to perform and record the human observations without a repository checkout. Release operators using a repository checkout can additionally preserve a compact evidence snapshot with `scripts/beta-qa-archive.ps1` before running session cleanup. See `BETA-QA-EVIDENCE-ARCHIVE.md`.

The archive utility does not change the retained candidate identity and cannot mark any human gate as passed. The desktop witness is supporting evidence and remains separate from authoritative packaged manual-QA evidence unless it is deliberately preserved for audit before cleanup.

## CI contract

The Beta Candidate workflow runs the core QA-kit builder self-test and desktop-witness-companion self-test under PowerShell 7 and Windows PowerShell 5.1. It then builds and verifies the real package/candidate metadata, creates the schema-v2 core QA kit, executes the retained standalone core verifier under both PowerShell engines, creates the schema-v1 witness companion, and executes its retained standalone verifier under both engines.

A separate Beta QA Witness Kit Contract provides a fast deterministic gate for the companion builder/verifier, including tamper rejection and fail-closed rejection of `humanGateClaimed=true`. The Beta Manual QA Contract continues to self-test the actual desktop witness logic under both PowerShell engines.

The independent Beta Release Proof contract continues to require the exact candidate source, workflow, package, core QA kit and packaged human-confirmed evidence. The objective witness companion strengthens and simplifies the real desktop QA session, but it does not replace the authoritative human evidence or weaken any final release proof requirement.

A verification-only pull-request run does not retain the large candidate artifact. The artifact is retained only on an explicitly selected `main` commit whose message contains `[beta-candidate]`.

## Release boundary

This QA kit is release-preparation hardening only. It does not change project completion, milestone `0.9` completion or public beta readiness. `0.5.0-beta.1` may be published only after every required interactive gate in `BETA-RELEASE.md` is genuinely completed and final release proof succeeds.