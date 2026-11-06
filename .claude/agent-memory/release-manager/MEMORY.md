# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.

## Current state (2026-11-06)

- Latest released version: **1.0.0** = commit `9cf098c` (formerly tag `v1.3.6`), live in production since
  2026-11-06 (deploy run for old tag v1.3.6 succeeded).
- Next version continues from 1.0.0 (SemVer). `[Unreleased]` holds the release-history page → next is a MINOR (1.1.0).
- Base for "commits since last release": `9cf098c` (use it while the `v1.0.0` tag does not exist yet).

## Open items

- **Tag renumbering not pushed yet.** Old tags `v1.2.3`–`v1.3.6` and `prod-2026-11-04` still exist on GitHub;
  `v0.2.3`–`v0.3.5` and `v1.0.0` do not. Renumbering must run with the "Deploy prod tag" workflow disabled
  (each v* push deploys); disabling it was not permitted for the agent, so the owner runs it by hand.
  Mapping table: `docs/release/release-process.md`. Until then the `[1.0.0]`/`[0.x]` compare links 404, and
  old `v1.2.x`/`v1.3.x` names would collide with future 1.2/1.3 releases — renumber before reaching 1.2.0.

## Release log

| Version | Date | Commit | Deploy | Notes |
|---|---|---|---|---|
| 1.0.0 | 2026-11-06 | 9cf098c | success (as v1.3.6) | first stable; renumbered from v1.3.6 |
