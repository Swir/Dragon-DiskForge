# Dragon DiskForge — Clean-machine package runtime matrix

This gate validates the **clean Windows x64 package as a package**, not as a source checkout.

## What the workflow proves

`Dragon DiskForge Clean Machine Runtime` builds the normal clean package once, then hands only these files to fresh Windows runner jobs:

- `DragonDiskForge-win-x64.zip`
- `DragonDiskForge-win-x64.zip.sha256`
- `clean-machine-runtime.ps1`

The runtime jobs intentionally do **not** check out the repository. They run on a fixed Windows runner generation plus the current `windows-latest` image. The probe removes .NET/Visual Studio/Git toolchain directories from `PATH` before launching Dragon package entry points so the self-contained runtime checks do not accidentally succeed through the development SDK.

Each runtime job verifies:

1. the external ZIP SHA-256 sidecar;
2. package manifest schema, architecture and debug-symbol policy;
3. SHA-256 binding for the desktop, CLI and shell entry points;
4. absence of PDB/test-only files;
5. self-contained CLI launch and canonical provider enumeration;
6. package-only `analyze` and dual SHA-256/SHA-512 `verify` on a generated image fixture;
7. isolated state/settings operation and sanitized diagnostic ZIP creation;
8. self-contained shell-helper launch plus per-user register → status → unregister on the disposable runner profile;
9. machine-readable evidence containing Windows version/build, package hashes and runtime results.

Evidence JSON is uploaded separately for every matrix runner.

## What it deliberately does not prove

This automated gate does **not** replace the independent public-beta manual checks for:

- a human-confirmed WinUI desktop launch on a clean supported end-user Windows installation;
- normal-user UAC behavior for operations that legitimately require Windows elevation;
- real cross-process drag-out into Explorer;
- dedicated disposable physical-media writer validation;
- visual accessibility/screen-reader review.

Those remain separate gates because a hosted CI runner is not equivalent to an interactive end-user desktop session or dedicated physical test hardware.

## Safety

The runtime probe never enables or invokes the physical-media writer. The shell integration exercise is per-user and is removed in a `finally` block. Image operations use only a generated temporary fixture, and all temporary files are removed after the probe.
