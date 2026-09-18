# Dragon DiskForge — Portable Beta QA Live Session

The retained `0.5.0-beta.1` candidate must keep the final human observations tied to one exact package and one real interactive Windows desktop session. The live-session companion exists so that continuity check travels with the retained candidate instead of depending on a later repository checkout.

## What the companion proves

The hash-bound live-session companion contains:

- `beta-qa-live-kit.json` + SHA-256 sidecar
- `beta-qa-live-kit-verify.ps1` + SHA-256 sidecar
- `beta-qa-live-session.ps1` + SHA-256 sidecar
- `BETA-QA-LIVE-SESSION.md` + SHA-256 sidecar

Its manifest is bound to the core beta QA kit, exact source commit, workflow run, candidate package SHA-256, standalone verifier, live-session helper and this guide. It permanently records `humanGateClaimed=false` and `publicRelease=false`.

This is supporting integrity evidence only. It does not prove that a person completed the UAC checklist, performed a real Explorer drag gesture, or passed any release gate.

## Verify the retained companion

From the extracted retained-candidate artifact directory, first verify the core QA kit and then the live-session companion:

```powershell
.\beta-qa-kit-verify.ps1 -ManifestPath .\beta-qa-kit.json
.\beta-qa-live-kit-verify.ps1 -ManifestPath .\beta-qa-live-kit.json
```

Both commands must pass before the live-session helper is trusted for the final desktop observations.

## Prepare the desktop session

Use the retained core session helper from an ordinary, interactive, unelevated PowerShell prompt with UAC enabled:

```powershell
.\beta-qa-session.ps1 -Mode prepare `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

The prepare step creates an isolated workspace, verifies the candidate again, launches the exact package and records the baseline session identity. It does not pass any human gate.

## Re-check continuity before observations

Before recording a human-confirmed observation, and again whenever the shell/app/session state might have changed, run:

```powershell
.\beta-qa-live-session.ps1 -Mode verify `
  -WorkspacePath <prepared-session-workspace>
```

The helper fails closed unless the prepared workspace is still contained and intact, the recorded Dragon DiskForge process is still running from the exact hashed executable, the current shell remains interactive and unelevated with UAC enabled, the Windows build/process architecture/session id still match the baseline, and the Explorer drop target still exists without becoming a reparse point.

A successful continuity check means only that the same prepared desktop context is still valid for continuing the manual test. Human results must still be recorded with the candidate's packaged `tools/beta-manual-qa.ps1` according to `BETA-MANUAL-VALIDATION.md`.

## Release boundary

The live-session companion never marks `normal-user UAC`, `cross-process Explorer drag-out`, clean-machine regression, or public beta readiness as complete. Those gates close only after the exact retained package has genuine human-confirmed observations and the final release-proof contract passes.
