# Dragon DiskForge — Manual UAC validation

This checklist validates the interactive elevation path that cannot be faithfully exercised by GitHub-hosted Windows runners because those runners execute elevated.

## Preconditions

- Run Dragon DiskForge from a normal, non-elevated Windows desktop session.
- Prepare one disposable `.vhd` or `.vhdx` image and one `.iso` image.
- Ensure no image is mounted before starting.

## VHD/VHDX elevation path

1. Open the VHD/VHDX in Dragon DiskForge.
2. Choose **Mount read-only**.
3. Confirm that Windows shows a UAC consent prompt only for the virtual-disk operation.
4. Approve the prompt.
5. Confirm the image becomes mounted and the UI refreshes to the real mounted state.
6. Unmount it from Dragon DiskForge and confirm the state disappears from the Mounted dashboard.

## Cancelled elevation

1. Start mounting a detached VHD/VHDX from a normal non-elevated session.
2. Cancel the Windows UAC prompt.
3. Confirm Dragon DiskForge reports that administrator approval was cancelled.
4. Confirm the image remains detached and the Mounted dashboard stays accurate after refresh.

## ISO no-elevation path

1. Open an ISO in Dragon DiskForge.
2. Choose **Mount read-only**.
3. Confirm no UAC prompt is shown.
4. Confirm a drive letter is detected and the mounted ISO appears in the Mounted dashboard.
5. Unmount it and confirm removal from the dashboard.

## Pass criteria

- UAC appears for VHD/VHDX only when the process is not already elevated.
- Cancelling consent changes no storage state.
- ISO mounts without elevation.
- The UI always re-reads Windows state after mount, unmount or cancellation.
