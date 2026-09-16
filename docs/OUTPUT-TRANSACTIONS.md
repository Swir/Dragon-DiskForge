# Safe Output Transactions

Dragon DiskForge uses `SafeOutputService` as the Core transaction boundary for future image creation, conversion, split/join and other file-producing operations.

## Proven contract

- output is first written to a unique temporary file in the destination directory
- the temporary file uses exclusive access and is flushed before finalization
- `FailIfExists` refuses an existing destination before invoking the writer and also fails closed if a destination appears during the write
- `ReplaceExisting` uses same-directory replacement so the completed temporary file is promoted only after the writer has finished successfully
- cancellation is checked before writing and again before final commit
- writer failures and cancellations do not intentionally publish partial destination files
- temporary files are cleaned on failure/cancellation/failed commit when cleanup is possible
- missing parent directories are rejected rather than created implicitly
- unknown overwrite policies fail closed
- the result reports the normalized destination path, committed byte count and whether an existing destination was replaced

## Validation

PR #42 implementation run #296 passed generated cases for:

1. new-file commit and exact byte count
2. pre-existing destination refusal before writer invocation
3. replacement of an existing destination
4. writer failure preserving the original destination
5. cancellation after a partial temporary write
6. pre-cancellation before writer invocation
7. a destination appearing during `FailIfExists`
8. a destination appearing during `ReplaceExisting`
9. missing parent directory rejection
10. invalid overwrite-policy rejection
11. temporary-file cleanup assertions

The same run also passed the complete provider/intelligence/Explorer/native Windows regression path, Release x64 build, clean package build and independent package verification.

## What this does not enable

This foundation does **not** enable Create or Convert in the UI. It does not define any image-format writer and it does not make an incomplete conversion recoverable after arbitrary external filesystem/device failure.

The separate 0.6 `cancellation/rollback safety` deliverable remains open until real mutating pipelines use this transaction boundary and prove pipeline-level rollback semantics.

## Required usage for future writers

A future mutating pipeline must:

1. validate its source and destination before invoking `SafeOutputService`
2. write only through the temporary output stream supplied by the service
3. propagate cancellation rather than swallowing it
4. avoid side effects outside the transaction unless those side effects have their own rollback plan
5. enable a user-visible capability only after format-specific tests plus the shared transaction tests are green
