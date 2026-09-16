# Dragon DiskForge 0.6.0-alpha.1

Windows x64 development release. This release does not claim completion of the 1.0 roadmap.

## Start
Extract the entire ZIP, then open **Start Dragon DiskForge.cmd** or **app/DragonDiskForge.App.exe**.
.NET and Windows App SDK runtimes are included. Visual Studio and the .NET SDK are not required.
Use **cli/ddf.exe --help** for command-line operations. Keep all files together.

## Included
- Read-only ISO/VHD/VHDX mount/unmount and mounted-volume Explorer.
- Direct ISO9660/Joliet browsing, search and safe extraction.
- Image-analysis dialog and JSON reports: partitions, filesystem recognition, boot/installer evidence, identity and bounded health findings.
- SHA-256 and SHA-512; optional expected-checksum comparison in Tools and CLI.
- Blank RAW, VHD and VHDX creation.
- ISO9660/Joliet data ISO creation from a folder (not bootable-image authoring).
- Standalone RAW/IMG/IMA, VHD and VHDX conversion to RAW, dynamic VHD or dynamic VHDX.
- GZip compression, bounded decompression and split/join with SHA-256 for each part.
- Source preservation, overwrite refusal, transactional outputs and cancellation.
- Shared-Core CLI with JSON analysis and meaningful exit codes.

## Limits
- Alpha: interactive non-admin UAC and cross-application drag/drop require the manual desktop checklist.
- Unsigned package: a publisher signing certificate is not part of this repository.
- QCOW, VMDK, DMG, WIM and FFU expose bounded metadata; generic extraction/conversion of these formats is not provided.
- Differencing VHD/VHDX conversion is rejected. Inputs are opened read-only.
- ISO creation rejects reparse points and files over 4 GiB minus one byte; at most 100,000 entries and 64 levels.
- No physical-disk writes, repair, secure erase, automatic updates or system-wide file associations.
- UI is English. EN/PL localization, deeper filesystem readers and accessibility regression remain roadmap work.
- A computed hash is not proof of authenticity; compare it with a trusted checksum.
- Health findings cover inspected metadata only and do not establish whole-image integrity.

## Uninstall / data
Close the app and remove the extracted folder. Local history and favorites are stored under **%LOCALAPPDATA%/DragonDiskForge**.
Deleting that folder resets local history; images are not stored there.

## Verification
Compare the ZIP's SHA-256 with the accompanying .sha256 file.
Automated gates cover provider/Core regression, conversion and ISO round trips, Windows mounting/direct ISO integration,
Release x64 compilation, packaged CLI startup and packaged GUI window startup.
These checks do not substitute for a clean-machine interactive desktop regression.
