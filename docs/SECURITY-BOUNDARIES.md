# Dragon DiskForge — Security Boundaries

This document records the repeatable security review for Dragon DiskForge's current pre-beta architecture. It is intentionally capability-oriented: a green host-side check proves the stated code/package boundary, not the absence of every possible vulnerability and not the manual beta gates that still require a real desktop environment.

## Security goals

Dragon DiskForge follows these rules:

- inspection and analysis are read-only by default;
- unsupported operations fail closed rather than being simulated;
- normal desktop operation does not request ambient administrator elevation;
- destructive physical-media execution is isolated behind destination identity, topology, capacity and explicit-confirmation gates;
- the public CLI does not expose the destructive physical-media writer;
- Windows shell integration is per-user and reversible and does not replace Windows `UserChoice` defaults;
- release packages exclude debug/test payloads and bind shipped entry points by SHA-256;
- crash/support evidence is bounded and deliberately excludes raw exception messages, source-file paths and image content;
- parsers and guest readers use bounded reads and are covered by malformed/truncated/out-of-bounds regression cases appropriate to each implemented format.

## Reviewed boundaries

### 1. Process elevation boundary

`src/DragonDiskForge.App/app.manifest` explicitly declares `asInvoker` with `uiAccess="false"`. The normal desktop process therefore does not opt into ambient elevation. Operations that may require additional Windows privileges must fail truthfully when the current token is insufficient; they must not silently change the application's execution level.

The security-boundary CI gate rejects `requireAdministrator` and `highestAvailable` if either is introduced into the desktop manifest.

### 2. Physical-media destructive boundary

The physical writer remains separated from normal read-only workflows.

The reviewed contract requires all of the following before a write plan may become eligible:

- a regular image-file source rather than another physical-device path;
- a concrete destination `PhysicalDriveN` identity;
- stable hardware identity evidence;
- known destination capacity large enough for the source;
- explicit refusal of the Windows system disk;
- exact destination-bound confirmation;
- Windows preflight that revalidates target identity/topology before write access;
- the dedicated disposable-media harness for destructive hardware validation.

The CI environment is required to remain non-destructive. `.github/workflows/disposable-physical-media-guard.yml` fails if `DDF_DISPOSABLE_WRITE_OPT_IN` is present and executes the disposable harness only without destructive opt-in.

The final 0.7 milestone still requires real dedicated-media validation. Host-side security tests do not substitute for that hardware gate.

### 3. Public CLI boundary

The public `dragon-diskforge.exe` CLI is intended for inspection, verification, reporting, portability state and diagnostics. The repeatable security gate checks that the CLI source does not reference the physical-media execution service, Windows physical-media write sink or disposable-writer opt-in contract.

Adding a destructive CLI command requires an explicit roadmap/security decision and new release-gate evidence; it must not arrive incidentally through shared service registration.

### 4. Windows shell boundary

Shell integration is rooted in `HKCU\Software\Classes`. It registers Dragon DiskForge for Open With discovery and an explicit context-menu verb. It does not write HKLM and does not modify Windows `UserChoice` default-app state.

Application paths are normalized and quote characters are refused before command registration. The registered command quotes both the executable and `%1` image argument. The security gate exercises these command-construction rejection paths in addition to the existing isolated registry smoke tests.

### 5. Parser and image-content boundary

Format parsers and guest readers must treat disk-image bytes as untrusted input. Existing format-specific smoke tests cover malformed, truncated, out-of-range and cancellation cases where applicable. Important bounded components include partition intelligence, filesystem recognition/depth, UDF traversal, QCOW2/VMDK guest readers and guest partition/filesystem intelligence.

Security-review completion depends on those regression suites remaining green together with the dedicated security-boundary gate and the full Windows build.

This project does not claim that the current parsers constitute a formally verified sandbox. New parsers must preserve explicit bounds and capability isolation and add negative-path tests before support claims are expanded.

### 6. Output and package boundary

Application-created output uses the safe-output transaction boundary so interrupted writes do not silently replace a good destination with partial data.

The clean Windows package pipeline:

- excludes `.pdb` files;
- rejects test-only payloads;
- requires exactly one desktop entry point;
- publishes self-contained CLI and shell-helper entry points;
- records SHA-256 for all three shipped executable entry points in the package manifest;
- emits a ZIP SHA-256 sidecar;
- independently reopens and verifies the package before artifact publication.

The final public beta still requires its own independently downloadable package, final beta version suffix, checksum and clean-machine validation.

### 7. Crash/support evidence boundary

Crash collection is best-effort and bounded. Persisted evidence is limited to structured metadata such as exception type, HRESULT, fingerprint, exception-chain type names and method-only stack frames. Raw exception messages, source-file paths and image content are intentionally excluded. Diagnostic ZIP creation reuses the safe-output transaction boundary and caps included crash summaries.

## Automated security gate

`tests/DragonDiskForge.SecurityBoundary.SmokeTests` and `.github/workflows/security-boundary.yml` make the high-value architectural invariants above regression-testable. The gate currently checks:

- explicit `asInvoker` / no ambient elevation request;
- HKCU-only shell integration with no `UserChoice` modification;
- no destructive physical-media writer exposure through the public CLI;
- clean-package debug/test exclusion and SHA-256 entry-point binding;
- destructive CI opt-in remains absent;
- system-disk, unstable-identity, unknown-capacity, undersized-media and physical-source write plans fail closed;
- destination-bound confirmation remains exact and case-sensitive;
- shell command quoting rejects executable substitution and quote injection.

## Manual gates that remain manual

The security review does not replace these beta requirements:

- clean-machine launch and core workflow regression;
- normal-user UAC behavior on a real supported Windows desktop;
- real cross-process Explorer drag-out;
- real dedicated disposable-media write/read-back validation for milestone 0.7.

Any security finding that invalidates a claimed capability must reopen the relevant roadmap item and block release until corrected and reverified.
