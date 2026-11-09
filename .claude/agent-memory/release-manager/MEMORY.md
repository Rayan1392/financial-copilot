# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.

## Current state (2026-11-09, ~16:00 +0330)

- Latest released version: **1.4.0**, annotated tag `v1.4.0` (tag object `7402695`) on prod merge commit
  `bae1c5e` (parents: prod `4f34a2b`, develop release commit `827b8c3`). Tag pushed 2026-11-09 ~15:42 +0330.
  Deploy run 42615018199 succeeded in 13m36s. The `v1.4.0` tag-on-prod and CHANGELOG checks passed;
  `/` and `/releases` returned 200, and `/releases` showed 1.4.0 as current. `/health` returned 404 on the
  public host, so it was not used as the release check.
- Previous release: 1.2.0 (`v1.2.0` on `4f34a2b`, run 42397525865 success).
- Base for "commits since last release": `v1.4.0`.
- `[Unreleased]` still holds the Telegram bot changes (link-failure messages, `/unlink`, commit `1a02b4d`),
  deliberately left out of 1.4.0. They run in the **Telegram gateway**, which `deploy-prod.yml` does not deploy
  (separate host, separate redeploy). Move them into a release only after the owner confirms the gateway is
  redeployed with that code.
- Migration `AddHotReadPathIndexes` (in 1.2.0) was already applied on prod by hand; it is CONCURRENTLY IF NOT
  EXISTS, so the deploy's migration step is a no-op.

## Open items

- **Tag renumbering not pushed yet.** On GitHub: `v1.1.0`, `v1.2.0` (correct) plus the old `v1.2.3`–`v1.3.6`
  and `prod-2026-11-04`; `v0.2.3`–`v0.3.5` and `v1.0.0` do not exist. Renumbering must run with the "Deploy
  prod tag" workflow disabled (each v* push deploys); the owner runs it by hand. Mapping table:
  `docs/release/release-process.md`. Until then the `[1.0.0]`, `[1.1.0]` and `[0.x]` compare links 404.
- **Legacy tag cleanup remains open:** the old `v1.2.3`–`v1.3.6` and `prod-2026-11-04` tags remain on GitHub;
  the destination tags `v0.2.3`–`v0.3.5` and `v1.0.0` are absent. The historical `v1.3.0` remains on
  `19b207a` (the mapping table associates that commit with `v0.3.0`); release `v1.4.0` avoided reusing it.
  Do not move or delete published tags without the owner's explicit authorization and the documented workflow
  disable/enable sequence.
- Version-sorted tag lists (`git tag --sort=-v:refname`) may show legacy tags above `v1.4.0`; "which version is
  live" must use the CHANGELOG / latest successful deploy run, not the highest-sorting tag.
- prod tracks `docker-compose.override.yml` (pins the telegram-gateway image) and
  `docker-compose.override.yml.bak-20261103` that develop does not have. Merges stay clean; not touched by releases.

## Release log

| Version | Date | Commit (prod) | Deploy | Notes |
|---|---|---|---|---|
| 1.4.0 | 2026-11-09 | bae1c5e | run 42615018199 success | user analytics dashboard, conditional CyclicalWaves valuation queries and P/S gauge, product-mix sales trend fix; Telegram items held in Unreleased |
| 1.2.0 | 2026-11-07 | 4f34a2b | run 42397525865 success | balance sheet in symbol menu, Sapio name+link on copied/shared answers, lower server load (indexes), stock-screening null-threshold fix; Telegram items held in Unreleased |
| 1.1.0 | 2026-11-07 | 0cdcd4c | success, run 42289387091 | monthly-sales summary cards, answer actions, analysis stages, market-column symbol menu, Persian number style, /releases page |
| 1.0.0 | 2026-11-06 | 9cf098c | success (as v1.3.6) | first stable; renumbered from v1.3.6 |
