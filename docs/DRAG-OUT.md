# Dragon Explorer drag-out

Dragon Explorer exports mounted-volume files and folders to Windows drag-and-drop as **copy-only** storage items. Drag-out is intentionally read-only with respect to the mounted image: Dragon DiskForge requests `DataPackageOperation.Copy` and never advertises Move.

## Safety boundary

Mounted-volume path validation is centralized in `ExplorerPathSafetyValidator`. Before a storage item is placed into the Windows data package, `ExplorerDragOutValidator` delegates to that same boundary and requires all of the following:

- the mounted root and candidate path are non-empty and normalize successfully
- the candidate is lexically contained by the mounted root
- the candidate still exists with the expected file/directory shape
- an entry already identified as a reparse point by Explorer enumeration is rejected
- the selected item **and every path component below the trusted mounted root** are checked for `FileAttributes.ReparsePoint`
- any junction/symbolic-link/reparse traversal below the mounted root fails closed, including a lexically in-root child that would resolve to data outside the mounted volume

The same validator is reused for mounted-folder browsing/search traversal, file preview/open and copy-out. Copy-out revalidates planned files immediately before opening them so a stale path that has become reachable through a reparse ancestor is rejected rather than copied.

Copy-out publication is transactional at the destination boundary. Single files are written through `SafeOutputService` and become visible only after a complete flush/commit. Directory exports are assembled under a unique Dragon-owned staging directory and moved to the final name only after the whole tree completes. Cancellation or failure removes the staging tree best-effort, so Dragon DiskForge does not intentionally publish a partial final file or directory tree.

The mounted root itself is the trusted anchor and is not rejected merely because Windows represents that root through a mount mechanism. Components beneath it are not trusted.

These checks reduce path-escape and partial-output risk but are not presented as a formal race-free filesystem sandbox. A real cross-process drag into Windows Explorer/Desktop remains a separate manual beta gate because hosted CI cannot faithfully reproduce that user gesture or all shell behavior.

## Automated validation

`DragonDiskForge.DragOut.SmokeTests` covers normal files/folders, nested normal paths, lexical escapes, declared reparse entries, a real junction/symbolic-link entry, traversal through a reparse ancestor to an existing outside file, stale paths, mounted Explorer browse/search/copy-out rejection through that same reparse ancestor, successful single-file/directory copy-out commits, and cancellation rollback with no published partial destination or Dragon staging/temp residue. The Windows CI creates an actual directory junction for the escape fixture so the ancestor check is exercised against filesystem metadata rather than only a mocked flag. The full WinUI Release build additionally proves the preview/open call sites compile against the canonical validator.

See `docs/MANUAL-VALIDATION.md` for the remaining package-bound Explorer/Desktop gesture checklist.
