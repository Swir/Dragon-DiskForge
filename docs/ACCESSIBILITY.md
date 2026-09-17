# Dragon DiskForge — Accessibility and Keyboard Contract

Dragon DiskForge treats accessibility as part of the product contract rather than a cosmetic pass. The 0.9 beta-hardening slice focuses on deterministic UI Automation metadata, keyboard access and changing-status announcements across the primary browsing surfaces.

## Hardened surfaces

The current automated contract covers:

- Direct Image Browse
- Dragon Explorer
- multi-image Explorer workspace
- Images / Recent / Favorites
- Mounted Images and mount history

These surfaces now provide explicit UI Automation names and, where useful, help text for interactive controls and dynamic collections. Changing paths, counts, status messages and selected-preview state use polite live-region metadata where an announcement is useful without interrupting the user.

## Keyboard contract

Common actions expose Windows access keys where they are stable and unambiguous inside a view. Native WinUI focus behavior remains the basis for Tab/Shift+Tab navigation, list selection and TabView traversal. The hardening intentionally avoids adding hidden destructive shortcuts.

## Automated regression gate

`scripts/accessibility-contract.ps1` parses the hardened XAML and fails if it finds contract regressions such as:

- a hardened surface with no UI Automation metadata
- an unlabeled button
- a search text box without an accessible name
- an unnamed progress indicator
- an InfoBar without a live-setting contract
- an unnamed ListView/TabView collection
- duplicate access keys inside the same view

`.github/workflows/accessibility-contract.yml` runs that contract on Windows and then compiles the WinUI x64 Release application. This gate is intentionally additive to the full Windows regression/build/package workflow, Security Boundary, Disposable Media Guard and Clean Machine Runtime workflows.

## Evidence boundary

Passing the automated accessibility contract proves the checked XAML semantics and that the hardened WinUI surface still compiles. It does **not** claim formal WCAG certification, complete accessibility conformance, or a human Narrator/NVDA/JAWS session.

Human assistive-technology testing remains useful release QA, especially for focus order, spoken phrasing, high-contrast behavior and real desktop interaction. Any issue found there should be treated as a product defect and folded back into this repeatable contract where practical.

## Safety

Accessibility changes must not broaden capabilities or bypass safety gates. Physical-media write operations remain outside the user-visible product surface until their independent hardware validation gate is satisfied.
