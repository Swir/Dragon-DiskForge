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

Drag-out performs this boundary check twice around Windows storage-item resolution. The selected source is validated before `StorageFile`/`StorageFolder` acquisition, then the resolved storage item is revalidated immediately before it is published into the data package. The second check requires the resolved path and file/directory shape to match the originally validated selection and repeats existence/reparse traversal checks. A source that disappears, changes shape, resolves to another path, or becomes a junction/reparse path during transfer preparation is therefore rejected instead of being advertised to Explorer.

The same validator is reused for mounted-folder browsing/search traversal, file preview/open and copy-out. Copy-out revalidates planned files immediately before opening them so a stale path that has become reachable through a reparse ancestor is rejected rather than copied.

Copy-out publication is transactional at the destination boundary. Single files are written through `SafeOutputService` and become visible only after a complete flush/commit. Directory exports are assembled under a unique Dragon-owned staging directory and moved to the final name only after the whole tree completes. Cancellation or failure removes the staging tree best-effort, so Dragon DiskForge does not intentionally publish a partial final file or directory tree.

The mounted root itself is the trusted anchor and is not rejected merely because Windows represents that root through a mount mechanism. Components beneath it are not trusted.

These checks narrow stale-path and path-escape windows but are not presented as a formal race-free filesystem sandbox. A real cross-process drag into Windows Explorer/Desktop remains a separate manual beta gate because hosted CI cannot faithfully reproduce that user gesture or all shell behavior.

## Automated validation

`DragonDiskForge.DragOut.SmokeTests` covers normal files/folders, nested normal paths, lexical escapes, declared reparse entries, a real junction/symbolic-link entry, traversal through a reparse ancestor to an existing outside file, stale paths, mounted Explorer browse/search/copy-out rejection through that same reparse ancestor, successful single-file/directory copy-out commits, and cancellation rollback with no published partial destination or Dragon staging/temp residue. Post-resolution coverage also proves that the same selected path/shape is accepted while a different resolved path, file/directory shape change, source deletion after initial validation, and replacement by a real junction all fail closed. The Windows CI creates actual directory junctions for the escape and substitution fixtures so the ancestor/revalidation checks are exercised against filesystem metadata rather than only mocked flags. The full WinUI Release build additionally proves the storage-item resolution call site compiles against the canonical validator.

See `docs/MANUAL-VALIDATION.md` for the remaining package-bound Explorer/Desktop gesture checklist.
