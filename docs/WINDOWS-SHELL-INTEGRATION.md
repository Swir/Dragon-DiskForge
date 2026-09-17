# Dragon DiskForge — Windows Shell Integration

Dragon DiskForge ships an explicit, reversible **per-user** Windows shell-integration helper in the clean Windows package:

```text
tools/dragon-diskforge-shell.exe
```

The helper does not require administrator rights because it writes only below the current user's `HKCU\Software\Classes` registry hive.

## Register

From the extracted Dragon DiskForge package:

```powershell
.\tools\dragon-diskforge-shell.exe register
```

Registration adds:

- Dragon DiskForge to the per-user `Applications\DragonDiskForge.App.exe` registration with the canonical supported image-extension set;
- an explicit **Open with Dragon DiskForge** shell verb for every extension in `SupportedFormats`;
- the application icon and a safely quoted single-file open command.

The helper deliberately **does not** replace the Windows default application and does not write a `UserChoice` override. Users remain in control of their default file handlers.

On Windows 11, classic per-user shell verbs may be shown under **Show more options** depending on the current Explorer shell behavior. Explorer may also cache shell registrations until a new window/session is opened.

## Status

```powershell
.\tools\dragon-diskforge-shell.exe status
```

The command reports whether the Dragon-owned application registration and every canonical image-extension verb point to the current package's `DragonDiskForge.App.exe`.

## Unregister

```powershell
.\tools\dragon-diskforge-shell.exe unregister
```

Unregister is idempotent and removes only Dragon DiskForge-owned application/verb keys. It leaves unrelated shell handlers intact.

## File-open activation

The unpackaged WinUI application accepts a supported existing image path supplied on its command line. Shell-launched images enter the same `LoadImageAsync` inspection path as picker/drop/library opens. An explicit launch image takes precedence over best-effort last-session restoration, preventing two competing startup opens.

Unsupported extensions, missing paths, switches and malformed paths are ignored by the startup resolver rather than guessed.

## Verification boundary

Automated Windows CI proves:

- the extension list is derived from the canonical `SupportedFormats` registry;
- registration creates the expected isolated per-user registry contract;
- no `UserChoice`/default-association override is created;
- every supported extension receives the Dragon-owned context-menu verb;
- repeated register/unregister calls are safe;
- unregister preserves unrelated verbs;
- supported command-line image activation resolves correctly;
- the clean package contains a self-contained shell helper whose SHA-256 is recorded and re-verified from the package manifest.

These automated checks prove the implementation and package contract. They do not claim a human visual check of every Explorer menu variant on every Windows build; clean-machine/manual Explorer validation belongs to the later quality/release-hardening track.
