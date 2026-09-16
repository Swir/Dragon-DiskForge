# Dragon DiskForge — Beta Release Gate

The first public GitHub beta is targeted for **0.5.0-beta.1**.

A workflow artifact is useful engineering evidence, but it is **not automatically a public beta**. The beta must be independently downloadable, reproducible through the documented pipeline, tested on the supported Windows path and documented with truthful limitations.

0.6 engineering may continue while these independent 0.5 beta release gates remain open, but that work does not waive or substitute any beta requirement below.

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
- [x] run #270 clean artifact was independently downloaded and verified
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

**NOT READY.** The automated 0.5 engineering scope is complete and current CI proves the provider/intelligence regression path, guest GPT/EBR integrity hardening, bounded UDF traversal, clean-package verification, truthful QCOW2/VMDK guest-byte readers and common bounded guest partition/filesystem analysis. Remaining blockers are independent release gates: final `0.5.0-beta.1` suffix/package promotion, clean-machine launch/regression, normal-user UAC validation, real cross-process drag-out validation and final public package/checksum/Release publication.

## Rule

Do not create an empty, symbolic or CI-only beta. Publish `0.5.0-beta.1` only after every required product/package/regression gate above is either completed or explicitly revised by a documented release decision backed by equivalent evidence.
