# Disposable Physical-Media Evidence Package

This guide exists only for the final **0.7 Dedicated disposable-media validation** gate. It does not expose physical writing in the product UI or public CLI, and it does not turn CI into hardware evidence.

## Safety boundary

Use only a dedicated disposable device whose loss is acceptable. The harness remains fail-closed and requires an elevated Windows session, explicit destructive opt-in, exact `PhysicalDriveN`, exact stable identity, exact source path, destination-bound confirmation token, and an additional acknowledgement for fixed media.

A real destructive run now also requires `DDF_DISPOSABLE_EVIDENCE_PATH`. The path must be a fresh local `.json` file. Existing evidence is never overwritten. Keep the evidence output on storage that is **not** the disposable target.

## Required inputs

```powershell
$env:DDF_DISPOSABLE_WRITE_OPT_IN = 'ERASE_DISPOSABLE_MEDIA_FOR_DRAGON_DISKFORGE_TEST'
$env:DDF_DISPOSABLE_DISK_NUMBER = '<N>'
$env:DDF_DISPOSABLE_STABLE_ID = '<exact stable ID from read-only inventory>'
$env:DDF_DISPOSABLE_IMAGE_PATH = '<absolute path to aligned disposable test image>'
$env:DDF_DISPOSABLE_CONFIRMATION_TOKEN = '<exact destination-bound ERASE PHYSICALDRIVE... token>'
$env:DDF_DISPOSABLE_EVIDENCE_PATH = '<fresh local path>\dragon-diskforge-physical-media-evidence.json'
```

For dedicated fixed test media only, also set the separately documented fixed-media acknowledgement. Never set either destructive opt-in in CI.

Run the existing harness from an elevated PowerShell session:

```powershell
dotnet run --project tests/DragonDiskForge.PhysicalMediaWrite.DisposableTests/DragonDiskForge.PhysicalMediaWrite.DisposableTests.csproj -c Release
```

## Evidence emitted only after a full pass

The harness writes the JSON witness and `<evidence>.sha256` sidecar only after all of these have succeeded:

- destination stable identity and non-system-disk validation;
- read-only Windows preflight and proven source backing-disk topology;
- logical-sector alignment;
- target-volume lock/dismount path and exact destination revalidation;
- bounded sector-aligned write completion;
- explicit device flush;
- execution SHA-256 equality with the locked source image;
- independent physical-device read-back SHA-256 equality.

The witness records schema version, UTC completion time, OS/architecture, exact disk number/device path/stable ID, capacity/bus/removable evidence, source length/hash/backing-disk numbers, logical sector size, aligned execution buffer, bytes written, execution state, lock/dismount + flush completion flags, read-back result, a SHA-256 of the destructive confirmation token, and the read-only preflight evidence strings. The full source path and raw confirmation token are deliberately not stored.

## Offline verification

Verify the pair before treating it as candidate hardware evidence:

```powershell
pwsh -NoProfile -File .\scripts\verify-disposable-physical-media-evidence.ps1 `
  -EvidencePath '<path>\dragon-diskforge-physical-media-evidence.json'
```

The verifier fails closed on a bad sidecar, unsupported schema, non-passing result, malformed hashes, mismatched source/execution/read-back hashes, incorrect byte counts/alignment, recovery-required state, missing lock/dismount/flush/read-back flags, source-on-target topology, or inconsistent preflight markers.

The contract itself is non-destructively self-tested under both PowerShell 7 and Windows PowerShell 5.1 in the Disposable Media Guard workflow:

```powershell
pwsh -NoProfile -File .\scripts\verify-disposable-physical-media-evidence.ps1 -SelfTest
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-disposable-physical-media-evidence.ps1 -SelfTest
```

## Gate rule

A valid JSON/sidecar pair is necessary evidence, not an automatic milestone completion. Before checking off 0.7, review that the run used intentionally selected disposable hardware and that the recorded identity matches the physically supervised target. Any interrupted, failed, ambiguous, source-on-target, hash-mismatched or recovery-required run does **not** satisfy the gate.
