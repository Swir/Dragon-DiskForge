# Dragon DiskForge architecture

## Goals

Dragon DiskForge is designed as a Windows-native disk-image workstation rather than a thin wrapper around one command-line tool. The UI and disk-image engine are separated so the same engine can later power the desktop app, CLI and automated workflows.

## Projects

### `DragonDiskForge.App`
WinUI 3 desktop front end. It owns presentation, navigation, drag-and-drop, user confirmations and Windows shell integration.

### `DragonDiskForge.Core`
UI-independent engine. It owns format identification, image metadata, provider contracts, verification and the future mount/extract/convert services.

## Provider model

Every image family should be handled through a provider with explicit capabilities. Providers may support any combination of:

- Detect
- Inspect
- Browse
- Extract
- Mount / unmount
- Create
- Convert
- Verify

A provider must never claim a capability it cannot perform safely.

## Safety boundaries

Read-only inspection is the default. Operations that can modify disks, removable media or images must be separated from inspection services and require explicit user intent. Bootable-USB operations must validate the target device and show destructive-action confirmation before writing.

## Planned service layers

- `ImageDetectionService` - identifies an image using extension plus signatures
- `ImageInspectionService` - filesystem, partition and boot metadata
- `MountService` - native Windows ISO/VHD/VHDX lifecycle and provider-backed mounts
- `ImageExplorerService` - tree enumeration and file access
- `ExtractionService` - safe extraction with path traversal protection
- `VerificationService` - SHA-256/SHA-512 and legacy MD5 verification
- `ConversionService` - provider-based image conversion
- `UsbWriterService` - guarded bootable-media creation
- `RecentImagesService` - local history without uploading image metadata

## Versioning

- `0.1.x` foundation and detection
- `0.2.x` mounting and explorer
- `0.3.x` extended providers
- `0.4.x` create and conversion pipeline
- `0.5.x` USB and power tools
- `1.0.0` production release

The roadmap is the source of truth for feature status and must be updated whenever a milestone changes.
