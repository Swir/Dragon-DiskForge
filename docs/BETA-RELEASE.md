# Dragon DiskForge — Beta Release Gate

The first public GitHub beta is targeted for **0.5.0-beta.1**.

## Why not publish the current development build as a beta?

Version 0.3.0 already provides a useful tested foundation, but the public beta should validate more than the original ISO/VHD/VHDX path. Milestone 0.4 establishes the provider architecture and adds more image families. Milestone 0.5 adds partition/filesystem/image intelligence that makes the application meaningfully useful across those providers.

## Required before 0.5.0-beta.1

### Product capability
- [x] 0.1 Foundation + Dragon UI complete
- [x] 0.2 Native ISO/VHD/VHDX Mount + Unmount complete
- [x] 0.3 Dragon Explorer complete
- [ ] 0.4 provider registry/fallback/isolation complete
- [ ] multiple additional 0.4 image providers proven by tests
- [ ] 0.5 partition/filesystem recognition beta scope complete
- [ ] unsupported actions remain disabled rather than simulated

### Windows beta package
- [ ] reproducible Windows x64 Release package
- [ ] package launches on a clean supported Windows machine
- [ ] no developer SDK/Visual Studio requirement for normal users
- [ ] final beta version embedded in the application assemblies
- [ ] application icon and version metadata verified
- [ ] SHA-256 checksum generated for the public package
- [ ] artifact/package contents reviewed for debug/test-only files

### Regression and manual QA
- [ ] Core smoke tests green
- [ ] provider registry/fallback/isolation tests green
- [ ] provider-specific tests green
- [ ] ISO direct-browse integration green
- [ ] native ISO/VHD/VHDX mount integration green
- [ ] full WinUI Release x64 build green
- [ ] normal-user UAC checklist completed on a desktop machine
- [ ] cross-process drag-out checklist completed on a desktop machine
- [ ] basic clean-machine launch/open/mount/explore/verify regression completed

### GitHub Release
- [ ] `0.5.0-beta.1` tag
- [ ] GitHub Release marked as pre-release
- [ ] Windows x64 downloadable package
- [ ] SHA-256 checksum file
- [ ] release notes with supported/tested capabilities
- [ ] known limitations listed explicitly
- [ ] upgrade/uninstall notes if an installer is used

## Rule

A workflow artifact is not automatically a public beta. A beta is published only when the package is independently downloadable, reproducible, tested on the supported Windows path and its limitations are documented.
