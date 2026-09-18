# Dragon DiskForge — Desktop Beta QA Witness

The desktop witness is a repository-side helper for the remaining interactive Windows beta gate. It strengthens the evidence around a real retained-candidate session without pretending to replace the human observation required by `docs/BETA-RELEASE.md`.

`scripts/beta-qa-desktop-witness.ps1` binds an objective witness to the exact prepared beta-QA session, candidate package and immutable manual-QA evidence identity. It verifies that the recorded Dragon DiskForge process still resolves to the prepared executable, that the same process owns a visible top-level window, and that the isolated Explorer drop target changes from a verified empty baseline to a bounded, hash-described destination tree.

## Truth boundary

A successful witness proves only objective facts that can be observed locally without synthesizing the user's gesture:

- the same retained candidate package remains bound to the QA session;
- the packaged manual-QA evidence still has the same immutable candidate/session identity, even if individual human check results were recorded after the baseline;
- the same Dragon DiskForge process remains alive and owns a visible top-level window;
- the prepared Explorer drop target was empty at baseline;
- after the user performs the test, destination files/directories exist and match a bounded deterministic hash snapshot;
- the destination tree has not changed when `verify` is run.

The witness **does not** prove that the user actually dragged from Dragon DiskForge, that Windows negotiated Copy semantics, that the source stayed unchanged, that a UAC prompt behaved correctly, or that any manual gate passed. Those observations must still be performed physically and recorded with the packaged `tools/beta-manual-qa.ps1` using `-HumanConfirmed`. Every witness file permanently carries `humanGateClaimed=false`.

## Privacy and safety

The witness is deliberately bounded and fail-closed:

- relative destination names are not written to the witness; only SHA-256 hashes of relative paths are stored;
- file contents are represented by SHA-256 plus byte length, not copied into the witness;
- the default scan limit is 2,048 items and 256 MiB of hashed file data;
- reparse points are rejected rather than followed;
- path containment is checked against the isolated QA workspace;
- the witness and SHA-256 sidecar live inside the disposable QA workspace by default;
- package and manual-QA sidecars are re-verified on every mode;
- a changed candidate, session identity, process identity or destination tree fails closed.

## Recommended drag-out sequence

First prepare the retained candidate from an ordinary **unelevated** interactive Windows session with UAC enabled:

```powershell
.\beta-qa-session.ps1 -Mode prepare `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

From the repository checkout, create the objective baseline against that prepared workspace before putting anything in its Explorer drop target:

```powershell
.\scripts\beta-qa-desktop-witness.ps1 -Mode baseline `
  -WorkspacePath .\artifacts\manual-qa\session-<package-sha-prefix>
```

The baseline requires the target to be empty and the prepared Dragon DiskForge process to own a visible top-level window. It does not mark `desktop.clean-launch` or any drag check as passed.

Next, physically perform the real cross-process drag from Dragon DiskForge into the Explorer window opened by the session helper. After the destination operation completes, capture the objective destination state:

```powershell
.\scripts\beta-qa-desktop-witness.ps1 -Mode observe `
  -WorkspacePath .\artifacts\manual-qa\session-<package-sha-prefix>
```

The manual-QA evidence file may have gained human-confirmed records between `baseline` and `observe`. The witness deliberately binds to the evidence's immutable package/session identity rather than to the mutable whole-file hash, so legitimate recording of individual checklist results does not invalidate the baseline. Changing the candidate identity still fails closed.

After visually confirming the required Copy semantics and source-preservation behavior, record the corresponding packaged manual-QA check with `-HumanConfirmed`. Use the exact check IDs from `BETA-MANUAL-VALIDATION.md`; for example:

```powershell
& '<workspace>\package\tools\beta-manual-qa.ps1' -Mode record `
  -EvidencePath '<workspace>\beta-manual-qa.json' `
  -Check drag.file-explorer-copy `
  -Result pass `
  -HumanConfirmed `
  -Note 'Real cross-process Explorer copy completed; source remained unchanged.'
```

Finally, re-verify the objective witness if the destination should still be unchanged:

```powershell
.\scripts\beta-qa-desktop-witness.ps1 -Mode verify `
  -WorkspacePath .\artifacts\manual-qa\session-<package-sha-prefix>
```

Repeat with a fresh session/baseline as needed for another drag scenario. Do not reuse a witness observation for multiple checklist checks.

## Evidence lifecycle

The desktop witness is supporting evidence, not the authoritative human gate. The existing `beta-qa-archive.ps1` archive remains focused on package-bound manual-QA evidence and does not currently ingest the witness file. Preserve any witness that is useful for audit before running `beta-qa-session.ps1 -Mode cleanup`; otherwise cleanup intentionally removes it with the disposable workspace.

Final beta release proof continues to consume the exact retained candidate and the packaged human-confirmed evidence. This helper does not change QA-kit schema v2, project completion, milestone `0.9` completion or public beta readiness.

## Contract self-test

```powershell
.\scripts\beta-qa-desktop-witness.ps1 -Mode self-test
```

The deterministic self-test validates bounded destination snapshots, witness SHA-256 sidecars, tamper rejection, path/item limits, and the important identity rule that normal mutation of checklist results does not change the immutable evidence identity while candidate-identity mutation is rejected. CI runs the self-test under PowerShell 7 and Windows PowerShell 5.1 as part of the Beta Manual QA Contract.
