# Dragon DiskForge — Desktop Beta QA Witness

The desktop witness strengthens the evidence around a real retained-candidate Windows session without pretending to replace the human observations required by `BETA-RELEASE.md`. The helper is available both from a repository checkout and, when a candidate is explicitly retained, as a hash-bound companion inside that retained artifact.

`beta-qa-desktop-witness.ps1` binds an objective witness to the exact prepared beta-QA session, candidate package and immutable manual-QA evidence identity. It verifies that the recorded Dragon DiskForge process still resolves to the prepared executable, that the same process owns a visible top-level window, and that the isolated Explorer drop target changes from a verified empty baseline to a bounded, hash-described destination tree.

## Truth boundary

A successful witness proves only objective facts that can be observed locally without synthesizing the user's gesture:

- the same retained candidate package remains bound to the QA session;
- the packaged manual-QA evidence still has the same immutable candidate/session identity even if individual human results were recorded after the baseline;
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

Because the witness also carries local session/process/drop-target provenance, preservation outside the disposable workspace is explicit rather than automatic. Use the archive helper's `-IncludeWitness` switch only when that supporting evidence is useful for audit.

## Verify the retained witness companion first

An explicitly retained beta candidate includes `beta-qa-witness-kit.json`, its sidecar, a standalone verifier, this guide and the desktop-witness helper. Verify that companion before using the helper:

```powershell
.\beta-qa-witness-kit-verify.ps1 -ManifestPath .\beta-qa-witness-kit.json
```

The verifier binds the helper and this guide to the exact core QA-kit manifest, source commit, workflow run and candidate package SHA-256. It permanently rejects `humanGateClaimed=true` and `publicRelease=true`.

When working from a repository checkout instead of a retained artifact, the same helper is available at `scripts/beta-qa-desktop-witness.ps1` and its deterministic contract is exercised by CI.

## Recommended drag-out sequence

First prepare the retained candidate from an ordinary **unelevated** interactive Windows session with UAC enabled:

```powershell
.\beta-qa-session.ps1 -Mode prepare `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

Use the exact workspace path printed by the session helper. Set the helper path once, depending on whether you are using the retained artifact or a repository checkout:

```powershell
$Workspace = '<workspace-path-printed-by-beta-qa-session.ps1>'
$WitnessHelper = if (Test-Path -LiteralPath '.\beta-qa-desktop-witness.ps1') {
    '.\beta-qa-desktop-witness.ps1'
} else {
    '.\scripts\beta-qa-desktop-witness.ps1'
}
```

Create the objective baseline before putting anything in the Explorer drop target:

```powershell
& $WitnessHelper -Mode baseline -WorkspacePath $Workspace
```

The baseline requires the target to be empty and the prepared Dragon DiskForge process to own a visible top-level window. It does not mark `desktop.clean-launch` or any drag check as passed.

Next, physically perform the real cross-process drag from Dragon DiskForge into the Explorer window opened by the session helper. After the destination operation completes, capture the objective destination state:

```powershell
& $WitnessHelper -Mode observe -WorkspacePath $Workspace
```

The manual-QA evidence file may have gained human-confirmed records between `baseline` and `observe`. The witness deliberately binds to the evidence's immutable package/session identity rather than to the mutable whole-file hash, so legitimate recording of individual checklist results does not invalidate the baseline. Changing the candidate identity still fails closed.

After visually confirming the required Copy semantics and source-preservation behavior, record the corresponding packaged manual-QA check with `-HumanConfirmed`. Use the exact check IDs from `BETA-MANUAL-VALIDATION.md`; for example:

```powershell
& "$Workspace\package\tools\beta-manual-qa.ps1" -Mode record `
  -EvidencePath "$Workspace\beta-manual-qa.json" `
  -Check drag.file-explorer-copy `
  -Result pass `
  -HumanConfirmed `
  -Note 'Real cross-process Explorer copy completed; source remained unchanged.'
```

Finally, re-verify the objective witness if the destination should still be unchanged:

```powershell
& $WitnessHelper -Mode verify -WorkspacePath $Workspace
```

Repeat with a fresh session/baseline as needed for another drag scenario. Do not reuse a witness observation for multiple checklist checks.

## Evidence lifecycle

The desktop witness is supporting evidence, not the authoritative human gate. Archive schema v2 can preserve the exact witness and sidecar **only when explicitly requested**:

```powershell
.\beta-qa-archive.ps1 -Mode archive `
  -WorkspacePath $Workspace `
  -ArchiveRoot <path-outside-session-workspace> `
  -IncludeWitness
```

The archive helper re-verifies the witness sidecar, `humanGateClaimed=false`, candidate package SHA-256, original QA-session metadata SHA-256, immutable manual-QA evidence identity and drop-target binding before copying it. The preserved archive then re-verifies the same bindings without needing the disposable workspace to remain present.

Omitting `-IncludeWitness` keeps the smaller privacy-reduced manual-evidence archive and leaves the witness inside the disposable workspace. Read `BETA-QA-EVIDENCE-ARCHIVE.md` before cleanup when the witness should be retained for audit.

Final beta release proof continues to consume the exact retained candidate and the packaged human-confirmed evidence. Preserving the witness strengthens auditability but does not change project completion, milestone `0.9` completion or public beta readiness.

## Contract self-test

From a repository checkout:

```powershell
.\scripts\beta-qa-desktop-witness.ps1 -Mode self-test
.\scripts\beta-qa-witness-kit.ps1 -Mode self-test
.\scripts\beta-qa-archive.ps1 -Mode self-test
```

CI runs the desktop-witness and retained witness-companion contracts under PowerShell 7 and Windows PowerShell 5.1. The archive contract additionally exercises plain and witness-preserving schema-v2 snapshots under both engines and proves tamper/gate/path failures close safely.