# Boot + Installer Intelligence

Dragon DiskForge 0.5 adds a bounded, read-only boot and installer evidence layer. The goal is to answer useful questions such as “does this ISO advertise BIOS or UEFI boot?” and “does this look like Windows or a supported Linux installer layout?” without mounting, executing or extracting content.

## Evidence model

`BootInstallerIntelligenceService` resolves an image through `ProviderRegistry`. The current implementation requires a provider with truthful `DirectBrowse` support, which today means a physical ISO9660/Joliet byte path that Core can actually inspect.

The result contains:

- provider identity and physical image size
- optional El Torito boot-catalog LBA
- bounded boot-catalog entries with platform, bootability, media type, load segment/count and image LBA
- conservative Windows/Linux installer detections with the exact evidence paths that triggered them
- architecture hints from recognized standard EFI fallback filenames or installer-directory evidence

## El Torito contract

Bootability is derived from on-disk boot metadata, not filenames.

The service:

1. scans ISO volume descriptors in the bounded LBA 16–63 descriptor window
2. accepts an El Torito boot record only with `CD001`, descriptor version 1 and system ID `EL TORITO SPECIFICATION`
3. reads the boot-catalog LBA from the boot record and verifies that the catalog lies inside the physical image
4. limits boot-catalog parsing to a 64 KiB window
5. validates the 32-byte validation entry, `0x55AA` key and 16-bit checksum
6. parses the default entry plus bounded section headers/entries
7. accepts only valid boot indicators
8. validates declared boot-image load ranges against the physical image

Platform ID `0x00` is reported as BIOS/x86 boot evidence and `0xEF` as UEFI evidence. Other platform IDs remain explicit `Other` evidence rather than being guessed.

## Installer evidence

Direct Browse traversal is deliberately bounded:

- maximum 4,096 directories
- maximum 50,000 entries
- maximum virtual depth 64
- visited-directory tracking prevents cycles
- reparse-point entries are ignored as installer evidence
- only actual files become marker evidence
- provider-returned `.` or `..` traversal-style path segments are rejected

### Windows

Windows installation media requires all of:

- `setup.exe`
- `sources/boot.wim`
- one install payload: `sources/install.wim`, `sources/install.esd` or `sources/install.swm`

A directory whose name resembles one of these files is not evidence.

### Linux

The current conservative marker sets cover:

- casper live/install media: `casper/vmlinuz`, an `initrd*` file and `casper/filesystem.squashfs`
- Debian-style installer media: supported `install.*` kernel path plus sibling initrd
- Anaconda-style install/live media: pxeboot kernel/initrd plus install or LiveOS squash payload

These are evidence categories, not distribution/version claims.

## Architecture hints

Standard EFI fallback filenames may provide bounded hints:

- `BOOTX64.EFI` → x86_64
- `BOOTIA32.EFI` → x86
- `BOOTAA64.EFI` → ARM64
- `BOOTARM.EFI` → ARM
- `BOOTRISCV64.EFI` → RISC-V 64

These are hints about boot artifacts. They do not by themselves prove the architecture of every installer payload or installed operating system.

## Non-goals and safety boundaries

This service does not:

- execute boot code or installer binaries
- mount the image as a side effect
- extract payloads
- write or repair image content
- infer BIOS/UEFI bootability from filenames
- follow filesystem reparse points
- scan guest filesystems inside sparse/compressed VMDK, QCOW/QCOW2, DMG or other containers without a real guest-sector reader
- claim installer distribution versions from weak marker strings

The absence of supported evidence is a valid result. Dragon DiskForge prefers “unknown/not detected” over inventing a capability or identity.

## Validation

`tests/DragonDiskForge.BootInstallerIntelligence.SmokeTests` generates disposable bounded ISO fixtures and validates hybrid BIOS/UEFI evidence, Windows install markers, Linux casper markers, architecture hints, malformed catalog checksums, out-of-file load ranges, Direct Browse capability gating and cancellation.

The dedicated gate runs before the existing provider/native Windows/Release x64 regression path in GitHub Actions.
