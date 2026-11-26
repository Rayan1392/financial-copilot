# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.
Older detail and the full version table: `release-history.md` (same folder).

## Release 1.32.1

- Published on 2026-11-26 (PATCH, Fixed: BUG-049, light-theme emerald text on the new-chat page reaches 4.5:1). Fix
  commit `28942fab`; also carries `0f34df4c` (OQ-136 forward ratio design proposal, docs only). Develop release commit
  `ec81c500`, prod merge `0ebcbdc8`, annotated tag `v1.32.1` (tag object `888a60ae`).
- Release check passed and release-note tests passed 12/12. No migration, no Telegram gateway change.
- Deploy run 45841835788 succeeded (deploy job 3m47s). `/releases` lists 1.32.1 first;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: OQ-136 blocked on the provider (Gate A pending). OQ-137 answered by the owner on 2026-11-26 («زیر حد پایین»),
  change in progress. Legacy tags `v1.2.3`–`v1.3.6` remain on origin.

## Release 1.32.0

- Published on 2026-11-26 (MINOR: an Added item, the «گیج‌های ارزش‌گذاری گدنا» starter card). Feature commit
  `ef7ef4cd` (159 change `valuation-gauge-starter-card`), develop release commit `9ed45529`, prod merge `d2a5a23a`,
  annotated tag `v1.32.0` (tag object `b0a1b2eb`).
- Release check passed and release-note tests passed 12/12. No migration, no Telegram gateway change.
- Deploy run 45838072183 succeeded (deploy job 4m08s). `/releases` lists 1.32.0 first;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: BUG-049 (light-theme emerald text below 4.5:1, pre-existing); OQ-136 forward needle blocked on the provider
  (design proposal, Gate A pending); OQ-137 open. Legacy tags `v1.2.3`–`v1.3.6` remain on origin.

## Release 1.31.0

- Published on 2026-11-26 (MINOR: a notable Changed item, the gauge price).
  - Changed: Feature 159 change `valuation-gauge-per-card-credits`, one credit per usable gauge card (three for the
    combined answer), with the hold raised before any read; a replayed turn reuses its original hold in every mode.
  - Changed: the equilibrium card no longer shows the quote provider name or the «بازه:» footer prefix.
- Commits:
  - copy edit `8ef840fd`, credits `03747161`, starter-card proposal `2c726b31` (docs only, Gate A pending);
  - develop release commit `af586bbc`;
  - prod merge `8033f4d1`;
  - annotated tag `v1.31.0` on `8033f4d1`.
- Checks:
  - the release check passed and release-note tests passed 12/12;
  - no migration and no Telegram gateway change;
  - backend failures compared by test name with clean HEAD `a6b9ed9f` and base `864b7344`: none new except
    load-related ones that pass alone; architecture 24/24; frontend build and lint passed.
- Deploy run 45823553842 succeeded (deploy job 11m26s). `/releases` returned HTTP 200 and lists 1.31.0 first;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open:
  - OQ-136 (forward P/E), OQ-137 (negative lower-bound label), OQ-141 and OQ-142 (starter card, Gate A pending);
  - prod and develop still differ in the same four pre-existing files.

## Release 1.30.0

- Published on 2026-11-26 (MINOR).
  - Added: the valuation gauge cards, already enabled in production by the owner after 1.29.2.
  - Changed: the Feature 159 change `valuation-gauge-cards-visual-refresh`: two-decimal ratios, whole-rial prices,
    green/black/red bounds, derived figures, the price band, and the segmented control on phones.
- Commits:
  - feature `f9de68e4` and label fix `efdf00a4`;
  - develop release commit `e318e0d5`;
  - prod merge `b4ddedf7`;
  - annotated tag `v1.30.0` (tag object `99ff80b5`).
- Checks:
  - the release check passed and release-note tests passed 12/12;
  - no migration and no Telegram gateway change;
  - backend failures were compared by test name with `864b7344`: none new once load-related timeouts were rerun alone.
- Deploy run 45809746873 succeeded (deploy job 10m45s). `/releases` returned HTTP 200 and lists 1.30.0 first;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open:
  - OQ-136, the forward P/E needle (provider data needed);
  - OQ-137, the wording of the «بالای حد پایین» tile when the figure is negative;
  - prod and develop still differ in four pre-existing files (compose override and its backup on prod;
    `.deepcode/settings.json` and a skill file on develop).

