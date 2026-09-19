# Dragon DiskForge — Explorer Destination Witness

The Explorer destination witness adds objective provenance around the real cross-process drag-out test without pretending to replace the human observation required by `BETA-RELEASE.md`.

`scripts/beta-qa-explorer-witness.ps1` binds the prepared QA session and exact candidate package to one visible **File Explorer** window that is actually showing the isolated drop-target folder. It records the Explorer process id, process start time, Windows session id, top-level window handle and normalized destination path, then fails closed if that destination is rebound before verification.

## Truth boundary

A successful capture/verify cycle proves only that:

- the exact candidate ZIP still matches its SHA-256 sidecar and the prepared session;
- the current shell remains in the same interactive, unelevated, UAC-enabled Windows session;
- exactly one visible File Explorer window is showing the prepared drop target;
- the Explorer process, process start time, session, window handle and destination folder remain unchanged between capture and verify;
- the witness file itself still matches its SHA-256 sidecar.

It does **not** prove that the user dragged from Dragon DiskForge, that Windows negotiated Copy semantics, that the source stayed unchanged, or that any manual release gate passed. The corresponding packaged `tools/beta-manual-qa.ps1` check still requires `-HumanConfirmed` and a real observation note. Every Explorer witness permanently carries `humanGateClaimed=false`.

## Recommended sequence

Prepare the exact retained candidate from an ordinary unelevated desktop session with UAC enabled. The existing session helper opens an isolated Explorer destination:

```powershell
.\beta-qa-session.ps1 -Mode prepare `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

Set the printed workspace path and capture the Explorer destination binding before the real gesture:

```powershell
$Workspace = '<workspace-path-printed-by-beta-qa-session.ps1>'
.\scripts\beta-qa-explorer-witness.ps1 -Mode capture -WorkspacePath $Workspace
.\scripts\beta-qa-desktop-witness.ps1 -Mode baseline -WorkspacePath $Workspace
```

Physically drag the supported item from Dragon DiskForge into that exact Explorer window. After the destination operation completes, capture the existing bounded destination-tree witness and verify that File Explorer has not been rebound:

```powershell
.\scripts\beta-qa-desktop-witness.ps1 -Mode observe -WorkspacePath $Workspace
.\scripts\beta-qa-explorer-witness.ps1 -Mode verify -WorkspacePath $Workspace
```

After visually confirming Copy semantics and source preservation, record the packaged manual check using the exact package/checksum and `-HumanConfirmed`, then run the normal desktop-witness and manual-evidence verification. The Explorer witness is supporting provenance only.

If more than one visible Explorer window resolves to the prepared destination, the helper intentionally refuses to choose one. Close duplicate windows and capture a fresh binding. If Explorer restarts, the target window changes, the session changes, the package changes, or the witness is tampered with, verification fails closed and the observation should be repeated.

## Contract self-test

The self-test is deterministic and does not synthesize GUI input. It checks sidecar tamper rejection, path containment and Explorer process/window rebinding rejection under both PowerShell 7 and Windows PowerShell 5.1:

```powershell
.\scripts\beta-qa-explorer-witness.ps1 -Mode self-test
```

The dedicated `Dragon DiskForge Explorer Witness Contract` workflow executes that self-test on Windows. This hardening improves auditability only; it does not change project progress, milestone `0.9` completion or beta readiness.
