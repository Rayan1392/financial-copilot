# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.

## Current state (2026-11-07)

- Latest released version: **1.1.0**, tag `v1.1.0` on prod merge commit `0cdcd4c` (develop `e3c7822`),
  live in production since 2026-11-07 ~00:15 (+0330); deploy run 42289387091 succeeded, including the
  "Verify tag matches CHANGELOG.md" step. https://sapioai.ir/releases shows 1.1.0 as «نسخهٔ جاری».
- Base for "commits since last release": `v1.1.0`.
- `[Unreleased]` holds the Telegram bot changes (link-failure messages, `/unlink`, commit `1a02b4d`). They
  are on prod but run in the **Telegram gateway**, which `deploy-prod.yml` does not deploy (separate host,
  separate redeploy). Move them into a release only after the gateway is redeployed with that code.

## Open items

- **Tag renumbering not pushed yet.** On GitHub: `v1.1.0` (correct) plus the old `v1.2.3`–`v1.3.6` and
  `prod-2026-11-04`; `v0.2.3`–`v0.3.5` and `v1.0.0` do not exist. Renumbering must run with the "Deploy prod
  tag" workflow disabled (each v* push deploys); disabling it was not permitted for the agent, so the owner
  runs it by hand. Mapping table: `docs/release/release-process.md`. Until then the `[1.0.0]`, `[1.1.0]` and
  `[0.x]` compare links 404, and old `v1.2.x`/`v1.3.x` names would collide with future 1.2/1.3 releases —
  renumber before reaching 1.2.0. `git describe` on prod already returns v1.1.0 (nearest tag).
- prod tracks `docker-compose.override.yml` and `docker-compose.override.yml.bak-20261103` that develop does
  not have (present since before 1.0.0; deploys work with them). Not touched by releases.

## Release log

| Version | Date | Commit (prod) | Deploy | Notes |
|---|---|---|---|---|
| 1.1.0 | 2026-11-07 | 0cdcd4c | success, run 42289387091 | monthly-sales summary cards, answer actions, analysis stages, market-column symbol menu, Persian number style, /releases page |
| 1.0.0 | 2026-11-06 | 9cf098c | success (as v1.3.6) | first stable; renumbered from v1.3.6 |
