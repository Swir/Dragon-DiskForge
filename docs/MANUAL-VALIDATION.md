# Dragon DiskForge — Manual Validation

Automated CI is the primary quality gate, but a few Windows desktop behaviors depend on an interactive user session and cannot be proven reliably on a GitHub-hosted administrator runner.

## 0.2 UAC / non-admin validation

Status: **manual QA required before public beta packaging**.

The native mount engine already contains the elevation policy and error handling. GitHub Actions proves ISO/VHD/VHDX mount/unmount, read-only behavior, drive-letter detection, mounted-image inventory, cancellation safety and error translation. The runner itself executes as an administrator, so it cannot faithfully validate the interactive UAC prompt seen by a normal desktop user.

### Test matrix

Run Dragon DiskForge from a normal non-admin Windows account or a standard unelevated desktop session.

1. Open a valid ISO.
   - Mount should work without an administrator prompt.
   - The mounted drive should appear in the image card and in the Mounted dashboard.
   - Unmount should complete normally.

2. Open a valid VHD.
   - Dragon DiskForge should request elevation only when the native Windows operation requires it.
   - Cancelling the UAC prompt must leave the image detached.
   - Approving UAC should mount the image read-only.
   - The mounted state must refresh after the elevated operation completes.

3. Repeat the VHD test with VHDX.
   - The same elevation, cancellation and state-refresh rules must hold.

4. Cancel an elevation request.
   - The app should show a friendly cancellation message.
   - No stale Mounted entry may remain.
   - A manual Refresh in Mounted must match the real Windows state.

5. Unmount a mounted VHD/VHDX from the Mounted dashboard.
   - Elevation should be requested only if Windows requires it.
   - After completion, the item must disappear from the live inventory.

### UAC pass criteria

- ISO never asks for unnecessary elevation.
- VHD/VHDX elevation is requested only for the native operation that needs it.
- Cancelling UAC never changes the disk-image state.
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
3. Drag a normal folder into Windows Explorer.
   - Windows should copy the folder rather than move it.
   - Source contents must remain available in Dragon Explorer.
4. Drag an item to the Windows desktop and verify the same Copy-only behavior.
5. Start a drag, then cancel it before dropping.
   - No destination file/folder should be created by Dragon itself.
6. If a reparse point/junction can be presented in a mounted test volume, verify that Dragon blocks drag-out and shows a warning.
7. Unmount or otherwise invalidate a source and verify stale entries cannot be dragged successfully after refresh/state change.

### Drag-out pass criteria

- Dragon advertises Copy only; it never requests Move.
- Source data on the mounted image remains unchanged.
- Files and folders land correctly in Windows Explorer/Desktop.
- Reparse points, path escapes and stale sources remain blocked.
- A failed/cancelled drag does not create misleading success state in Dragon.

These checklists are intentionally separate from the automated integration suite. Failure here is a release-blocking desktop QA issue, not a reason to fake a CI result.
