# Dragon DiskForge — Supported Provider Capabilities

> This document is generated from the canonical Core provider registry exposed by `dragon-disk-forge formats --format json`. Do not hand-edit provider rows; update the implementation and regenerate the matrix.

This matrix describes **provider-layer capabilities only**. It does not turn metadata-only providers into full decoders, does not imply write support, and does not replace the separate native Windows mount contract or beta release gates.

## Canonical provider matrix

| Priority | Provider | Extensions | Inspect | Browse | Search | Copy out | Partitions | Geometry | Tracks | Virtual metadata | Container metadata |
|---:|---|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| 100 | ISO9660 / Joliet (`iso9660`) | `.iso` | ✅ | ✅ | ✅ | ✅ | — | — | — | — | — |
| 95 | CCD / IMG / SUB track layout (`ccd-img-sub`) | `.ccd`<br>`.img`<br>`.sub` | ✅ | — | — | — | — | — | ✅ | — | — |
| 90 | IMG / RAW partitions (`raw-partitions`) | `.dd`<br>`.img`<br>`.raw` | ✅ | — | — | — | ✅ | — | — | — | — |
| 80 | IMA / Floppy geometry (`floppy-ima`) | `.flp`<br>`.ima` | ✅ | — | — | — | — | ✅ | — | — | — |
| 70 | BIN / CUE track layout (`bin-cue`) | `.bin`<br>`.cue` | ✅ | — | — | — | — | — | ✅ | — | — |
| 60 | MDF / MDS track layout (`mdf-mds`) | `.mdf`<br>`.mds` | ✅ | — | — | — | — | — | ✅ | — | — |
| 50 | NRG track layout (`nrg`) | `.nrg` | ✅ | — | — | — | — | — | ✅ | — | — |
| 40 | VMDK sparse metadata (`vmdk-sparse`) | `.vmdk` | ✅ | — | — | — | — | — | — | ✅ | — |
| 30 | QCOW / QCOW2 metadata (`qcow`) | `.qcow`<br>`.qcow2` | ✅ | — | — | — | — | — | — | ✅ | — |
| 20 | DMG / UDIF metadata (`dmg-udif`) | `.dmg` | ✅ | — | — | — | — | — | — | ✅ | — |
| 10 | WIM / ESD container metadata (`wim-esd`) | `.esd`<br>`.wim` | ✅ | — | — | — | — | — | — | — | ✅ |
| 5 | FFU container metadata (`ffu`) | `.ffu` | ✅ | — | — | — | — | — | — | — | ✅ |

### Capability meanings

- **Inspect** — the provider can validate its supported subset and return bounded image/container metadata.
- **Browse / Search / Copy out** — provider-backed filesystem browsing is implemented without relying on a native mount.
- **Partitions** — the provider exposes a bounded partition-table path.
- **Geometry / Tracks** — the provider exposes bounded media-geometry or optical track-layout evidence.
- **Virtual metadata / Container metadata** — metadata inspection only unless another explicitly documented service supplies a narrower guest-byte path.

## Important boundaries

- `.img` intentionally overlaps the CCD and RAW providers. Extension matching only influences deterministic probe order; the selected provider must still validate the content.
- Native Windows **Mount / Unmount** for ISO/VHD/VHDX is a separate Windows integration path and is not represented by provider capability flags.
- ISO9660/Joliet direct browse/search/copy-out is a managed provider path and does not require mounting.
- QCOW2 and hosted-sparse VMDK have bounded guest-byte readers only for the explicitly proven uncompressed subsets documented in [`GUEST-BYTE-READERS.md`](GUEST-BYTE-READERS.md). The matrix does not claim arbitrary QCOW2/VMDK decoding, writing or mounting.
- WIM/ESD and FFU rows describe bounded container metadata. They do not claim file extraction, image application or mutation.
- SHA-256/SHA-512 verification is a shared read-only service and CLI capability for readable files, not a provider capability flag.

## Native Windows paths outside the provider flags

| Path | Proven formats/scope | Release boundary |
|---|---|---|
| Native Mount / Unmount | ISO, VHD, VHDX | Read-only-first Windows path; normal-user UAC remains part of interactive beta QA. |
| Managed direct browse | ISO9660/Joliet ISO | Browse, search and safe copy-out without a native mount. |
| Shared verification | Any readable file | SHA-256 + SHA-512 are computed in one bounded sequential pass; this does not imply format decoding. |

## Release truthfulness

The matrix is a source-derived capability reference, **not a beta-readiness signal**. Public `0.5.0-beta.1` remains governed by [`BETA-RELEASE.md`](BETA-RELEASE.md), including the outstanding human-confirmed clean-desktop/UAC/Explorer gates.

## Regenerate / verify

```powershell
.\scripts\generate-capability-matrix.ps1 -Write
.\scripts\generate-capability-matrix.ps1 -Check
.\scripts\generate-capability-matrix.ps1 -SelfTest
```

`-Check` builds the Release CLI, asks the canonical registry for JSON provider descriptors, validates ids/extensions/capabilities/order and fails if this committed document is stale.

---

Generated capability reference for **Dragon DiskForge** · by Swir · [GitHub](https://github.com/Swir/Dragon-DiskForge)
