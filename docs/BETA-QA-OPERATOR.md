# Dragon DiskForge — Beta QA Workspace Router

`beta-qa-operator.ps1` is a small read-only operator helper for the remaining interactive beta gates. It reads the package/checksum/evidence paths already recorded by `beta-qa-session.ps1` and delegates status/next/group verification to the existing fail-closed `beta-manual-gate-status.ps1` verifier.

It does **not** create observations, auto-pass a gate, modify evidence, change roadmap progress or authorize a release. The exact packaged `tools/beta-manual-qa.ps1` remains the only writer for authoritative human observations.

## Why this exists

The retained QA session already records the exact candidate package, checksum sidecar, extracted packaged QA tool and schema-v3 manual-QA evidence file. Re-entering those paths for every status or recording step adds avoidable operator error while the two 0.9 human gates remain open.

The router turns the prepared workspace into the input contract. From a repository checkout:

```powershell
.\scripts\beta-qa-operator.ps1 -Mode next -WorkspacePath <session-workspace>
```

It validates session schema 1, refuses missing package/checksum/evidence files and then lets `beta-manual-gate-status.ps1` perform the authoritative package/evidence binding checks.

## Retained candidate artifact

The beta-candidate workflow stages the router together with its delegated verifier in the root of an explicitly retained candidate artifact. Each companion file has its own SHA-256 sidecar, so the downloaded operator surface can be checked independently without modifying the immutable candidate ZIP.

After downloading and extracting the retained artifact, run:

```powershell
.\beta-qa-operator.ps1 -Mode next -WorkspacePath <session-workspace>
```

The default verifier path resolves to the colocated `beta-manual-gate-status.ps1`; no repository checkout or repeated package/checksum/evidence path entry is required.

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

Generate an exact, copy-pasteable record command after physically performing a real observation:

```powershell
.\scripts\beta-qa-operator.ps1 -Mode record-command -WorkspacePath <session-workspace> -Check uac.vhd-cancel -Result pass -HumanConfirmed -Note "UAC cancellation left the VHD detached and the app reported cancellation cleanly."
```

`record-command` is still read-only: it prints but does not execute the authoritative packaged QA command. Before printing, the router resolves the packaged `tools/beta-manual-qa.ps1` from the prepared session, rejects rooted/path-escape values and re-verifies its session-bound SHA-256. Passing command generation requires `-HumanConfirmed`; notes must meet the schema-v3 12–1000 character observation requirement. Failed observations can be generated with `-Result fail` without claiming a pass.

When working from an extracted retained candidate artifact, use the same commands with `.\beta-qa-operator.ps1` instead of `.\scripts\beta-qa-operator.ps1`.

Run the routing contract self-test:

```powershell
.\scripts\beta-qa-operator.ps1 -Mode self-test
```

## Safety boundary

The router cannot write schema-v3 evidence or turn objective witness evidence into a human claim. Its `record-command` mode only produces a command line bound to the prepared package/checksum/evidence and the hash-verified packaged QA tool; the operator must deliberately run that generated command after the real observation. A successful `verify-group` proves only the selected manual-QA group for the exact package/session evidence; `docs/BETA-RELEASE.md` remains the release authority.

This helper is blocker-removal tooling for the current 0.9 qualification target. It does not change the 83% weighted project progress or the 5/7 milestone count until the actual human acceptance evidence exists.
