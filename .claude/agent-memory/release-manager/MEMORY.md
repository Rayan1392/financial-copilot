# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.
Older detail and the full version table: `release-history.md` (same folder).

## Release 1.39.0

- Tagged on 2026-11-28 (MINOR: one notable Changed item). The equilibrium range card measures the distance to the
  lower bound, the midpoint and the upper bound from today's price, with the direction as a word («رشد» / «افت»)
  instead of a sign (change `159-…/equilibrium-distance-from-price`, `3863a943`). Backend and frontend; no migration,
  no Telegram gateway change.
- Develop release commit `25e09869`, prod merge `df79d8d8` (temporary worktree), annotated tag `v1.39.0`
  (tag object `e40a58e3`). Release check passed; release-note tests 12/12. Gauge unit tests (235) and the gauge
  frontend tests (69) passed before the release.
- Deploy run 46115565776 succeeded (about 11m40s). `/releases` lists 1.39.0 and
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: the four-tile layout of the equilibrium card was verified by tests only, not in a browser or on a device;
  the owner is to look at it. BUG-053 still blocks the gauge card E2E tests. `specs/README.md` still says "not
  released" for this change and for the two changes of 1.38.0.

## Release 1.38.0

- Tagged on 2026-11-28 (MINOR: two Added items). Added the provider's forward P/E and P/S on the valuation gauge
  cards, with ingestion and storage of the gauge `forward` member (changes `148-…/gauge-forward-field` and
  `159-…/valuation-gauge-provider-forward`), and copy/share of the product revenue mix answer as an image
  (feature 075 change). All in commit `1c42ec79` (subject "valuation", committed by the owner).
- Develop release commit `0d9e3497`, prod merge `dfb8e858` (temporary worktree), annotated tag `v1.38.0`
  (tag object `3bbdf228`). Release check passed; release-note tests 12/12. Gauge unit tests (452), architecture
  tests (24) and the gauge card frontend tests (49) passed before the release; the product-mix change was not
  tested by the releasing session.
- **Migration:** `20261128090000_AddCyclicalWavesV2GaugeForwardValue` (five nullable `Forward*` columns and two
  check constraints on `cyclicalwaves."GaugeDetails"`). The owner acknowledged the missing database backup.
- Deploy run 46107436341 succeeded (about 11m30s). `/releases` lists 1.38.0 and
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: the stored forward values fill on the next gauge sync (daily 23:30 UTC, or
  `POST /api/v1/admin/cyclicalwaves-v2/gauges/sync`); comparing them with TahlilApp's page and the read-only count in
  the 148 change's `tasks.md` are with the owner. BUG-053 still blocks the gauge card E2E tests. The production
  value of the Sapio estimate flag was not verified. `specs/README.md` still says "not released" for these changes.

## Release 1.37.0

- Tagged on 2026-11-28 (MINOR: an Added item). Added the public market research guide `/market-guide`
  (feature 166, `3e530c2f`); Changed the new-chat introduction (`ea317ab0`). Frontend and docs only; no backend,
  migration or Telegram gateway change.
- Develop release commit `79b01656`, prod merge `776ef099` (temporary worktree), annotated tag `v1.37.0`
  (tag object `98232ddd`), created and pushed by the owner. Release check passed; release-note, market-guide,
  experience, starter-card and auth tests passed (6 files, 105 tests); frontend build passed. The full frontend
  suite, lint, type check and backend tests were not run.
- Deploy run 46066135101 succeeded (deploy job about 4m). `/releases` shows 1.37.0 as «نسخهٔ جاری»;
  `/market-guide` returned HTTP 200 and `https://api.sapioai.ir/health` returned `Healthy`.
- Open: feature 166 verification tasks 5–7 are unchecked. `specs/bugs.md` still says "Not yet released" for
  BUG-052 and BUG-049, and `specs/README.md` still says feature 166 is "implemented locally"; both are outside
  the release records and were left for the owner. Legacy tags `v1.2.3`–`v1.3.6` remain on origin.

## Release 1.36.0

- Tagged on 2026-11-27 (MINOR: one Added item). Added copy/share of an own-industry comparison answer as an
  image (`908ecbfa`, frontend only; no backend, config or migration change).
- Develop release commit `b5db642e`, prod merge `f0ee1e4f` (merge-tree/commit-tree), annotated tag `v1.36.0`
  (tag object `b15b4b14`). Release check passed; release-note tests plus the two answer-image test files passed
  (3 files, 34 tests). Backend and the rest of the frontend suite were not rerun.
- Deploy run 46017335498 succeeded (deploy job 9m03s). `/releases` shows 1.36.0 as «نسخهٔ جاری»;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: `specs/README.md` still says "not released" for change `comparison-answer-image-share` (feature 125);
  left for the owner, as that file is outside the release records. Real-device check of image copy/share
  (iOS/Android/Telegram paste) was not done by this agent.

## Release 1.35.0

