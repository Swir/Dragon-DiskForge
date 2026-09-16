# Dragon DiskForge — Beta Release Gate

The first public GitHub beta is targeted for **0.5.0-beta.1**.

A workflow artifact is useful engineering evidence, but it is **not automatically a public beta**. The beta must be independently downloadable, reproducible, tested on the supported Windows path and documented with truthful limitations.

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
- [ ] deeper supported NTFS / remaining agreed 0.5 intelligence scope
- [ ] final 0.5 beta-scope hardening checkpoint

Latest validated checkpoints include PR #32 / run #260 and PR #33 / implementation run #262. These prove automated code/build/artifact paths, not clean-machine public-beta readiness.

### Windows beta package
- [ ] reproducible independently downloadable Windows x64 beta package
- [ ] package launches on a clean supported Windows machine
- [ ] no developer SDK/Visual Studio requirement for normal users
- [ ] final beta version embedded in application assemblies
- [ ] application icon and version metadata verified in the beta package
- [ ] SHA-256 checksum generated for the public package
- [ ] artifact/package contents reviewed for debug/test-only files

### Regression and manual QA
- [x] Core smoke tests green
- [x] provider registry/fallback/isolation tests green
- [x] provider-specific tests green
- [x] partition/filesystem/intelligence/report automated gates green
- [x] ISO direct-browse integration green
- [x] native ISO/VHD/VHDX mount integration green
- [x] full WinUI Release x64 build green
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

**NOT READY.** Automated Windows CI is strong and produces a Release x64 artifact, but the agreed 0.5 engineering scope is still in progress and the clean-machine/manual/package gates above remain open.

## Rule

Do not create an empty, symbolic or CI-only beta. Publish `0.5.0-beta.1` only after every required product/package/regression gate above is either completed or explicitly revised by a documented release decision backed by equivalent evidence.
