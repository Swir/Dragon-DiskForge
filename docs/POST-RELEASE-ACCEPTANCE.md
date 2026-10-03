# Dragon DiskForge post-release acceptance

The public beta is accepted only when the published assets are proven to be the exact retained candidate.

Required invariants:

- source commit matches the retained candidate source commit;
- public package SHA-256 matches the retained package SHA-256;
- public installer SHA-256 matches the retained installer SHA-256;
- candidate workflow run id matches the retained candidate workflow run;
- release-manifest retained evidence SHA-256 matches the canonical retained evidence file;
- verifier tooling is read back from the exact verifier commit and matches local SHA-256 values;
- the downloaded public installer passes the existing installer smoke verification;
- the installer smoke result records retainedIdentityMatched and runtimeInstallerVerified as true;
- after uninstall, product payload markers must be absent before test harness cleanup;
- clean-desktop WinUI regression, normal-user UAC, Explorer drag-out and disposable physical-media checks remain real human or physical gates.

A public 0.5.0-beta.1 release must not be treated as complete until these invariants and the existing release-proof gates are all satisfied.
