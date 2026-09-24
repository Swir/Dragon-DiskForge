# Dragon DiskForge — Beta QA Workspace Router

`beta-qa-operator.ps1` is a small read-only operator helper for the remaining interactive beta gates. It reads the package/checksum/evidence paths already recorded by `beta-qa-session.ps1` and delegates to the existing fail-closed `beta-manual-gate-status.ps1` verifier.

It does **not** create observations, auto-pass a gate, modify evidence, change roadmap progress or authorize a release. The exact packaged `tools/beta-manual-qa.ps1` remains the only writer for authoritative human observations.

## Why this exists

The retained QA session already records the exact candidate package, checksum sidecar and schema-v3 manual-QA evidence file. Re-entering all three paths for every `status`, `next` or group-verification call adds avoidable operator error while the two 0.9 human gates remain open.

The router turns the prepared workspace into the input contract:

```powershell
.\scripts\beta-qa-operator.ps1 -Mode next -WorkspacePath <session-workspace>
```

It validates session schema 1, refuses missing package/checksum/evidence files and then lets `beta-manual-gate-status.ps1` perform the authoritative package/evidence binding checks.

## Commands

Show all manual-gate groups:

```powershell
.\scripts\beta-qa-operator.ps1 -Mode status -WorkspacePath <session-workspace>
```

Show the highest-priority unresolved real observation:

```powershell
.\scripts\beta-qa-operator.ps1 -Mode next -WorkspacePath <session-workspace>
```

Verify one group only after its real human observations are expected to be complete:

```powershell
.\scripts\beta-qa-operator.ps1 -Mode verify-group -Group uac -WorkspacePath <session-workspace>
.\scripts\beta-qa-operator.ps1 -Mode verify-group -Group drag -WorkspacePath <session-workspace>
.\scripts\beta-qa-operator.ps1 -Mode verify-group -Group desktop -WorkspacePath <session-workspace>
```

Run the routing contract self-test:

```powershell
.\scripts\beta-qa-operator.ps1 -Mode self-test
```

## Safety boundary

The router is intentionally read-only. It does not accept a `pass` result, `-HumanConfirmed`, notes or check IDs. It cannot weaken the schema-v3 evidence contract or turn objective witness evidence into a human claim. A successful `verify-group` proves only the selected manual-QA group for the exact package/session evidence; `docs/BETA-RELEASE.md` remains the release authority.

This helper is blocker-removal tooling for the current 0.9 qualification target. It does not change the 83% weighted project progress or the 5/7 milestone count until the actual human acceptance evidence exists.
