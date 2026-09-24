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
- [x] per-user Windows x64 installer definition and fail-closed build/verification contract exist
- [ ] retained candidate includes an installer that passes silent install/uninstall verification
- [x] candidate package contains the fail-closed `tools/beta-manual-qa.ps1` evidence tool and binds its SHA-256 in package manifest schema 5
- [x] the manual-QA evidence contract self-tests under PowerShell 7 and Windows PowerShell 5.1, independently re-verifies the packaged tool hash, rejects evidence produced by a different running QA script and rejects underspecified passing observation notes
- [x] retained independently downloadable `0.5.0-beta.1` Windows x64 candidate from green `main`
- [x] retained evidence schema v2 binds the packaged desktop witness companion manifest, verifier, helper and guide by SHA-256
- [x] retained evidence additionally binds the package-specific live-session continuity manifest, verifier, helper and guide by SHA-256
- [x] retained evidence additionally binds the package-specific real-Explorer witness verifier, helper and guide by SHA-256
- [x] retained evidence binds the exact packaged normal-user UAC witness tool and before/after pair verifier by SHA-256
- [ ] package launches on a clean supported Windows machine
- [x] package is self-contained and does not require a developer SDK/Visual Studio for the verified packaged runtime paths
- [x] final beta version suffix embedded in application assemblies
- [x] application icon and version metadata verified in a `0.5.0-beta.1` candidate after suffix promotion
- [ ] final public package SHA-256 published with the Release

<!-- retained-beta-candidate:start -->
Current retained candidate: Beta Candidate run #266 (`35995158447`), artifact `DragonDiskForge-0.5.0-beta.1-win-x64-candidate-35995158447`, built from `main` source commit `3ccad9f0906da0bb9ff6e376191321bf9f4d5135`; nested package SHA-256 `5ac7b85844b92ce94bcfeba40432a6dbce8784c401b325e079c763ed64c1c832`. Evidence is witness-bound (schema v2), archive-bound, live-session-bound, Explorer-witness-bound and package-UAC-witness-bound to the packaged desktop witness, portable evidence-archive, package-specific live continuity, package-specific real-Explorer witness, packaged normal-user UAC witness and packaged UAC before/after pair-verifier companions; no human gate is claimed. It is a non-public engineering candidate, not a public release. Authoritative retained evidence: [`retained-beta-candidate.json`](retained-beta-candidate.json).
<!-- retained-beta-candidate:end -->

Independent retained-artifact read-back confirms GitHub artifact SHA-256 `05d0d7d88140c8a7d0cc568a945dbc2c713ca687423fc5a8c3410999786650f2`, package manifest schema 6, x64, `.NET=self-contained`, `WindowsAppSDK=self-contained`, `VisualCpp=app-local`, exactly one desktop entry point, no PDB payloads and SHA-256-bound desktop/manual-QA/UAC-witness/UAC-pair-verifier entry points. The nested package SHA-256 is `5ac7b85844b92ce94bcfeba40432a6dbce8784c401b325e079c763ed64c1c832`; the desktop entry point is bound as `17636ba776953e74f309d5329a1e6356b094c9273577241609795a9a37e8ff75`, the packaged manual-QA tool as `525f632943ece5e2ae72ca350015bab49d2a8149a54aa98e9a8e1fd1788a6d94`, the packaged UAC witness tool as `19897638afa6015b94355927ae07001b9ae6750242e510339966f0a2d288e431` and the packaged UAC before/after pair verifier as `e3e7f08cb278bf35e38bc425e5b4006f9da90da18a8bef2e718363b190833500`. Schema-v2 retained evidence also binds witness-kit manifest SHA-256 `e7b82cdc072fa0c0aab2e68ab9f8eb1a110358ccea4d4c66b93fe98460812821`, verifier `773412a1ca8a432e3caff928f961feaf1f3df55a8db3c1f3d9a3efc2868e5597`, desktop witness helper `5cfd082dafc86d539524970e5fbaafaa13e72ba19a67bd434debb2182c7945fc` and guide `ec1fc17b4998dda9f8fef10b0c2f71b71e1b5a9948a71bc5037f6e774b42aade`. Archive binding additionally records archive-kit manifest SHA-256 `40c85b856aaeea828b1903bc16476e8a8b9891ffb8b1015e8fedb9d74de0a53a`, verifier `091ac0ca3495caf15f708ab231a1f2a23cacfbb0d28e95339e65b4dfbc5ff4e4`, helper `d57e0f0350c4659b638e1781a92294d35608c0bf4d396d6169e126bdcf9b7ad5` and guide `d2a893c8d10842783c638ffe776b894d78498bc577d4606c4c200a095a3db3fa`. Live-session binding additionally records live-kit manifest SHA-256 `79b923ba885dbf5419e9b458db2defc88dc16f1cad494ca92075f9cd050f9ee3`, verifier `c158058c54b0d321c34ececb1df8830e7db48684b443169f061e6bcffc0a30bc`, live-session helper `97090a2bc4c6a9970cf26670c5b9886266a0d91586b16feb4c5cce745a5b5b6b` and guide `5a197d33c0880a83a3edf42380eef31b1f67b17b53fc6eaff6468adcb6f81e22`. Explorer-witness binding additionally records verifier `8f444d976bc4f8b8cfa88d61e3812c0889a77cc72e8c840771de1fcc027416dd`, real-Explorer witness helper `2cd0a7641c61440395aa62c311dce668e1da1d9fcff773ff51751e3ce2e9e62a` and guide `ba5bc328f970a409c209524f3e7c42829ab52dce01330801d6b3127e6faac6b4`. The retained package also binds the exact normal-user UAC witness script and UAC pair verifier shipped inside the ZIP. The package-only clean-machine runtime matrix also exercises packaged CLI/shell/state/diagnostic paths after developer-toolchain paths are removed. These facts close the developer-SDK dependency gate for the verified packaged runtime paths, but **do not** substitute for the remaining human-confirmed WinUI launch, UAC or real Explorer drag-out gates.

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
- [x] package-bound interactive QA evidence tooling exists and fails closed on pending, elevated, tampered, mismatched-package, mismatched-running-tool or underspecified passing-observation evidence
- [x] packaged desktop witness companion is independently hash-bound without claiming human completion
- [x] package-specific live-session continuity companion is independently hash-bound without claiming human completion
- [ ] normal-user UAC checklist completed on a desktop machine
- [ ] cross-process drag-out checklist completed on a desktop machine
- [ ] basic clean-machine launch/open/mount/explore/verify/analyze regression completed

