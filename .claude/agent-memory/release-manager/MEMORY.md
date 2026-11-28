# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.
Older detail and the full version table: `release-history.md` (same folder).

## Release 1.41.2

- Released on 2026-11-28 (PATCH: one small Changed item). The new-chat option starter card is behind its own switch
  `OptionAnswers:StarterCardEnabled` (default off); typed option questions and the symbol menu follow
  `OptionAnswers:Enabled` only (change 168 `option-starter-card-switch`, `853c6ad2`). Option answers are still off
  on production, so users see no difference yet. Release commit `de7fc44e`; prod merge `8ead7e21`; annotated tag
  `v1.41.2` (tag object `97130068`). No migration, no Telegram gateway change.
- Release check passed and release-note tests passed 12/12. Full solution and frontend suites were not run.
- Deploy run 46162999085 succeeded in 14m54s. `https://api.sapioai.ir/health` returned `Healthy`;
  `https://sapioai.ir/releases` shows 1.41.2 first.

## Release 1.41.1

- Released on 2026-11-28 (PATCH: release-note correction only, no code). OQ-184 answered: TahlilApp updates P/E, P/S
  and the equilibrium range once a day and on a new report, so production keeps the market-hours run off. Wording fix
  of the released 1.41.0 entry (summary line and first Changed bullet) in its own commit `6c8ad0a3`; 1.41.1 Fixed
  entry says the 1.41.0 note was wrong. Release commit `86855fda`; prod merge `f5a9df40` (merge-tree, dirty main
  worktree held another session's Feature 168 work); annotated tag `v1.41.1` (tag object `be6c1bce`).
- Release check passed; release-note tests 12/12. Deploy run 46162524677 succeeded (deploy job 4m41s). `/health`
  HTTP 200 `Healthy`; `/releases` lists 1.41.1.
- Open: the notes describe 06:00/19:00 Full runs and the CodalMirror refresh; the server `.env` steps (cron
  `30 2,15 * * *`, `CYCLICALWAVES_V2_CODAL_GAUGE_REFRESH_ENABLED=true`) were not verified by the release session.

## Release 1.41.0

- Released on 2026-11-28 (MINOR: two notable Changed items). Changed: CyclicalWaves v2 gauges and equilibrium
  ranges refresh during market hours and after CodalMirror filings, full runs at 06:00 and 19:00 Tehran
  (`acda444d`); the equilibrium card shows today's last trade price after the close (`29c88f4f`). Fixed: BUG-055,
  instruments sync stores the Persian symbol (`a3ae4633`). Shipped dark, no CHANGELOG line: Feature 167 option
  data (`aae83fef`, `TsetmcPolling:OptionContractsEnabled`/`OptionOrderBookEnabled` off) and Feature 168 option
  answers (`024e420f`, `OptionAnswers:Enabled` off); intraday and Codal gauge switches also default off.
  Release commit `1e8b2103`; prod merge `b5040432`; annotated tag `v1.41.0` (tag object `ada66196`).
- Migrations (run at API start, no backup; acknowledged by the owner): `20261128095025_Feature167OptionMarket`,
  `20261128150000_AddCyclicalWavesV2SyncRunNotBefore`.
- Release check passed and release-note tests passed 12/12. Full solution and frontend suites were not run.
- Deploy run 46159225899 succeeded in 14m18s. `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`;
  `https://sapioai.ir/releases` shows 1.41.0.
- Open: CHANGELOG line for options when `OPTION_ANSWERS_ENABLED` is turned on; option rollout (instruments sync,
  stub backfill, option switches) and Feature 168 AC-23; BUG-055 production re-check after the first instruments
  sync; intraday/Codal gauge switches in the server `.env`.

## Release 1.40.0

- Released on 2026-11-28 (MINOR: one Added item). Added year-wide manual acquisition of completed Shamsi monthly
  company production/sales reports; the operator can still refresh one chosen month. Feature commit `ddf96c19`;
  release commit `3343f5f2`; prod merge `41de3e80`; annotated tag `v1.40.0` (tag object
  `70082bb2`). No database migration.
- Release check passed and release-note tests passed 12/12. Focused calendar tests passed 22/22, completed-month
  endpoint integration tests passed 3/3, and frontend production build passed. Full solution and frontend Vitest
  suites were not run. Frontend repository-wide ESLint reported pervasive CRLF errors; typecheck reported known
  existing failures.
- Deploy run 46119466994 succeeded in 14m11s. `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`;
  `https://sapioai.ir/releases` lists 1.40.0 as «نسخهٔ جاری».
- Open: targeted monthly form Vitest coverage remains outstanding. Full frontend test suite and full solution
  suite remain unverified.

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
