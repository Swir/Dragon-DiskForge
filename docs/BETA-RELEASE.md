# Dragon DiskForge — Beta Release Gate

The first public GitHub beta is targeted for **0.5.0-beta.1**.

A workflow artifact is useful engineering evidence, but it is **not automatically a public beta**. The beta must be independently downloadable, reproducible through the documented pipeline, tested on the supported Windows path and documented with truthful limitations.

Later engineering may continue while these independent beta release gates remain open, but that work does not waive or substitute any beta requirement below.

## Required before 0.5.0-beta.1

### Product capability
- [x] 0.1 Foundation + Dragon UI complete
- [x] 0.2 Native ISO/VHD/VHDX Mount + Unmount complete
- [x] 0.3 Dragon Explorer complete
- [x] 0.4 provider registry/fallback/isolation complete
- [x] multiple additional 0.4 image providers proven by tests
- [x] 0.5 partition/filesystem/image-intelligence beta engineering scope complete
- [x] unsupported actions remain disabled rather than simulated

### Current 0.5 automated evidence
- [x] cross-provider partition intelligence
- [x] bounded filesystem recognition
- [x] boot/installer intelligence
- [x] unified identity/health intelligence foundation
- [x] Windows Analyze + text/JSON reporting surface
- [x] deeper bounded exFAT/FAT32/UDF filesystem evidence
- [x] bounded NTFS `$MFT` / `$MFTMirr` metadata-depth validation
- [x] cross-source boot/installer architecture reconciliation without guessed conflict resolution
- [x] centralized semantic development-version metadata
- [x] independently verified clean-package candidate gate
- [x] independently bounded UDF root-directory traversal for validated Type 1 physical mappings
- [x] bounded QCOW2 v2/v3 standard uncompressed guest-byte reader foundation
- [x] bounded hosted sparse VMDK v1 standard uncompressed guest-byte reader foundation
- [x] common guest-byte integration into bounded partition/filesystem intelligence
- [x] final 0.5 beta-scope hardening checkpoint

Validated automated checkpoints include PR #35 / run #270, PR #36 / run #274, PR #37 / run #277, PR #38 / run #284, PR #39 / run #287 and PR #40 / implementation run #289. PR #40 was merged after the final documentation-synchronized CI checkpoint, closing the automated 0.5 engineering scope.

### Windows beta package
- [x] clean Windows x64 package-candidate pipeline exists and is gated after the Release build
- [x] candidate package requires exactly one `DragonDiskForge.App.exe`
- [x] candidate package excludes `.pdb` and test-only payloads
- [x] candidate package emits a manifest and SHA-256 sidecar
- [x] central product version metadata feeds executable and package metadata
- [x] candidate package manifest records semantic version, ProductVersion/FileVersion, entry-point hash, icon and architecture
- [x] independent CI verification reopens the ZIP and validates checksum/manifest/version/hash/icon/content policy before artifact upload
- [x] canonical Dragon icon is included in the clean package root
- [x] candidate package contains the fail-closed `tools/beta-manual-qa.ps1` evidence tool and binds its SHA-256 in package manifest schema 5
- [x] the manual-QA evidence contract self-tests under PowerShell 7 and Windows PowerShell 5.1, independently re-verifies the packaged tool hash and rejects evidence produced by a different running QA script
- [x] retained independently downloadable `0.5.0-beta.1` Windows x64 candidate from green `main`
- [x] retained evidence schema v2 binds the packaged desktop witness companion manifest, verifier, helper and guide by SHA-256
- [ ] package launches on a clean supported Windows machine
- [x] package is self-contained and does not require a developer SDK/Visual Studio for the verified packaged runtime paths
- [x] final beta version suffix embedded in application assemblies
- [x] application icon and version metadata verified in a `0.5.0-beta.1` candidate after suffix promotion
- [ ] final public package SHA-256 published with the Release

<!-- retained-beta-candidate:start -->
Current retained candidate: Beta Candidate run #113 (`35327348417`), artifact `DragonDiskForge-0.5.0-beta.1-win-x64-candidate-35327348417`, built from `main` source commit `48054cc5036ca3809915b3f8392599c315bf2ec7`; nested package SHA-256 `8baeddfed293167e54d827ed1c11e511a3d69c3a80af2a567dfe66e4b78200e7`. Evidence is witness-bound (schema v2) and archive-bound to the packaged desktop witness companion and portable evidence-archive companion and does not claim any human gate. It is a non-public engineering candidate, not a public release. Authoritative retained evidence: [`retained-beta-candidate.json`](retained-beta-candidate.json).
<!-- retained-beta-candidate:end -->