- Tagged on 2026-11-27 (MINOR: an Added item). Added copy/share of a valuation gauge answer as an image
  (`8175d846`); Changed deep analysis to weigh evidence (`8c86849d`); Fixed BUG-052, gauge fetch time on
  unchanged vendor values, so industry comparison and P/E, P/S gauge cards work on closed market days (`ed1059a7`).
- Develop release commit `53f6756b`, prod merge `25ffbdbb` (merge-tree/commit-tree), annotated tag `v1.35.0`
  (tag object `948a8f7d`). The three change commits were already on prod through untagged merges; this merge
  added only the changelog and docs commit `004acd31`. Release check passed; release-note tests 12/12. Backend
  and other frontend tests were not rerun for this release.
- Deploy run 45993560736 succeeded (17m44s). `/releases` shows 1.35.0 as «نسخهٔ جاری»;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: `specs/bugs.md` still says "Not yet released" for BUG-052 (and BUG-049, shipped in 1.32.1); the release
  process does not cover that file, so it was left for the owner. BUG-052 production check (task 0) and BUG-053
  (19 gauge E2E tests fail at host start) are open. Commit `c52739d9` (subject ".") set
  `ValuationGaugeCards:Enabled` and `ForwardEstimate:Enabled` to true and two `CapabilitySources` in the tracked
  `appsettings.json`; runtime values in production were not verified.

## Release 1.34.0

- Tagged on 2026-11-27 (MINOR). Added Sapio's forward P/E and P/S estimates to valuation gauge cards
  (OQ-136, `57fbbc3f`) and made new-chat starter cards ask for a symbol (`63346dea`).
- Develop release commit `9fe62ccb`, prod merge `d4b62c32`, annotated tag `v1.34.0` (tag object
  `1a93f76a`). The release check passed, as did 91 focused frontend and 79 focused backend tests.
  No migration or Telegram gateway change.
- Deploy run 45960803820 succeeded (11m36s from run start to completion). The public `/releases` page
  showed 1.34.0 as current, and `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- The forward-estimate flag defaults to off in the released commit. Its runtime value was not verified
  or changed. The local uncommitted appsettings edit was excluded.

## Release 1.33.0

- Published on 2026-11-26 (MINOR at the owner's request; the single Changed item is small, so the rules alone would
  have allowed a PATCH). OQ-137: «زیر حد پایین» with the absolute value when the price is below the equilibrium range,
  card and text answer. Change commit `909b7351`, develop release commit `49fab715`, prod merge `515cc066`, annotated tag
  `v1.33.0` (tag object `4be8d3f6`).
- Release check passed and release-note tests passed 12/12. No migration, no Telegram gateway change.
- Deploy run 45850567270 succeeded (deploy job 11m15s). `/releases` lists 1.33.0 first;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: OQ-136 blocked on the provider (Gate A pending). Untracked `tools/research/` (another session) was left out.
  Legacy tags `v1.2.3`–`v1.3.6` remain on origin.

## Release 1.32.1

- Published on 2026-11-26 (PATCH, Fixed: BUG-049, light-theme emerald text on the new-chat page reaches 4.5:1). Fix
  commit `28942fab`; also carries `0f34df4c` (OQ-136 forward ratio design proposal, docs only). Develop release commit
  `ec81c500`, prod merge `0ebcbdc8`, annotated tag `v1.32.1` (tag object `888a60ae`).
- Release check passed and release-note tests passed 12/12. No migration, no Telegram gateway change.
- Deploy run 45841835788 succeeded (deploy job 3m47s). `/releases` lists 1.32.1 first;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: OQ-136 blocked on the provider (Gate A pending). OQ-137 answered by the owner on 2026-11-26 («زیر حد پایین»),
  change in progress. Legacy tags `v1.2.3`–`v1.3.6` remain on origin.

## Lessons and repository quirks

- Check `git log v<last>..origin/prod` before a release; prod may carry untagged merges (1.14.0, 1.15.0, 1.16.0).
- Do the prod merge in a temporary worktree under the scratchpad. If the path is too long, build the merge with
  `git merge-tree --write-tree` + `git commit-tree` and push `<sha>:refs/heads/prod`.
- The owner runs git from Windows PowerShell 5.1: one command per line, no `&&`, explicit absolute paths (an empty
  `$TMP` once put a worktree at `C:/prod-release`).
- prod tracks files develop lacks (`docker-compose.override.yml`, its `.bak-20261103`, `.deepcode/settings.json`,
  a DDD `SKILL.md`). Merges stay clean; releases do not touch them.
- prod carries owner commit `1e68bd6` with an older copy of this file; a develop->prod merge may conflict on it
  (take the newer content).
- `src/frontend/node_modules` exists only in the main worktree; junction it temporarily to run the changelog
  vitest elsewhere.
- `git rev-parse --short A B` fails with several revisions in this repo's shell; query one revision per call.

## Open items

- **Tag renumbering not pushed yet.** On GitHub: `v1.1.0`, `v1.2.0`, `v1.4.0`–`v1.10.0` (correct) plus the old
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
- Feature flags not verified in prod `.env`: `SymbolGroundedFallback__Enabled` (owner's key was misspelled on
  2026-11-17) and `ReportDiscussion:Enabled`.
