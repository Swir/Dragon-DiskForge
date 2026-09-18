# Dragon DiskForge — Beta QA Evidence Archive

The interactive `0.5.0-beta.1` beta gate produces package-bound human evidence inside the disposable workspace prepared by `beta-qa-session.ps1`. That workspace is intentionally removable after testing, so valid evidence must be preserved before cleanup when it needs to be handed to release verification.

`beta-qa-archive.ps1` creates a small integrity-preserving snapshot without copying the large candidate ZIP and without claiming that any human gate passed. Archive schema v2 can also preserve the objective desktop witness **only when `-IncludeWitness` is explicitly requested**. The retained beta candidate carries this helper, this guide and a standalone archive-kit manifest/verifier, so evidence preservation does not require a repository checkout.

## Safety and truth boundary

The archive is **provenance storage only**. A successful archive verification means that the stored evidence, evidence sidecar, package manifest and sanitized session provenance still match their recorded SHA-256 values and candidate identity. When a desktop witness is explicitly included, verification additionally proves that the witness and sidecar are intact and still bind to the same candidate package, original QA-session metadata and immutable manual-QA evidence identity.

It does **not** mean that clean desktop launch, normal-user UAC, Explorer drag-out, milestone `0.9`, or public beta readiness passed. A preserved witness remains objective supporting evidence only: it cannot prove that a human actually dragged from Dragon DiskForge, that Windows negotiated Copy semantics, that the source remained unchanged or that UAC behaved correctly. Final release proof must still verify the original exact candidate package and complete human-confirmed evidence through the existing release contracts.

The archive manifest and sanitized session provenance both carry `releaseGateClaimed=false` by design. The separate retained archive-kit manifest carries `humanGateClaimed=false` and `publicRelease=false`. A copied desktop witness must itself carry `humanGateClaimed=false`; any attempt to archive a witness that claims a human gate fails closed.

## Verify the retained archive tooling first

From the extracted retained candidate directory, verify the hash-bound archive companion before using its helper:

```powershell
.\beta-qa-archive-kit-verify.ps1 -ManifestPath .\beta-qa-archive-kit.json
```

This binds the archive helper and guide to the same source commit, workflow run, package SHA-256, core QA-kit manifest and desktop-witness manifest as the selected retained candidate. It does not inspect or approve human observations.

## Preserve evidence before cleanup

Run this after completing or pausing an interactive QA session and before deleting its disposable workspace:

```powershell
.\beta-qa-archive.ps1 -Mode archive `
  -WorkspacePath <session-workspace> `
  -ArchiveRoot <path-outside-session-workspace>
```

When `-ArchiveRoot` is omitted, the helper uses `artifacts/manual-qa/evidence-archive/` relative to the current working directory and creates a deterministic identity-bearing child directory. For retained-artifact testing, an explicit destination outside the disposable session workspace is recommended so the preserved snapshot is easy to locate.

The archive root must resolve outside the disposable QA workspace. An archive root inside the session workspace is rejected so `beta-qa-session.ps1 -Mode cleanup` cannot silently delete the preserved snapshot.

A normal schema-v2 snapshot contains:

- `beta-manual-qa.json`
- `beta-manual-qa.json.sha256`
- `package-manifest.json`
- `session-provenance.json`
- `archive-manifest.json`

The snapshot intentionally does not duplicate the large candidate ZIP. The package SHA-256, architecture, desktop entry-point SHA-256, packaged manual-QA tool SHA-256 and original QA-session metadata SHA-256 remain recorded in the archive manifest and must resolve back to the exact retained candidate for release proof.

## Preserve the objective desktop witness

If the real Explorer drag-out session produced `beta-qa-desktop-witness.json` and its SHA-256 sidecar, preserve that supporting evidence explicitly:

```powershell
.\beta-qa-archive.ps1 -Mode archive `
  -WorkspacePath <session-workspace> `
  -ArchiveRoot <path-outside-session-workspace> `
  -IncludeWitness
```

`-IncludeWitness` is opt-in because the witness carries more local desktop/session metadata than the sanitized core evidence archive. The helper refuses the request unless the witness and sidecar are present, intact and bound to the same package SHA-256, exact original `beta-qa-session.json` SHA-256, immutable manual-QA evidence identity and Explorer drop target.

A witness-preserving schema-v2 snapshot adds:

- `beta-qa-desktop-witness.json`
- `beta-qa-desktop-witness.json.sha256`

The archive manifest records whether a witness is included, its SHA-256 and whether an objective destination observation was captured. `humanGateClaimed` remains false regardless of witness presence.

## Verify a preserved snapshot

```powershell
.\beta-qa-archive.ps1 -Mode verify `
  -ArchivePath <preserved-snapshot-directory>
```

Verification fails closed when:

- an archived payload is missing, unexpected or its SHA-256 changed;
- the evidence sidecar is malformed or mismatched;
- evidence schema/gate/package identity disagrees with the archive manifest;
- the archived package manifest disagrees with the recorded version, architecture or entry-point hashes;
- sanitized session provenance disagrees with the candidate identity or original session-metadata SHA-256;
- a witness-preserving archive contains a missing, tampered, mismatched-candidate or mismatched-session witness;
- the witness immutable evidence identity no longer matches the archived manual-QA evidence;
- witness observation state disagrees with the archive manifest;
- an archive or witness attempts to claim human/release-gate completion.

The verifier continues to accept legacy schema-v1 evidence archives created by the previous helper. New archives are always schema v2.

## Contract self-test

Repository CI exercises the archive helper directly under both PowerShell 7 and Windows PowerShell 5.1:

```powershell
.\scripts\beta-qa-archive.ps1 -Mode self-test
```

The self-test creates a synthetic package-bound QA workspace, verifies both a normal schema-v2 archive and an explicit witness-preserving archive, then proves fail-closed behavior for tampered evidence, tampered witness, a synthetic witness human-gate claim and an archive root placed inside the disposable workspace. The separate archive-kit contract additionally proves that the retained helper/guide cannot be substituted without breaking their package-bound manifest verification.

## Cleanup order

Use this order for a real retained-candidate session:

1. Verify `beta-qa-archive-kit.json` with the retained standalone verifier.
2. Perform and record only observations that were actually witnessed.
3. For Explorer drag-out, capture and verify the objective desktop witness if it is useful for audit.
4. Run the packaged `tools/beta-manual-qa.ps1 -Mode verify` when all required checks are expected to pass.
5. Preserve the core evidence snapshot with `beta-qa-archive.ps1 -Mode archive`; add `-IncludeWitness` only when the witness should be retained for audit.
6. Verify the preserved snapshot with `beta-qa-archive.ps1 -Mode verify`.
7. Only then run `beta-qa-session.ps1 -Mode cleanup` if the disposable workspace is no longer needed.

If the human evidence is incomplete, an archive may still be created as a checkpoint. It remains a snapshot of that incomplete state and must never be presented as beta-ready evidence.