Independent retained-artifact read-back confirms GitHub artifact SHA-256 `6e99afebcaea09b38d3a110abc0ee1509a5558b4f87262e9a51923348f8000e7`, package manifest schema 5, x64, `.NET=self-contained`, `WindowsAppSDK=self-contained`, `VisualCpp=app-local`, exactly one desktop entry point, no PDB payloads and SHA-256-bound desktop/manual-QA entry points. Schema-v2 retained evidence also binds witness-kit manifest SHA-256 `17a0d477da1c98372cfa9d7882fa2e5ec1d15c5fba65580b2f51e750be95b13c`, verifier `773412a1ca8a432e3caff928f961feaf1f3df55a8db3c1f3d9a3efc2868e5597`, desktop witness helper `5cfd082dafc86d539524970e5fbaafaa13e72ba19a67bd434debb2182c7945fc` and guide `aa7ed02a023aea854e9ea6f791f75cb63480cf005e1fa52412a9f0f43b45058b`. The package-only clean-machine runtime matrix also exercises packaged CLI/shell/state/diagnostic paths after developer-toolchain paths are removed. These facts close the developer-SDK dependency gate for the verified packaged runtime paths, but **do not** substitute for the remaining human-confirmed WinUI launch gate.

### Regression and manual QA
- [x] Core smoke tests green
- [x] provider registry/fallback/isolation tests green
- [x] provider-specific tests green
- [x] partition/filesystem/intelligence/report automated gates green
- [x] bounded UDF traversal gate green
- [x] NTFS/architecture hardening gate green
- [x] QCOW2 guest-byte reader gate green
- [x] VMDK hosted sparse guest-byte reader gate green
- [x] guest partition/filesystem intelligence gate green
- [x] guest GPT/EBR integrity hardening gate green
- [x] ISO direct-browse integration green
- [x] native ISO/VHD/VHDX mount integration green
- [x] full WinUI Release x64 build green
- [x] versioned clean ZIP candidate build/checksum/verification gate green
- [x] package-bound interactive QA evidence tooling exists and fails closed on pending, elevated, tampered, mismatched-package or mismatched-running-tool evidence
- [x] packaged desktop witness companion is independently hash-bound without claiming human completion
- [ ] normal-user UAC checklist completed on a desktop machine
- [ ] cross-process drag-out checklist completed on a desktop machine
- [ ] basic clean-machine launch/open/mount/explore/verify/analyze regression completed

The remaining interactive checks must be recorded against the exact retained `0.5.0-beta.1` ZIP. Manual-QA schema v3 requires the ZIP/checksum pair again for **every** recorded observation, re-verifies the package, executable and packaged QA-tool identities before saving, and refuses to initialize, record or verify unless the SHA-256 of the script that is currently executing exactly matches `tools/beta-manual-qa.ps1` from that candidate. Evidence initialization and every observation store that running-tool SHA alongside the package SHA-256. The contract also records the Windows build, process architecture, interactive/elevation state, session id and UAC availability, and fails closed if a later passing observation no longer matches the clean-desktop baseline.

Passing records still require explicit human confirmation because hosted CI cannot honestly perform or observe the UAC approval/cancellation flows and real cross-process Explorer drag gestures. Schema-v1 and schema-v2 manual-QA evidence are intentionally not migrated to v3: those observations must be repeated using the exact packaged v3 tool so the stronger running-tool binding is genuine rather than inferred after the fact. The separate retained-candidate evidence schema v2 described above is release provenance, not a substitute for manual-QA evidence.

### GitHub Release
- [ ] `0.5.0-beta.1` tag
- [ ] GitHub Release marked as pre-release
- [ ] Windows x64 downloadable package
- [ ] SHA-256 checksum file
- [ ] release notes with supported/tested capabilities
- [ ] known limitations listed explicitly
- [ ] upgrade/uninstall notes if an installer is used

## Current beta readiness

**NOT READY.** The automated engineering scope is complete for the beta-targeted image-intelligence slice and current CI proves the provider/intelligence regression path, clean-package verification, truthful QCOW2/VMDK guest-byte readers, security boundaries, accessibility hardening and package-only clean-machine runtime paths. The exact retained candidate above is independently bound to its source commit, package checksum and packaged witness companion. Remaining blockers are independent interactive/release gates: clean-machine interactive launch/open/mount/explore/verify/analyze regression, normal-user UAC validation, real cross-process drag-out validation and final public package/checksum/GitHub pre-release publication.

## Rule

Do not create an empty, symbolic or CI-only beta. Publish `0.5.0-beta.1` only after every required product/package/regression gate above is either completed or explicitly revised by a documented release decision backed by equivalent evidence.
