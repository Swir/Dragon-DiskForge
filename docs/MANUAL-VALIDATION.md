# Dragon DiskForge — Manual Validation

Automated CI is the primary quality gate, but a few Windows desktop behaviors depend on an interactive user session and cannot be proven reliably on a GitHub-hosted administrator runner.

## Exact-package evidence workflow

The clean Windows package contains `tools/beta-manual-qa.ps1`. It is intentionally fail-closed and is used only to record the remaining human desktop observations against the exact final `0.5.0-beta.1` release candidate.

The tool binds the evidence to the ZIP SHA-256, package version, desktop entry-point SHA-256 and packaged QA-tool SHA-256. Passing observations require an interactive **unelevated** Windows session plus explicit human confirmation. The evidence JSON is written atomically and receives its own SHA-256 sidecar. `verify` fails if a required check is pending/failing, the package changed, the evidence was tampered with, initialization or observation was elevated/non-interactive, or explicit human confirmation is absent.

The committed source metadata is now promoted to `0.5.0-beta.1`, but a verification-only PR artifact is not the final manual-QA target. Initialize final evidence only against the retained `0.5.0-beta.1` ZIP/checksum pair produced from the exact green `main` commit selected for beta testing. Run from a normal unelevated PowerShell session, for example:

```powershell
.\tools\beta-manual-qa.ps1 -Mode new `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -EvidencePath .\beta-manual-qa.json

.\tools\beta-manual-qa.ps1 -Mode list -EvidencePath .\beta-manual-qa.json
```

After physically performing one checklist observation, record exactly that result. A passing result requires `-HumanConfirmed`:

```powershell
.\tools\beta-manual-qa.ps1 -Mode record `
  -EvidencePath .\beta-manual-qa.json `
  -Check uac.iso-no-prompt `
  -Result pass `
  -HumanConfirmed `
  -Note "Mounted and unmounted the test ISO without a UAC prompt."
```

At the end, verify the entire evidence set against the same release-candidate ZIP:

```powershell
.\tools\beta-manual-qa.ps1 -Mode verify `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -EvidencePath .\beta-manual-qa.json
```

The script also exposes `-Mode self-test`; CI executes that contract under both PowerShell 7 and Windows PowerShell 5.1. A green self-test proves the evidence machinery, **not** the human UAC or cross-process drag gestures.

## Exact-candidate QA session preparation helper

The repository also contains `scripts/beta-qa-session.ps1` to prepare the retained candidate for the remaining interactive checks without weakening or auto-completing any release gate. This helper stays **outside** the retained ZIP so using it does not change the package hash selected for manual QA.

From a normal unelevated interactive Windows session with UAC enabled, run:

```powershell
.\scripts\beta-qa-session.ps1 -Mode prepare `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256
```

The helper fails closed unless it can prove the candidate checksum, product/version/x64 manifest identity, self-contained .NET + Windows App SDK + app-local Visual C++ deployment, desktop entry-point hash, packaged QA-tool hash, required runtime payload, a single desktop executable and PDB-free package hygiene. It then creates an isolated workspace, invokes the **candidate's own** `tools/beta-manual-qa.ps1` to initialize hash-bound evidence, performs a short local process-liveness preflight and opens a dedicated Windows Explorer drop-target folder.

The liveness preflight is deliberately not a human result: session metadata is written with `manualGatePassed=false`, and no required evidence check is automatically changed to `pass`. The user still has to visibly perform each checklist action and record it with `-HumanConfirmed` through the packaged evidence tool.

Use the path printed by `prepare` for the following helper modes:

```powershell
.\scripts\beta-qa-session.ps1 -Mode status -WorkspacePath .\artifacts\manual-qa\session-<package-sha-prefix>
.\scripts\beta-qa-session.ps1 -Mode cleanup -WorkspacePath .\artifacts\manual-qa\session-<package-sha-prefix>
```

`status` delegates to the exact packaged QA tool and lists the package-bound evidence state. `cleanup` stops the recorded Dragon DiskForge process only when the current process path still matches the recorded executable, then removes the isolated workspace. The helper's deterministic `self-test` is exercised in CI under both PowerShell 7 and Windows PowerShell 5.1; that test validates preparation mechanics only and does not replace the interactive release gate.

## Clean supported Windows desktop regression

Status: **manual QA required before public beta packaging**.

The final package must be launched from the extracted release ZIP on a supported clean Windows desktop without a repository checkout or developer tooling. Record these package-facing checks through the evidence tool:

- `desktop.clean-launch` — packaged WinUI application launches normally from the final ZIP.
- `desktop.basic-regression` — Open, Mount, Explorer, Verify and Analyze complete successfully through the packaged WinUI application.

