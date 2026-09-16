# Dragon DiskForge — Beta Release Gate

The first public GitHub beta is targeted for **0.5.0-beta.1**.

A workflow artifact is useful engineering evidence, but it is **not automatically a public beta**. The beta must be independently downloadable, reproducible through the documented pipeline, tested on the supported Windows path and documented with truthful limitations.

## Required before 0.5.0-beta.1

### Product capability
- [x] 0.1 Foundation + Dragon UI complete
- [x] 0.2 Native ISO/VHD/VHDX Mount + Unmount complete
- [x] 0.3 Dragon Explorer complete
- [x] 0.4 provider registry/fallback/isolation complete
- [x] multiple additional 0.4 image providers proven by tests
- [ ] 0.5 partition/filesystem/image-intelligence beta scope complete
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
- [ ] independently bounded UDF traversal where justified
- [ ] truthful guest-sector reader foundation for sparse/compressed virtual disks
- [ ] final 0.5 beta-scope hardening checkpoint

Latest validated checkpoints include PR #34 / run #266 and PR #35 / implementation run #270. Run #270 proves the complete existing automated test/native/build path plus clean-package build, independent ZIP verification and both artifact uploads.

### Windows beta package
- [x] clean Windows x64 package-candidate pipeline exists and is gated after the Release build
- [x] candidate package requires exactly one `DragonDiskForge.App.exe`
- [x] candidate package excludes `.pdb` and test-only payloads
- [x] candidate package emits a manifest and SHA-256 sidecar
- [x] central product version metadata feeds executable and package metadata
- [x] candidate package manifest records semantic version, ProductVersion/FileVersion, entry-point hash, icon and architecture
- [x] independent CI verification reopens the ZIP and validates checksum/manifest/version/hash/icon/content policy before artifact upload
- [x] canonical Dragon icon is included in the clean package root
- [x] run #270 clean artifact was independently downloaded after CI: sidecar SHA-256 matched the nested ZIP; manifest version was `0.5.0-alpha.1`; executable SHA matched the manifest; one application EXE and zero PDB files were present
- [ ] final independently downloadable `0.5.0-beta.1` Windows x64 package
- [ ] package launches on a clean supported Windows machine
- [ ] no developer SDK/Visual Studio requirement for normal users
- [ ] final beta version suffix embedded in application assemblies
- [ ] application icon and version metadata verified in the final beta package after suffix promotion
- [ ] final public package SHA-256 published with the Release

### Regression and manual QA
- [x] Core smoke tests green
- [x] provider registry/fallback/isolation tests green
- [x] provider-specific tests green
- [x] partition/filesystem/intelligence/report automated gates green
- [x] NTFS/architecture hardening gate green
- [x] ISO direct-browse integration green
- [x] native ISO/VHD/VHDX mount integration green
- [x] full WinUI Release x64 build green
- [x] versioned clean ZIP candidate build/checksum/verification gate green
- [ ] normal-user UAC checklist completed on a desktop machine
- [ ] cross-process drag-out checklist completed on a desktop machine
- [ ] basic clean-machine launch/open/mount/explore/verify/analyze regression completed

### GitHub Release
- [ ] `0.5.0-beta.1` tag
- [ ] GitHub Release marked as pre-release
- [ ] Windows x64 downloadable package
- [ ] SHA-256 checksum file
- [ ] release notes with supported/tested capabilities
- [ ] known limitations listed explicitly
- [ ] upgrade/uninstall notes if an installer is used

## Current beta readiness

**NOT READY.** Automated Windows CI now proves the full existing regression path plus a versioned clean ZIP candidate, checksum and independent package-verification gate. The remaining blockers are the unfinished agreed 0.5 engineering scope (independently bounded UDF traversal where justified and truthful sparse/compressed guest-sector reader foundations), final beta-scope hardening, final `0.5.0-beta.1` suffix promotion, and clean-machine/normal-user UAC/real cross-process drag-out/manual regression.

## Rule

Do not create an empty, symbolic or CI-only beta. Publish `0.5.0-beta.1` only after every required product/package/regression gate above is either completed or explicitly revised by a documented release decision backed by equivalent evidence.
