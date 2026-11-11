# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.

## Current state (2026-11-11, ~18:15 +0330)

- Latest released version: **1.9.0**, annotated tag `v1.9.0` (tag object `27757f8`) on prod merge commit
  `8465aa9` (parents: prod `c43609a`, develop release commit `ce1ff2e`). Tag pushed 2026-11-11 ~18:13 +0330.
  Deploy run 43041307287 succeeded in ~8.5m (all steps green). `/releases` returned 200 and contains "1.9.0".
- Previous release: 1.8.0 (`v1.8.0` on `6c960b1`, run 42917616701 success).
- Base for "commits since last release": `v1.9.0`.
- Pattern since 1.4.0: release commit on develop, `Merge branch 'develop' into prod` (--no-ff) in the main
  worktree (prod checked out there), tag the prod merge commit. develop may be checked out in `D:\wt\a`.
- Note: `da623ed` (analysis-card line breaks fix) landed on develop after the 1.8.0 release commit but was
  merged into prod before tagging `v1.8.0`, so it shipped in 1.8.0 without a changelog line.
- `src/frontend/node_modules` exists only in the main worktree; to run the changelog vitest in another worktree,
  junction it temporarily (gitignored) and remove the junction afterwards.

## Open items

- **Tag renumbering not pushed yet.** On GitHub: `v1.1.0`, `v1.2.0`, `v1.4.0`–`v1.9.0` (correct) plus the old
  `v1.2.3`–`v1.3.6`; `v0.2.3`–`v0.3.5` and `v1.0.0` do not exist. Renumbering must run with the "Deploy prod
  tag" workflow disabled (each v* push deploys); the owner runs it by hand. Mapping table:
  `docs/release/release-process.md`. Until then the `[1.0.0]`, `[1.1.0]` and `[0.x]` compare links 404.
- The historical `v1.3.0` remains on `19b207a` (mapping table: `v0.3.0`). Do not move or delete published tags
  without the owner's explicit authorization and the documented workflow disable/enable sequence.
- "Which version is live" must use the CHANGELOG / latest successful deploy run, not the highest-sorting tag.
- prod tracks `docker-compose.override.yml` (pins the telegram-gateway image) and
  `docker-compose.override.yml.bak-20261103` that develop does not have. Merges stay clean; not touched by releases.
- Telegram gateway items (link-failure messages, `/unlink`) were released in 1.8.0; the gateway is deployed
  separately from `deploy-prod.yml`.

## Release log

| Version | Date | Commit (prod) | Deploy | Notes |
|---|---|---|---|---|
| 1.9.0 | 2026-11-11 | 8465aa9 | run 43041307287 success | full TahlilApp analysis text for Max; technical-analysis route; unavailable valuation topics refused without charge; price-unit and "Found metric data" fixes |
| 1.8.0 | 2026-11-11 | 6c960b1 | run 42917616701 success | TahlilApp comprehensive-analysis card (preview); Telegram /unlink and link-failure reasons |
| 1.7.0 | 2026-11-10 | 14dadae | run 42830596519 success | |
| 1.6.0 | 2026-11-09 | 48b8fd9 | run 42686015806 success | |
| 1.5.0 | 2026-11-09 | dfda6b9 | run 42631239073 success | |
| 1.4.0 | 2026-11-09 | bae1c5e | run 42615018199 success | user analytics dashboard, conditional CyclicalWaves valuation queries and P/S gauge, product-mix sales trend fix |
| 1.2.0 | 2026-11-07 | 4f34a2b | run 42397525865 success | balance sheet in symbol menu, Sapio name+link on copied/shared answers, lower server load (indexes), stock-screening null-threshold fix |
| 1.1.0 | 2026-11-07 | 0cdcd4c | success, run 42289387091 | monthly-sales summary cards, answer actions, analysis stages, market-column symbol menu, Persian number style, /releases page |
| 1.0.0 | 2026-11-06 | 9cf098c | success (as v1.3.6) | first stable; renumbered from v1.3.6 |
