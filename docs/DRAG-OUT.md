# Dragon Explorer drag-out

This document is intentionally created only on the feature branch while the drag-out slice is under CI validation.

The implementation exports safe mounted-volume files/folders to Windows drag-and-drop as **copy-only** storage items. Reparse points, stale paths and paths outside the mounted root are rejected before transfer.
