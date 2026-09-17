# Dragon DiskForge — Product Roadmap

This file is the source of truth for product progress. Meaningful feature changes must update tests, `CHANGELOG.md`, status and progress documentation.

## Status legend
- ✅ complete
- 🚧 in progress
- ⬜ planned

---

## 0.1 Foundation + Dragon Visual Identity — ✅ complete
WinUI 3/.NET 10 shell, Core separation, image detection, verification foundation, read-only-first architecture, Dragon visual system, responsive/accessibility resources, Windows icon and x64 CI are proven.

## 0.2 Native Mount + Unmount — ✅ complete
Native Windows ISO/VHD/VHDX read-only-first Mount/Unmount, state detection, progress/cancellation and disposable Windows integration tests are proven.

## 0.3 Dragon Explorer — ✅ complete
- ✅ mounted-volume list/navigation/search/Copy out
- ✅ bounded Preview modes
- ✅ Recent Images + Favorites
- ✅ mounted history + multi-image workspace
- ✅ safe Copy-only drag-out
- ✅ managed ISO9660/Joliet direct browsing without mount

## 0.4 Extended Image Providers — ✅ complete
**Required 0.4 engineering scope: 100%.** Provider foundation, eleven additional image families and provider-contract hardening are real and tested.

- ✅ truthful capability reporting and deterministic provider resolution
- ✅ failure isolation, cancellation and descriptor hardening
- ✅ IMG/RAW, IMA/floppy, BIN/CUE, MDF/MDS, NRG, CCD/IMG/SUB
- ✅ VMDK, QCOW/QCOW2, DMG/UDIF, WIM/ESD and FFU metadata

**0.4 exit criteria: PASSED.** Closing this milestone hardens the internal provider contract; it does not promise a stable public plugin API.

---

## 0.5 Partitions + File Systems + Image Intelligence — ✅ complete

**Required 0.5 automated engineering scope: 100%.** Public beta publication still has independent manual/package gates in `docs/BETA-RELEASE.md`.

### Completed 0.5 scope
- ✅ provider-independent partition intelligence
- ✅ FAT12/16/32, exFAT, supported NTFS, ext2/3/4, ISO9660/Joliet and UDF VRS recognition on proven mappings
- ✅ bounded El Torito / BIOS / UEFI and installer evidence
- ✅ unified identity + health intelligence with provenance
- ✅ Windows Analyze + text/JSON reporting
- ✅ deeper exFAT/FAT32/UDF/NTFS metadata evidence
- ✅ architecture reconciliation without guessed conflict resolution
- ✅ clean Windows x64 package-candidate pipeline + independent ZIP verification
- ✅ bounded physical UDF Type 1 root traversal
- ✅ read-only QCOW2 standard-uncompressed guest-byte reader subset
- ✅ read-only hosted-sparse VMDK `monolithicSparse` guest-byte reader subset
- ✅ common guest-relative MBR/EBR/GPT + filesystem intelligence
- ✅ guest GPT CRC/geometry and EBR containment hardening

**0.5 exit criteria: PASSED for automated engineering scope.** Beta publication remains blocked until the independent release/manual gates in `docs/BETA-RELEASE.md` are satisfied.

---

## 0.6 Create + Convert + Verify — ✅ complete

**Required 0.6 engineering scope: 100%.** All five top-level deliverables are implemented and validated.

- ✅ image creation and conversion pipeline
- ✅ split/join and bounded sparse-input/compression handling
- ✅ SHA-256/SHA-512 verification
- ✅ temporary output + atomic finalization
- ✅ cancellation/rollback safety

### SHA-256/SHA-512 verification — ✅ complete foundation
- ✅ `ImageVerificationInfo` reports SHA-256, SHA-512 and exact hashed-byte count
- ✅ both digests are computed in one bounded sequential pass
- ✅ SHA-256 compatibility API plus dedicated SHA-512 API
- ✅ monotonic progress/cancellation/missing-file coverage
- ✅ PR #41 implementation run #292 passed full Windows regression/build/package validation

### Transactional output boundary — ✅ complete foundation
- ✅ reusable `SafeOutputService`
- ✅ same-directory temporary output
- ✅ `FailIfExists` and `ReplaceExisting` policies
- ✅ completed temporary output is flushed before publication
- ✅ writer failure/cancellation preserves existing committed destinations on the proven paths
- ✅ PR #42 implementation run #296 and final run #298 passed full Windows regression/build/package validation

### RAW creation + guest-to-RAW conversion — ✅ complete foundation
- ✅ explicit-length blank RAW creation
- ✅ bounded `IGuestByteReader` → RAW materialization
- ✅ explicit QCOW2 → RAW and hosted-sparse VMDK → RAW over the proven reader subsets
- ✅ exact captured guest-visible length required at commit
- ✅ source/destination identity rejection
- ✅ PR #43 implementation run #300 passed full Windows regression/build/package validation

