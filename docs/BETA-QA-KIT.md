# Dragon DiskForge — Retained Beta QA Kit

The retained `0.5.0-beta.1` candidate is an engineering test artifact, not a public release. Its purpose is to let the remaining interactive Windows checks run against one exact package identity without requiring a repository checkout to obtain the verifier, session helper or manual checklist.

## What the retained artifact contains

An explicitly selected `[beta-candidate]` build retains these files together:

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

QA-kit manifest schema v2 binds the exact source commit/workflow run, candidate package SHA-256, candidate-metadata SHA-256, desktop entry-point SHA-256, packaged manual-QA tool SHA-256, standalone verifier SHA-256, external session-helper SHA-256, start-guide SHA-256 and manual-validation guide SHA-256. Every retained companion file also carries a conventional SHA-256 sidecar.

The standalone verifier verifies its own filename and SHA-256 against schema v2 before trusting the rest of the kit. It then validates the kit manifest and all candidate/helper/document bindings without a repository checkout. This prevents the final release-proof path from silently accepting a different verifier or an unbound start guide alongside an otherwise valid candidate.

## Verify before interactive testing

Run from an ordinary PowerShell prompt in the extracted retained artifact directory:

```powershell
.\beta-qa-kit-verify.ps1 -ManifestPath .\beta-qa-kit.json
```

The verifier fails closed when its own hash does not match the manifest, a sidecar is malformed, a companion file hash does not match, a referenced filename attempts path traversal, the source commit/workflow/package identity disagrees with `beta-candidate.json`, or the package/entry/manual-QA identities are inconsistent.

A successful verifier result proves only that the retained test inputs are internally consistent with the selected GitHub Actions candidate. It does **not** prove that the WinUI application visibly launched, that UAC behaved correctly, or that a real cross-process Explorer drag gesture succeeded.

## Prepare the Windows desktop session

After the standalone kit verification passes, use the retained external helper from an interactive **unelevated** Windows session with UAC enabled:

```powershell
.\beta-qa-session.ps1 -Mode prepare `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

The helper independently re-verifies the candidate package/runtime completeness, initializes evidence through the packaged hash-bound `tools/beta-manual-qa.ps1`, performs only a non-authoritative process-liveness preflight and opens an isolated Explorer drop target. It never marks a human gate as passed.

Follow `BETA-MANUAL-VALIDATION.md` for the exact clean-launch/basic-regression, normal-user UAC and Explorer drag-out observations. Passing observations still require explicit human confirmation through the packaged QA tool.

## CI contract

The Beta Candidate workflow runs the QA-kit builder self-test under PowerShell 7 and Windows PowerShell 5.1. It then builds and verifies the real package/candidate metadata, creates the schema-v2 QA kit, and executes the retained standalone verifier under both PowerShell engines.

The independent Beta Release Proof contract also self-tests that final release proof rejects a mismatched/tampered QA-kit companion and requires the QA kit to match the exact candidate source, workflow, package, candidate metadata and shipped entry-point hashes before human QA evidence can complete release proof.

A verification-only pull-request run does not retain the large candidate artifact. The artifact is retained only on an explicitly selected `main` commit whose message contains `[beta-candidate]`.

## Release boundary

This QA kit is release-preparation hardening only. It does not change project completion, milestone `0.9` completion or public beta readiness. `0.5.0-beta.1` may be published only after every required interactive gate in [`BETA-RELEASE.md`](BETA-RELEASE.md) is genuinely completed and final release proof succeeds.
