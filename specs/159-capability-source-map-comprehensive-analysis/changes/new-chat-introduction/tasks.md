# New chat introduction implementation plan

Status: Gate B: Approved by Kamran Abdi on 2026-11-27

- [x] Replace the heading, badge, subheading, and body on `src/frontend/src/routes/_app.chat.tsx` with the supplied Persian copy. Keep the introductory hierarchy and existing theme tokens. Use bidirectional isolation for the two Latin ratios.
- [x] Remove the now-unused `starterCoverage` function and its obsolete test. Preserve the existing capability checks and card tests. Record the updated desktop copy decision beside OQ-142.
- [x] Run the existing starter-card tests and check the four passages, wrapping, and card layout in light and dark mode at desktop and narrow viewport widths. Report verification constraints.

The work is limited to the frontend introduction and its supporting source, test, and decision record. It has no backend, database, or deployment dependencies.

Verification: all 32 targeted tests and the frontend build passed. The starter-card browser fixture renders the same introduction as `/chat`. It verified the copy, ratio direction, wrapping, and card layout in both themes at widths from 320 to 1280 px, with the valuation card enabled and disabled. The browser suite also passed its desktop and mobile symbol picker checks.
