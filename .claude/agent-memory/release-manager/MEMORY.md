# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.

## Current state (2026-11-07, ~21:15 +0330)

- Latest released version: **1.2.0**, annotated tag `v1.2.0` (tag object `6b1c35c`) on prod merge commit
  `4f34a2b` (parents: prod `0cdcd4c`, develop release commit `0668c9c`). Tag pushed 2026-11-07 ~21:12 +0330.
  Deploy run 42397525865 succeeded via Actions (watched by the main session; ~13 min, server build load ~20);
  /health 200 and /releases shows 1.2.0 on sapio prod.
- Previous: 1.1.0 (`v1.1.0` on `0cdcd4c`, run 42289387091 success).
- Base for "commits since last release": `v1.2.0`.
- `[Unreleased]` still holds the Telegram bot changes (link-failure messages, `/unlink`, commit `1a02b4d`),
  deliberately left out of 1.2.0. They run in the **Telegram gateway**, which `deploy-prod.yml` does not deploy
  (separate host, separate redeploy). Move them into a release only after the owner confirms the gateway is
  redeployed with that code.
- Migration `AddHotReadPathIndexes` (in 1.2.0) was already applied on prod by hand; it is CONCURRENTLY IF NOT
  EXISTS, so the deploy's migration step is a no-op.

## Open items

- **Tag renumbering not pushed yet.** On GitHub: `v1.1.0`, `v1.2.0` (correct) plus the old `v1.2.3`–`v1.3.6`
  and `prod-2026-11-04`; `v0.2.3`–`v0.3.5` and `v1.0.0` do not exist. Renumbering must run with the "Deploy
  prod tag" workflow disabled (each v* push deploys); the owner runs it by hand. Mapping table:
  `docs/release/release-process.md`. Until then the `[1.0.0]`, `[1.1.0]` and `[0.x]` compare links 404.
- **Collision now imminent:** 1.2.1 and 1.2.2 are free, but `v1.2.3`–`v1.2.7`, `v1.3.0`, `v1.3.2`–`v1.3.6`
  still exist as legacy tags. Before a 1.2.3 or 1.3.0 release, the legacy tags must be renumbered (deleted from
  origin). Never reuse an existing tag name; if renumbering has not happened, stop and ask.
- Version-sorted tag lists (`git tag --sort=-v:refname`) show legacy `v1.3.6` above `v1.2.0`; "which version is
  live" must use the CHANGELOG / latest successful deploy run, not the highest-sorting tag.
- prod tracks `docker-compose.override.yml` (pins the telegram-gateway image) and
  `docker-compose.override.yml.bak-20261103` that develop does not have. Merges stay clean; not touched by releases.

## Release log

| Version | Date | Commit (prod) | Deploy | Notes |
|---|---|---|---|---|
| 1.2.0 | 2026-11-07 | 4f34a2b | run 42397525865 success | balance sheet in symbol menu, Sapio name+link on copied/shared answers, lower server load (indexes), stock-screening null-threshold fix; Telegram items held in Unreleased |
| 1.1.0 | 2026-11-07 | 0cdcd4c | success, run 42289387091 | monthly-sales summary cards, answer actions, analysis stages, market-column symbol menu, Persian number style, /releases page |
| 1.0.0 | 2026-11-06 | 9cf098c | success (as v1.3.6) | first stable; renumbered from v1.3.6 |
