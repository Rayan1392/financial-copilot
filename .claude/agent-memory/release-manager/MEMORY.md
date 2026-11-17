# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.

## Current state (2026-11-17)

- Latest released version: **1.15.0** (MINOR: notable Changed), annotated tag `v1.15.0` (tag object `81a8814`) on
  prod merge commit `738c926` ("Merge develop into prod for v1.15.0"; parents prod `7070079`, develop release
  commit `dd1947b`). Deploy run 44018545211 succeeded in 8m23s. `/releases` HTTP 200 and its HTML contains 1.15.0.
- 1.15.0 ships financial-query credit pricing (`e6c0d02`): answered financial-statement table/analysis and
  monthly sales/product trend turns cost 2 credits (were 1 via `AiQuery.Scanner`); NoData stays free. No
  migrations. The owner had merged develop into prod (`7070079`) before the CHANGELOG commit, so the release
  commit was merged into prod a second time before tagging.
- 2026-11-17: the owner ran the worktree commands from PowerShell, where `$TMP` was empty; the worktree landed at
  `C:/prod-release` and its removal failed while the shell's cwd was inside it (empty folder left behind, git
  worktree metadata gone). Use an explicit absolute path when handing PowerShell commands to the owner.
- Base for "commits since last release": `v1.15.0`.
- Previous: 1.14.0 (MINOR: Added), annotated tag `v1.14.0` (tag object `565f990`) on prod
  merge commit `0e2838e` ("Merge develop into prod for v1.14.0"; parents prod `cd2ae25`, develop release commit
  `041fad9`). Deploy run 43882866503 succeeded in 9m8s. `/releases` HTTP 200 and its HTML contains 1.14.0.
- 1.14.0 ships Feature 163 symbol-grounded fallback (+ fix `15f1e84`), the 30-day TahlilApp analysis window
  (`66cd449`, `9f6bff8`), and the owner's untagged prod merge `cd2ae25` (Report Discussion 2 credits per answered
  turn `b917c28`, «… را تفسیر کن» suggestion `a94617f`); the owner decided cd2ae25 ships with 1.14.0, and the
  release manager added its two changelog lines. 163 (`SymbolGroundedFallback:Enabled`) and 162
  (`ReportDiscussion:Enabled`) are flag-gated, default off; whether prod `.env` enables them was not verified.
- Lesson 2026-11-16: check `git log v<last>..origin/prod` before a release; prod may carry untagged merges.
- 2026-11-16 (1.14.0): the main worktree had the owner's uncommitted work, so the prod merge was done in a
  temporary worktree `D:/wt/release-prod` (removed afterwards). Only `CHANGELOG.md` was staged on develop.
- Previous: 1.13.1 (PATCH, Fixed only), `v1.13.1` on `71c0614`, run 43802887944 success.
- 1.13.1 ships BUG-031 (`d8ff5f9`): Report Discussion analysis follow-ups no longer end in zero-credit fallbacks.
  The feature sits behind `ReportDiscussion:Enabled` (default off; prod could enable it via server `.env`, which
  reaches the API through env_file). Live end-to-end check T6 for BUG-031 was still pending at release.
- 2026-11-16: main worktree had develop checked out, no worktree had prod; the prod merge was made with
  `git merge-tree --write-tree` + `git commit-tree` + `git update-ref` (clean). This MEMORY.md was identical on
  prod and develop at that time; it was left uncommitted (not part of the release commit).
- Owner runs git from Windows PowerShell 5.1: give one command per line, no `&&`, no unset `$VARS` in paths.
- prod carries owner commit `1e68bd6` ("a": this MEMORY.md + deleted `.deepcode/settings.json` and a `.roo` skill)
  that develop lacks; it stays on prod only. This file therefore differs between prod and develop; committing
  it on develop will conflict with prod on the next develop->prod merge (resolve by taking the newer content).
- Pattern since 1.4.0: release commit on develop, `Merge branch 'develop' into prod` (--no-ff) in the main
  worktree (prod checked out there), tag the prod merge commit. develop may be checked out in `D:\wt\a`.
- Note: `da623ed` (analysis-card line breaks fix) landed on develop after the 1.8.0 release commit but was
  merged into prod before tagging `v1.8.0`, so it shipped in 1.8.0 without a changelog line.
- `src/frontend/node_modules` exists only in the main worktree; to run the changelog vitest in another worktree,
  junction it temporarily (gitignored) and remove the junction afterwards.

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

## Release log

| Version | Date | Commit (prod) | Deploy | Notes |
|---|---|---|---|---|
| 1.15.0 | 2026-11-17 | 738c926 | run 44018545211 success | financial-statement and monthly sales-trend answers cost 2 credits |
| 1.14.0 | 2026-11-16 | 0e2838e | run 43882866503 success | answers from the quarterly income statement for unanswered single-symbol questions (flag-gated); TahlilApp analyses only from the last 30 days; Report Discussion 2 credits + «… را تفسیر کن» suggestion |
| 1.13.1 | 2026-11-16 | 71c0614 | run 43802887944 success | Report Discussion analysis follow-ups no longer fail (BUG-031; feature flag-gated) |
| 1.13.0 | 2026-11-15 | 1f4ab9ef | run 43747994724 success | self-service profile editing on web and mobile; assistant response system update |
| 1.11.0 | 2026-11-12 | 34e0b32 | run 43160802312 success | answers from financial statements (one income-statement item, multi-company comparison) and monthly product reports, safe refusal; desktop shows the question while its answer is prepared (BUG-023) |
| 1.10.0 | 2026-11-11 | 7b69391 | run 43054189283 success | follow-up suggestion boxes on balance sheet and other single-symbol answers; «تحلیل X» takes the comprehensive-analysis route |
| 1.9.0 | 2026-11-11 | 8465aa9 | run 43041307287 success | full TahlilApp analysis text for Max; technical-analysis route; unavailable valuation topics refused without charge; price-unit and "Found metric data" fixes |
| 1.8.0 | 2026-11-11 | 6c960b1 | run 42917616701 success | TahlilApp comprehensive-analysis card (preview); Telegram /unlink and link-failure reasons |
| 1.7.0 | 2026-11-10 | 14dadae | run 42830596519 success | |
| 1.6.0 | 2026-11-09 | 48b8fd9 | run 42686015806 success | |
| 1.5.0 | 2026-11-09 | dfda6b9 | run 42631239073 success | |
| 1.4.0 | 2026-11-09 | bae1c5e | run 42615018199 success | user analytics dashboard, conditional CyclicalWaves valuation queries and P/S gauge, product-mix sales trend fix |
| 1.2.0 | 2026-11-07 | 4f34a2b | run 42397525865 success | balance sheet in symbol menu, Sapio name+link on copied/shared answers, lower server load (indexes), stock-screening null-threshold fix |
| 1.1.0 | 2026-11-07 | 0cdcd4c | success, run 42289387091 | monthly-sales summary cards, answer actions, analysis stages, market-column symbol menu, Persian number style, /releases page |
| 1.0.0 | 2026-11-06 | 9cf098c | success (as v1.3.6) | first stable; renumbered from v1.3.6 |