A hosted package-only runtime matrix already proves the self-contained CLI/shell/state/diagnostic paths without source checkout. It does not substitute for actually launching and driving the WinUI desktop application as a user.

## 0.2 UAC / non-admin validation

Status: **manual QA required before public beta packaging**.

The native mount engine contains an explicit commit boundary around Windows storage mutations. Caller cancellation is honored before the native mount/unmount process is launched. Once Windows has started `Mount-DiskImage` or `Dismount-DiskImage`, Dragon DiskForge lets that native operation finish and performs bounded live-state reconciliation instead of killing the helper process and assuming rollback. An explicitly cancelled UAC prompt is still treated as a no-change cancellation. GitHub Actions proves ISO/VHD/VHDX mount/unmount, read-only behavior, drive-letter detection, mounted-image inventory, pre-launch cancellation safety and error translation. The runner itself executes as an administrator, so it cannot faithfully validate the interactive UAC prompt seen by a normal desktop user.

### Test matrix

Run Dragon DiskForge from a normal non-admin Windows account or a standard unelevated desktop session.

1. Open a valid ISO.
   - Mount should work without an administrator prompt.
   - The mounted drive should appear in the image card and in the Mounted dashboard.
   - Unmount should complete normally.
   - Evidence check: `uac.iso-no-prompt`.

2. Open a valid VHD.
   - Dragon DiskForge should request elevation only when the native Windows operation requires it.
   - Cancelling the UAC prompt must leave the image detached.
   - Approving UAC should mount the image read-only.
   - The mounted state must refresh after the elevated operation completes.
   - Evidence checks: `uac.vhd-cancel` and `uac.vhd-approve-readonly`.

3. Repeat the VHD test with VHDX.
   - The same elevation, cancellation and state-refresh rules must hold.
   - Evidence checks: `uac.vhdx-cancel` and `uac.vhdx-approve-readonly`.

4. Cancel an elevation request.
   - The app should show a friendly cancellation message.
   - No stale Mounted entry may remain.
   - A manual Refresh in Mounted must match the real Windows state.

5. Unmount a mounted VHD/VHDX from the Mounted dashboard.
   - Elevation should be requested only if Windows requires it.
   - After completion, the item must disappear from the live inventory.
   - Evidence check: `uac.unmount-refresh`.

### UAC pass criteria

- ISO never asks for unnecessary elevation.
- VHD/VHDX elevation is requested only for the native operation that needs it.
- Cancelling the UAC prompt never changes the disk-image state.
- Approving UAC completes the requested operation and the UI refreshes to the real Windows state.
- Error text is understandable and does not expose raw command noise as the primary message.

## 0.3 Explorer drag-out validation

Status: **manual desktop gesture QA required before public beta packaging**.

Automated CI validates the source path, mounted-root boundary, stale-source handling, reparse-point blocking, WinUI compilation and Copy-only transfer intent. A hosted runner cannot faithfully reproduce a person dragging from one desktop application into another.

### Test matrix

1. Mount a trusted ISO and open it in Dragon Explorer.
2. Drag a normal file from Dragon Explorer into an empty folder in Windows Explorer.
   - Windows should show a copy operation.
   - The destination copy must appear with matching content.
   - The mounted source must remain unchanged.
   - Evidence check: `drag.file-explorer-copy`.
3. Drag a normal folder into Windows Explorer.
   - Windows should copy the folder rather than move it.
   - Source contents must remain available in Dragon Explorer.
   - Evidence check: `drag.folder-explorer-copy`.
4. Drag an item to the Windows desktop and verify the same Copy-only behavior.
   - Evidence check: `drag.desktop-copy`.
5. Start a drag, then cancel it before dropping.
   - No destination file/folder should be created by Dragon itself.
   - Evidence check: `drag.cancel-no-mutation`.
6. If a reparse point/junction can be presented in a mounted test volume, verify that Dragon blocks drag-out and shows a warning.
   - Evidence check: `drag.reparse-blocked`.
7. Unmount or otherwise invalidate a source and verify stale entries cannot be dragged successfully after refresh/state change.
   - Evidence check: `drag.stale-blocked`.

### Drag-out pass criteria

- Dragon advertises Copy only; it never requests Move.
- Source data on the mounted image remains unchanged.
- Files and folders land correctly in Windows Explorer/Desktop.
- Reparse points, path escapes and stale sources remain blocked.
- A failed/cancelled drag does not create misleading success state in Dragon.

These checklists are intentionally separate from the automated integration suite. Failure here is a release-blocking desktop QA issue, not a reason to fake a CI result. The evidence tool makes the result reproducible and package-bound, but only a real human desktop session can complete these release gates.