## Release 1.29.2

- Published on 2026-11-26 (PATCH, Fixed: «ترکیب فروش/درآمد ماهانه ‹نماد›» in a monthly sales conversation opens the
  product mix table). Fix commit `a7fe38b2`; also carries `3c9593b2` (Feature 159 change
  `standalone-valuation-gauge-cards`, off by default via `VALUATION_GAUGE_CARDS_ENABLED`, so not in the changelog).
  Develop release commit `ead299fe`, prod merge `ee28d1eb`, annotated tag `v1.29.2` (tag object `29627bb7`).
- Release check passed and release-note tests passed 12/12. No migration, no Telegram gateway change.
- Deploy run 45768239733 succeeded (deploy job 11m27s). `/releases` returned HTTP 200 and lists 1.29.2 first;
  `https://api.sapioai.ir/health` returned HTTP 200 `Healthy`.
- Open: the gauge cards stay off in production until the owner enables them (staging check pending, see the change's
  `tasks.md`); enabling them is a configuration change, and a changelog «Added» entry belongs to the release in which
  they become visible. The live Fajr monthly mix wording has not been exercised by an authenticated user.

## Release 1.29.1

- Published on 2026-11-25 (PATCH, Fixed). BUG-048 fix commit `c9103364`, develop release commit
  `2f301185`, prod merge `23850a47`, and annotated tag `v1.29.1` (tag object `4cfd17ac`).
  The release check passed and release-note tests passed 12/12. No migration is required.
- Deploy run 45582343689 succeeded, with a deploy job of 11m20s. The public release page returned HTTP 200
  and showed 1.29.1 as current with the Fajr entry. The public API health endpoint returned HTTP 200 and Healthy.
- The release corrects monthly sales report routing, prioritizes leading products in rate suggestions,
  and opens monthly sales report discussions in Simple mode with web search off. The public checks verified the
  deployment. The authenticated Fajr conversation has not been exercised against live data.

## Release 1.29.0

- Published on 2026-11-25 (MINOR: Added user-initiated support requests from the existing web inbox).
  Feature commit `9b414351`, develop release commit `24396ac9`, prod merge `216a9f5c`, and annotated tag
  `v1.29.0` (tag object `b5b8d0a2`). Deploy run 45550145476 succeeded, with a deploy job of 10m31s.
  The public release page returned HTTP 200 and showed 1.29.0 as current, with the support entry.
  The public API health endpoint returned HTTP 200 and Healthy.
- The tag/changelog check passed and release-note tests passed 12/12. No new Auth migration is needed.
  Feature 165's existing schema shipped in 1.22.0. Queue ownership and the internal response target remain open in OQ-129.

## Release 1.28.0

- Published on 2026-11-24 (MINOR: Added «‹نماد› را با صنعت خودش مقایسه کن» to the desktop symbol menu
  (watchlist, market movers) and the mobile symbol sheet). Feature commit `bc6d521b`; also carries
  `0a15e2b0` (BUG-047 tests) and `bb9d8f16` (BUG-046 docs), neither in the changelog. Develop release commit
  `8f22ff11`, prod merge `bd5aeafe` (merge-tree/commit-tree), annotated tag `v1.28.0` (tag object `4769dd7e`).
- Release-note tests passed 12/12 and the tag/changelog check passed. Deploy run 45443715705: success
  (about 4m30s); `/releases` returned HTTP 200 and lists 1.28.0 first. `gh run watch` dropped on a transient
  api.github.com connection error; the result was confirmed with `gh run view`.

## Release 1.27.0

- Published on 2026-11-24 (MINOR: Added new-chat starter cards for own-industry comparison and cash flow +
  Fixed whole-rial equilibrium price in the industry comparison). Commits `4c0e7a36` (fix), `dbec5919`
  (feat), develop release commit `ef48972a`, prod merge `f8c31cf4` (merge-tree/commit-tree), annotated tag
  `v1.27.0` (tag object `b39ed230`).
- Release-note tests passed 12/12 and the tag/changelog check passed. Deploy run 45424581870: success (12m8s); verified 2026-11-24 with `gh run list` and https://sapioai.ir/releases (HTTP 200).
- Owner's uncommitted edits (054 fix-instrument-catalog-coverage tasks.md, BUG-046 row in specs/bugs.md) were
  left out of the release.

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
