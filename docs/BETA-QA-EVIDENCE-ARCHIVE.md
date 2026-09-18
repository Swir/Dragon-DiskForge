# Dragon DiskForge — Beta QA Evidence Archive

The interactive `0.5.0-beta.1` beta gate produces package-bound human evidence inside the disposable workspace prepared by `scripts/beta-qa-session.ps1`. That workspace is intentionally removable after testing, so valid evidence must be preserved before cleanup when it needs to be handed to release verification.

`beta-qa-archive.ps1` creates a small integrity-preserving snapshot without copying the large candidate ZIP and without claiming that any human gate passed.

## Safety and truth boundary

The archive is **provenance storage only**. A successful archive verification means that the stored evidence, evidence sidecar, package manifest and sanitized session provenance still match their recorded SHA-256 values and candidate identity.

It does **not** mean that clean desktop launch, normal-user UAC, Explorer drag-out, milestone `0.9`, or public beta readiness passed. Final release proof must still verify the original exact candidate package and complete human-confirmed evidence through the existing release contracts.

The archive manifest and sanitized session provenance both carry `releaseGateClaimed=false` by design.

## Preserve evidence before cleanup

Run this from the repository checkout after completing or pausing an interactive QA session and before deleting its disposable workspace:

```powershell
.\scripts\beta-qa-archive.ps1 -Mode archive `
  -WorkspacePath .\artifacts\manual-qa\session-<package-sha-prefix>
```

The default destination is:

```text
artifacts/manual-qa/evidence-archive/<version>-<package-sha-prefix>-<evidence-sha-prefix>/
```

The archive root must resolve outside the disposable QA workspace. An archive path inside the session workspace is rejected so `beta-qa-session.ps1 -Mode cleanup` cannot silently delete the preserved snapshot.

Each snapshot contains only:

- `beta-manual-qa.json`
- `beta-manual-qa.json.sha256`
- `package-manifest.json`
- `session-provenance.json`
- `archive-manifest.json`

The snapshot intentionally does not duplicate the 160+ MiB candidate ZIP. The package SHA-256, architecture, desktop entry-point SHA-256 and packaged manual-QA tool SHA-256 remain recorded in the archive manifest and must be resolved back to the exact retained candidate for release proof.

## Verify a preserved snapshot

```powershell
.\scripts\beta-qa-archive.ps1 -Mode verify `
  -ArchivePath .\artifacts\manual-qa\evidence-archive\<archive-folder>
```

Verification fails closed when:

- an archived payload is missing or its SHA-256 changed;
- the evidence sidecar is malformed or mismatched;
- evidence schema/gate/package identity disagrees with the archive manifest;
- the archived package manifest disagrees with the recorded version, architecture or entry-point hashes;
- sanitized session provenance disagrees with the candidate identity;
- an archive manifest attempts to claim release-gate completion.

## Contract self-test

```powershell
.\scripts\beta-qa-archive.ps1 -Mode self-test
```

The self-test creates a synthetic package-bound QA workspace, archives and verifies it, then proves that tampered archived evidence and an archive root placed inside the disposable workspace both fail closed. CI runs this contract under PowerShell 7 and Windows PowerShell 5.1.

## Cleanup order

Use this order for a real retained-candidate session:

1. Perform and record only observations that were actually witnessed.
2. Run the packaged `tools/beta-manual-qa.ps1 -Mode verify` when all required checks are expected to pass.
3. Preserve the evidence snapshot with `beta-qa-archive.ps1 -Mode archive`.
4. Verify the preserved snapshot with `beta-qa-archive.ps1 -Mode verify`.
5. Only then run `beta-qa-session.ps1 -Mode cleanup` if the disposable workspace is no longer needed.

If the human evidence is incomplete, an archive may still be created as a checkpoint. It remains a snapshot of that incomplete state and must never be presented as beta-ready evidence.