### Transactional split/join — ✅ complete foundation
- ✅ split sets stage every part plus `dragon-split-manifest.json` in a sibling temporary directory
- ✅ final set is published only after all parts and the manifest are complete and flushed
- ✅ generated part names and a 10,000-part safety ceiling
- ✅ SHA-256 integrity recorded for every part
- ✅ join validates manifest version, geometry, safe filenames, physical lengths and part hashes
- ✅ join output uses `SafeOutputService`
- ✅ cancellation/conflict/hash-mismatch paths are tested not to intentionally publish partial replacement output

### Bounded compression and sparse-input handling — ✅ complete foundation
- ✅ transactional whole-file gzip compression
- ✅ gzip decompression requires an explicit maximum output byte count
- ✅ minimum gzip envelope, magic, method and reserved base-header bits are checked before output staging
- ✅ malformed/truncated gzip and decompression-cap paths fail closed
- ✅ the already-proven QCOW2/VMDK sparse/unallocated guest mappings can be materialized read-only to flat RAW
- ✅ no sparse-container writer is claimed
- ✅ no QCOW2 compressed-cluster, VMDK stream-optimized/compressed extent or DMG `blkx` decoder is claimed
- ✅ no Create/Convert WinUI action is enabled merely because the Core foundation exists
- ✅ PR #44 implementation run #304 passed the new split/join + gzip gate plus the complete provider/intelligence/Explorer/native Windows/Release/clean-package path

See `docs/OUTPUT-TRANSACTIONS.md`, `docs/RAW-IMAGE-PIPELINES.md` and `docs/SPLIT-COMPRESSION-PIPELINES.md`.

**0.6 exit criteria: PASSED after final documentation-synchronized CI on the PR head.**

---

## 0.7 Physical Media Tools — 🚧 in progress

**Current required 0.7 engineering scope: 6/7 = ~86%.** The safety foundation and write-execution contract are implemented and passed full Windows CI. Development remains deliberately non-destructive until the final disposable-media write gate is proven.

- ✅ read-only physical disk inventory with serial-backed stable device identity when available
- ✅ capacity/bus/removable/system-disk evidence
- ✅ explicit system-disk and ambiguous-device refusal policy
- ✅ write-plan preview with source/destination identity checks
- ✅ destructive-action confirmation contract bound to destination identity
- ✅ bounded progress/cancellation plus explicit fail-safe recovery semantics after any destination write attempt
- ⬜ separately validated physical write path only after safety gates are proven

The execution coordinator revalidates the destination identity and exact confirmation token immediately before I/O, rechecks source length after opening the file, writes through a bounded injected sink contract, reports monotonic progress, isolates progress-observer failures, and distinguishes safe pre-write refusal/cancellation from failures or cancellation after destination mutation may have begun. Once any destination write is attempted, an abnormal exit is fail-closed as `DestinationMayBeModified` + `RequiresRecovery`; no generic rollback is claimed.

PR #46 implementation run #319 passed the expanded physical-media safety gate plus the complete existing provider/intelligence/Explorer/native Windows/Release/clean-package regression path. The PR still intentionally contains no Windows physical-device writer and exposes no destructive UI action.

See `docs/PHYSICAL-MEDIA-SAFETY.md`.

**No physical-device write capability is user-visible today.**

## 0.8 Windows Integration + Power Tools — ⬜ planned
- ⬜ file associations/context menu
- ⬜ shared-Core CLI
- ⬜ PowerShell-friendly output
- ⬜ session restore/settings import-export/diagnostic export

## 0.9 Quality, Security + Beta Hardening — ⬜ planned
- ⬜ expanded provider/integration tests
- ⬜ non-admin UAC/manual drag validation
- ⬜ large/corrupt/truncated/fuzz-style image tests
- ⬜ keyboard/screen-reader/High-DPI/theme review
- ⬜ localization architecture and EN/PL baseline
- ⬜ crash diagnostics/performance profiling
- ⬜ beta regression checklist

## 1.0 Production Release — ⬜ planned
- ⬜ final UI/UX
- ⬜ signed installer / portable build where appropriate
- ⬜ release pipeline and update strategy
- ⬜ stable provider API/config migration
- ⬜ full documentation/capability matrix/troubleshooting
- ⬜ regression suite green
- ⬜ GitHub Release with binaries/checksums

---

## Non-negotiable project rules
1. Never enable a fake UI capability.
2. Read-only inspection is the default.
3. Sensitive operations require explicit validation and confirmation.
4. UI stays separate from Core.
5. New formats use providers/capabilities, not one monolithic parser.
6. README, ROADMAP, STATUS, MILESTONES and CHANGELOG stay synchronized.
7. Large image fixtures are generated, not committed.
8. Accessibility outranks decoration.
9. High-impact operations remain gated until independently validated.
10. A milestone completes only after its exit criteria pass.
