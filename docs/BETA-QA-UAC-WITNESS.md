# Dragon DiskForge — UAC Witness Companion

This helper supports the remaining **normal-user UAC validation** gate for the retained Windows x64 beta candidate. It records objective environment and disk-image state around a human-observed UAC action. It **never** marks the UAC gate complete by itself.

## What it proves

Each witness is bound to:

- the exact candidate ZIP SHA-256 and checksum sidecar;
- package manifest schema/version/architecture;
- the packaged desktop entry-point SHA-256;
- the packaged `tools/beta-manual-qa.ps1` SHA-256;
- the exact `beta-qa-uac-witness.ps1` helper SHA-256;
- an interactive, unelevated Windows desktop session with `EnableLUA=1`;
- current UAC policy values relevant to consent behavior;
- the selected UAC checklist id and `before`/`after` phase;
- a privacy-preserving SHA-256 fingerprint of the tested ISO/VHD/VHDX path;
- the current Windows `Get-DiskImage` attachment state and, when available, disk read-only state.

The witness keeps `humanGateClaimed=false` and `publicRelease=false`. Secure-desktop consent/cancellation remains a human observation and must still be recorded with the candidate's packaged `beta-manual-qa.ps1`.

## Supported checklist ids

- `uac.iso-no-prompt`
- `uac.vhd-cancel`
- `uac.vhd-approve-readonly`
- `uac.vhdx-cancel`
- `uac.vhdx-approve-readonly`
- `uac.unmount-refresh`

## Capture workflow

Run the helper from a normal, unelevated interactive Windows session. Use the exact retained candidate ZIP and its `.sha256` file.

```powershell
# Before the human-observed action
.\scripts\beta-qa-uac-witness.ps1 `
  -Mode capture `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -Check uac.vhd-cancel `
  -Phase before `
  -ImagePath C:\QA\fixture.vhd `
  -EvidencePath .\uac-vhd-cancel-before.json

# Perform the action in Dragon DiskForge and explicitly observe the UAC prompt/result.

# After the action
.\scripts\beta-qa-uac-witness.ps1 `
  -Mode capture `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -Check uac.vhd-cancel `
  -Phase after `
  -ImagePath C:\QA\fixture.vhd `
  -EvidencePath .\uac-vhd-cancel-after.json
```

Each JSON file receives a SHA-256 sidecar. Verify either witness against the same candidate:

```powershell
.\scripts\beta-qa-uac-witness.ps1 `
  -Mode verify `
  -PackagePath .\DragonDiskForge-win-x64.zip `
  -ChecksumFile .\DragonDiskForge-win-x64.zip.sha256 `
  -ImagePath C:\QA\fixture.vhd `
  -EvidencePath .\uac-vhd-cancel-after.json
```

Then record the corresponding human observation through the exact `tools\beta-manual-qa.ps1` copy from the verified candidate package. The release gate remains open until every required UAC checklist item has valid human-confirmed evidence.

## Safety and privacy

The helper is read-only. It does not mount, unmount, elevate, approve, cancel, write, or mutate an image. It stores only the image leaf name and a SHA-256 fingerprint of the normalized full path, not the full image path. If the session is elevated, non-interactive, has UAC disabled, package identity is invalid, sidecars are tampered, or Windows cannot query the disk-image state, capture fails closed.
