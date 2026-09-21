# Dragon DiskForge — Guarded Beta Publication

This document describes the final local promotion path for the first public GitHub beta. It does **not** make the beta ready by itself and does not replace any gate in [`BETA-RELEASE.md`](BETA-RELEASE.md).

## Purpose

`scripts/beta-release-publish.ps1` turns an already verified retained candidate plus completed interactive QA evidence into a deterministic release bundle. `publish` mode can then create the GitHub pre-release through GitHub CLI, but only after the existing exact-candidate release-proof contract succeeds.

The publisher is intentionally downstream of:

- the retained candidate metadata and SHA-256 sidecars;
- the hash-bound beta QA kit;
- schema-v3 manual QA evidence produced by the exact packaged QA tool;
- `scripts/beta-release-proof.ps1 -Mode verify`;
- the source-derived supported capability matrix.

It cannot convert pending or fabricated evidence into release readiness.

## Inputs

Use the exact files from the retained candidate artifact plus the completed manual evidence pair:

```text
artifacts/windows/DragonDiskForge-win-x64.zip
artifacts/windows/DragonDiskForge-win-x64.zip.sha256
artifacts/windows/beta-candidate.json
artifacts/windows/beta-candidate.json.sha256
artifacts/windows/beta-qa-kit.json
artifacts/windows/beta-qa-kit.json.sha256
artifacts/windows/beta-qa-kit-verify.ps1 + .sha256
artifacts/windows/beta-qa-session.ps1 + .sha256
artifacts/windows/BETA-QA-KIT.md + .sha256
artifacts/windows/BETA-MANUAL-VALIDATION.md + .sha256
artifacts/manual-qa/beta-manual-qa.json
artifacts/manual-qa/beta-manual-qa.json.sha256
```

The complete QA-kit companion set must remain beside `beta-qa-kit.json` because the existing release-proof verifier validates those hashes before any release preparation.

## Prepare without publishing

`prepare` mode is the recommended final checkpoint. It runs the exact release proof in an isolated PowerShell child process and refuses to create a bundle if the proof fails.

```powershell
.\scripts\beta-release-publish.ps1 `
  -Mode prepare `
  -ExpectedSourceCommit <40-character-retained-candidate-source-sha>
```

On success it creates:

```text
artifacts/release/0.5.0-beta.1/
├── DragonDiskForge-0.5.0-beta.1-win-x64.zip
├── DragonDiskForge-0.5.0-beta.1-win-x64.zip.sha256
├── RELEASE-NOTES-0.5.0-beta.1.md
├── release-manifest-0.5.0-beta.1.json
├── release-manifest-0.5.0-beta.1.json.sha256
└── SUPPORTED-CAPABILITIES-0.5.0-beta.1.md
```

The release manifest binds the exact source commit, retained candidate run, candidate/QA-kit/manual-evidence hashes, package hash, release-notes hash and the bundled capability-matrix snapshot hash. The manual QA JSON itself is deliberately not copied into the public release bundle.

## Publish the GitHub pre-release

Only after reviewing the prepared bundle, run from an authenticated GitHub CLI session:

```powershell
.\scripts\beta-release-publish.ps1 `
  -Mode publish `
  -ExpectedSourceCommit <40-character-retained-candidate-source-sha>
```

Publish mode:

1. re-runs the full release proof;
2. rebuilds the release bundle from the verified inputs;
3. requires authenticated `gh` access to exactly `Swir/Dragon-DiskForge`;
4. refuses to overwrite an existing `0.5.0-beta.1` tag or Release;
5. creates a GitHub **pre-release** targeted at the exact verified source commit;
6. uploads the versioned Windows x64 ZIP, checksum, release manifest + checksum and capability-matrix snapshot;
7. reads the tag and Release back and verifies the exact source commit, pre-release state and expected asset set.

A failed proof prevents publication. A pre-existing tag/release is treated as ambiguous state and is refused rather than overwritten. If GitHub accepts a new release but the immediate source/pre-release/asset read-back fails, the publisher attempts to delete only that newly created release and tag before returning failure.

## Public post-release verification

Publication is not the final proof. After GitHub has accepted the pre-release, run the independent public verifier on a Windows machine from the exact approved source tree:

```powershell
.\scripts\beta-post-release-verify.ps1 `
  -Mode verify `
  -ExpectedSourceCommit <40-character-retained-candidate-source-sha>
```

The post-release verifier deliberately reads the Release through GitHub's **public unauthenticated API** and downloads the published assets through their public `github.com` browser-download URLs. It then fails closed unless all of the following remain true after publication:

1. the tag exists publicly and resolves to the exact approved source commit;
2. the Release is published, non-draft and still marked as a pre-release;
3. the expected versioned Windows ZIP, ZIP SHA-256, release manifest + SHA-256 and capability-matrix snapshot are present exactly once and are non-empty;
4. public downloads succeed without relying on a private Actions artifact or authenticated GitHub CLI session;
5. downloaded package and release-manifest sidecars target the correct filenames and match the downloaded bytes;
6. the release manifest binds the exact tag/source commit, candidate/QA/manual-evidence provenance, package SHA-256 and capability-matrix SHA-256;
7. the downloaded public ZIP passes the existing `scripts/verify-package.ps1` runtime-completeness, manifest, embedded-tool self-test, CLI-provider and shell-helper verification on Windows.

This is intentionally separate from the publisher's immediate read-back. It proves the durable **public** assets that users can actually fetch, rather than only the local bundle or newly-created Release metadata. A successful publication must not be considered post-release verified until this command passes against the public Release.

`-KeepDownloads` may be used while diagnosing a failed verification; normal successful runs delete the temporary public-download copy automatically.

## CI contract

`.github/workflows/beta-release-publish-contract.yml` runs the publisher self-test under both PowerShell 7 and Windows PowerShell 5.1. The self-test proves that:

- a verified fixture creates a hash-bound bundle and resolved release notes;
- a failed release proof blocks preparation;
- package tampering is rejected;
- candidate/source mismatch is rejected;
- non-canonical tag spelling is rejected;
- the generated GitHub CLI argument set retains exact source, pre-release and asset binding.

`.github/workflows/beta-post-release-verify-contract.yml` separately runs the public-verifier contract under PowerShell 7 and Windows PowerShell 5.1. Its offline self-test covers release-state validation, required-asset identity, public URL scoping, release-manifest/source binding, checksum validation and rejection of package tampering. CI does **not** claim that a public Release exists; real `verify` mode is run only after publication.

The CI contracts **do not publish a Release** and do not substitute for interactive Windows QA.

## Release readiness remains separate

The public beta stays **NOT READY** until every unchecked item in [`BETA-RELEASE.md`](BETA-RELEASE.md) has real evidence. The publisher exists to make the final promotion reproducible and fail-closed after those gates pass; its existence does not change project or milestone percentages. After publication, the separate public post-release verifier must also pass before the beta can be treated as fully publication-verified.
