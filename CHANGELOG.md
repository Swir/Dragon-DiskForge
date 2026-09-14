# Changelog

All notable changes to Dragon DiskForge will be documented here.

The project follows semantic versioning while it evolves toward 1.0.

## [Unreleased]

### Added
- Dragon visual design system with obsidian, charcoal, ember, molten and crimson design tokens
- Custom Dragon DiskForge dragon-head/sigil in the application shell
- Vector Dragon sigil asset under `docs/branding/dragon-sigil.svg`
- Branded Forge dashboard and disk-image drop zone
- Dragon-styled capability cards, status pills and image action card
- Light-theme translation of the core Dragon color tokens

### Changed
- Reworked the generic WinUI dashboard into the first recognizable Dragon DiskForge interface
- Kept Mount and Explore disabled until their real engine milestones are implemented

### Planned
- Native ISO/VHD/VHDX mount and unmount service
- Mounted-drive state detection
- In-app Dragon Explorer
- Open and extract files from supported images
- Recent-image history

## [0.1.0] - 2026-09-14

### Added
- Initial WinUI 3 / .NET 10 application shell
- Separate `DragonDiskForge.Core` engine project
- Drag-and-drop image loading and file picker
- Supported-format catalogue
- Signature-based detection for ISO, VHD, VHDX, QCOW2, DMG and WIM/ESD
- SHA-256 verification
- Initial roadmap and developer scripts