The remaining interactive checks must be recorded against the exact retained `0.5.0-beta.1` ZIP. Manual-QA schema v3 requires the ZIP/checksum pair again for **every** recorded observation, re-verifies the package, executable and packaged QA-tool identities before saving, and refuses to initialize, record or verify unless the SHA-256 of the script that is currently executing exactly matches `tools/beta-manual-qa.ps1` from that candidate. Evidence initialization and every observation store that running-tool SHA alongside the package SHA-256. Every pass or fail record also requires a trimmed 12–1000 character observation note; final verification fails closed when a required passing observation lacks that minimum audit context. The contract records the Windows build, process architecture, interactive/elevation state, session id and UAC availability, and fails closed if a later passing observation no longer matches the clean-desktop baseline.

Passing records still require explicit human confirmation because hosted CI cannot honestly perform or observe the UAC approval/cancellation flows and real cross-process Explorer drag gestures. Schema-v1 and schema-v2 manual-QA evidence are intentionally not migrated to v3: those observations must be repeated using the exact packaged v3 tool so the stronger running-tool binding is genuine rather than inferred after the fact. The separate retained-candidate evidence schema v2 described above is release provenance, not a substitute for manual-QA evidence. Candidate #266 is the current retained package-bound target carrying live-session process-start binding plus Explorer/UAC witness provenance; this keeps preparation and integrity checks tied to the exact source/run/package while still refusing to claim human completion.

### GitHub Release
- [ ] `0.5.0-beta.1` tag
- [ ] GitHub Release marked as pre-release
- [ ] Windows x64 downloadable package
- [ ] Windows x64 per-user installer and SHA-256 checksum file
- [ ] SHA-256 checksum file
- [ ] release notes with supported/tested capabilities
- [ ] known limitations listed explicitly
- [ ] upgrade/uninstall notes if an installer is used

## Current beta readiness

**NOT READY.** The automated engineering scope is complete for the beta-targeted image-intelligence slice and current CI proves the provider/intelligence regression path, clean-package verification, truthful QCOW2/VMDK guest-byte readers, security boundaries, accessibility hardening and package-only clean-machine runtime paths. The exact retained candidate above is independently bound to its source commit, package checksum, current packaged manual-QA tool and packaged witness/archive/live-session companions. Remaining blockers are independent interactive/release gates: clean-machine interactive launch/open/mount/explore/verify/analyze regression, normal-user UAC validation, real cross-process drag-out validation and final public package/checksum/GitHub pre-release publication.

## Rule

Do not create an empty, symbolic or CI-only beta. Publish `0.5.0-beta.1` only after every required product/package/regression gate above is either completed or explicitly revised by a documented release decision backed by equivalent evidence.
