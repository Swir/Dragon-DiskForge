### Current retained checkpoint

The authoritative retained candidate is **Beta Candidate run #113 (`35327348417`)**, artifact `DragonDiskForge-0.5.0-beta.1-win-x64-candidate-35327348417`, built from `main` commit `48054cc5036ca3809915b3f8392599c315bf2ec7`. The nested package SHA-256 is `8baeddfed293167e54d827ed1c11e511a3d69c3a80af2a567dfe66e4b78200e7`; the GitHub artifact SHA-256 is `014d8971c4434c03ed0577848f968fa1bd1f00294f93796d0e9c18bf206645fe`.

This candidate was intentionally selected by the PR #89 merge after the portable evidence-archive companion was added to the exact-package QA kit. The final PR head passed all required pull-request workflows, and the selected `main` commit then passed Build #506 plus Beta Candidate #113. Independent artifact read-back verified the outer artifact digest, every supplied SHA-256 sidecar, package manifest schema 5, x64 architecture, `.NET=self-contained`, `WindowsAppSDK=self-contained`, `VisualCpp=app-local`, the desktop entry-point hash and the packaged manual-QA-tool hash.

Canonical retained evidence remains schema v2 for compatibility, is witness-bound to the packaged desktop witness companion, and now carries separately verified archive-binding fields for the portable evidence-archive manifest, verifier, helper and guide. The dedicated retained archive-binding contract fails closed if those hashes, the candidate identity, or the synchronized documentation drift. None of these bindings claims a human gate.

The authoritative machine-readable record is [`retained-beta-candidate.json`](retained-beta-candidate.json). It deliberately keeps `publicRelease=false` and `betaReady=false`.

Earlier candidate checkpoints remain historical evidence only. In particular, candidates before the PR #82 same-session binding, PR #87 witness binding or PR #89 portable archive kit must not be mixed with the current manual-QA evidence path.

This reselection does not complete or waive any manual release gate. Clean-desktop visible WinUI launch/basic regression, normal-user UAC behavior and real cross-process Explorer/Desktop drag-out still require human observations against the exact retained package, and the separate 0.7 physical-writer gate still requires dedicated disposable media. Until that evidence exists, the candidate remains non-public and `0.5.0-beta.1` must not be published as a GitHub Release.
