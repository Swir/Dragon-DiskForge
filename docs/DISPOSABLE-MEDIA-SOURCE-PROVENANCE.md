# Disposable physical-media source provenance

This document extends the existing disposable-media hardware evidence package for the one remaining 0.7 hardware gate. It does not expose destructive writing in Dragon DiskForge and it does not replace the requirement for a physically supervised disposable-media run.

## Why this exists

A valid hardware evidence JSON proves what happened to the selected device, but the final gate must also prove **which exact Dragon DiskForge source revision produced that evidence**. A stale or locally modified harness must not be accepted as current milestone evidence.

The final supervised validation should therefore be started through:

```powershell
pwsh -NoProfile -File .\scripts\run-disposable-physical-media-validation.ps1
```

Use the same destructive opt-in, target identity, source image, destination-bound confirmation token and fresh `DDF_DISPOSABLE_EVIDENCE_PATH` documented in `DISPOSABLE-MEDIA-EVIDENCE.md`.

## Fail-closed source requirements

Before the existing destructive harness is started, the wrapper requires all of the following:

- a Git checkout with `origin` pointing to canonical `Swir/Dragon-DiskForge`;
- an exact 40-hex `HEAD` commit;
- no tracked working-tree or index modifications;
- the existing fail-closed hardware harness and evidence verifier present at their canonical paths;
- a fresh evidence destination and fresh provenance destination.

Untracked build outputs are ignored by the clean-tree check because normal `dotnet` build products are not source evidence. Tracked modifications are always refused.

## Provenance output

After the existing hardware harness has completed, flushed the device, passed physical read-back SHA-256 and produced a valid evidence JSON/sidecar pair, the wrapper creates:

- `<evidence>.provenance.json`
- `<evidence>.provenance.json.sha256`

The provenance envelope records the canonical repository name, exact source commit, clean-tree assertion, hardware-evidence JSON SHA-256, hardware-evidence sidecar SHA-256, and SHA-256 values for the destructive harness source, base evidence verifier and wrapper source.

The provenance pair is written only after the base hardware evidence passes its existing verifier. Existing provenance files are never overwritten.

## Independent review

Verify the complete pair from a clean checkout of the recorded commit:

```powershell
pwsh -NoProfile -File .\scripts\verify-disposable-physical-media-provenance.ps1 `
  -EvidencePath '<path>\dragon-diskforge-physical-media-evidence.json' `
  -ProvenancePath '<path>\dragon-diskforge-physical-media-evidence.json.provenance.json' `
  -SourceRoot '<path>\Dragon-DiskForge' `
  -ExpectedSourceCommit '<40-hex commit>'
```

The verifier fails closed when the provenance sidecar is invalid, the repository is not canonical, the checkout is dirty, `HEAD` differs from the recorded or expected commit, source hashes differ, the hardware evidence or its sidecar has changed, or the base hardware-evidence verifier rejects the run.

## CI boundary

CI only self-tests the provenance contract under PowerShell 7 and Windows PowerShell 5.1. CI explicitly refuses destructive opt-in variables. Hosted CI cannot satisfy the physical-media milestone gate.

## Gate rule

The JSON/sidecar evidence pair plus its source-provenance pair materially improve reviewability, but they still do **not** complete milestone 0.7 by themselves. Completion requires a physically supervised run against intentionally selected disposable hardware and review that the recorded device identity matches that supervised target.
