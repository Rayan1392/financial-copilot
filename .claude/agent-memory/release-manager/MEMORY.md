# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.

## Release 1.24.0

- Published on 2026-11-23 (MINOR: Changed with new capability and additive API fields). Feature commit
  `0b5f3014`, develop release commit `9936c234`, prod merge `3f2d361a`, annotated tag `v1.24.0` (tag object
  `80df2845`). Deploy run 45256376423 succeeded in about 10m20s. `/releases` returned the new build and lists
  1.24.0 first.
- Admin conversation log: rows without a tenant user account show the actor kind (configured API client name or
  unknown) and ID, with an actor dialog (copy, filter by ID). No database migration or Telegram gateway change.
  Release-note tests passed 12/12 and the tag/changelog check passed.
- Open: confirm on production that these rows resolve to API client names (client list is server configuration
  only). Backlog: API-client conversations are labelled «وب».

## Release 1.23.1

- Tagged on 2026-11-23 (PATCH: Fixed). Fix commit `998a1082`, develop release commit `79187b89`, prod
  merge `59bce290`, annotated tag `v1.23.1` (tag object `a5757658`). Deploy run 45226817725 succeeded
  (outcome confirmed while releasing 1.24.0).
- BUG-045: the admin conversation log opens Customer 360 for accounts in the effective tenant and retains
  the question and answer for rows with no account. No database migration or Telegram gateway change.
  Release-note tests passed 12/12 and the tag/changelog check passed.

## Release 1.23.0

- Published on 2026-11-23 (MINOR: Added). Feature commit `81525218`, develop release commit `4fb27a7f`, prod
  merge `50c652c7`, annotated tag `v1.23.0` (tag object `a8c2198`). Deploy run 45211036100 passed and
  `/releases` was verified to show 1.23.0 as current.
- The admin conversation log opens Customer 360 from the user cell. The shared dialog supports manual credit
  grants for authorized administrators and has a responsive RTL layout. No database migration or Telegram
  gateway change. Release-note tests passed 12/12 and the tag/changelog check passed.

## Release 1.22.0

- Published on 2026-11-22 (MINOR: Added). Develop release commit `4e7a4cfc`, prod merge `32810830`, annotated tag
  `v1.22.0`. Deploy run 45065263416 succeeded in about 9m40s. `/releases` returned HTTP 200 and lists 1.22.0.
- Ships Feature 165 in-app messages (`3ac47a08`) and the 162 change `local-company-data-context` (`136a131a`, which
  also carries Feature 125 CyclicalWaves v2 relative valuation, off in production by default).
- Migrations ran with no backup, acknowledged by the owner: `Feature165InAppMessages` (Auth) and
  `AddCyclicalWavesV2RelativeValuation` (FinancialIngestion, nullable columns and an index only).
- Open: 165 verification tasks (browser walkthrough, access tests, rollout docs; OQ-129); 162 owner live check AC-12
  (T25); 4 frontend 165 tests timed out locally; unit tests `AnalyticalAnswerAndReportScoreTests` and
  `NavigatorEvaluationReliabilityTests` failed outside the baseline and are uninvestigated. No Telegram gateway changes.

## Release 1.21.0

- Published on 2026-11-21. Annotated tag `v1.21.0` points to prod merge `5beaa9b4`.
- Deploy run 44945400621 succeeded in 9m46s. The public release page returned the new version. The 23 release-note and cash-flow tests passed.

## Current state (2026-11-18)

- Latest released version: **1.17.1** (PATCH: Fixed only), annotated tag `v1.17.1` (tag object
  `b685c747d28be36c683c931ae8a2b26a78d8b843`) on prod merge commit `bc38de3e` ("Merge develop into prod for
  v1.17.1"; parents prod `3c68b8f`, develop release commit `da7fe4ef`). Deploy run 44307527081 succeeded in 8m53s.
  `/releases` returned HTTP 200 with 1.17.1 marked as the current version; API health returned HTTP 200 `Healthy`.
- 1.17.1 ships the BUG-033 billing fix: successful financial-report discussions settle at the fixed two-credit price
  instead of failing when the selected base operation would otherwise charge more. Focused integration tests passed
  35; 7 PostgreSQL tests were skipped because database connection configuration was unavailable. Release-note tests
  passed 12/12. No database migration.
- Base for "commits since last release": `v1.17.1`.
- Previous: 1.17.0 (MINOR: notable Changed), annotated tag `v1.17.0` (tag object `88b9093`) on
  prod merge commit `3c68b8f` ("Merge develop into prod for v1.17.0"; parents prod `dfd4730`, develop release commit
  `33aefd8`). Deploy run 44228930762 succeeded in 3m58s. `/releases` returned HTTP 200 with 1.17.0 as the newest
  heading. The server reported API healthy (migrations/startup), frontend 200, and worker running without restarting.
- 1.17.0 ships Dastkhat Main and FaNum typography for desktop and mobile PWA, with the Dastkhat font assets cached
  offline. No database migration. Release-note tests passed 12/12. Broader frontend baseline failures and the open
  desktop overflow criterion are documented in `specs/153-mobile-pwa-workspace/changes/dastkhat-font-typography/ImplementationReport.md`.
- Previous: 1.16.0 (MINOR: notable Changed), annotated tag `v1.16.0` (tag object `d46ebc1`) on
  prod merge commit `dfd4730` ("Merge develop into prod for v1.16.0"; parents prod `63161ec`, develop release commit
  `71df9ed`). Deploy run 44158131482 succeeded in 9m30s. `/releases` HTTP 200, newest heading 1.16.0.
- 1.16.0 ships Feature 163 change `answer-readability` (`3c696e6`, merge `7718f98`): shared answer layout rules for
  the 163 and 162 prompts, `GroundedAnswerLayout.Normalize` (thousands separators), web typography for all Markdown
  answers, Telegram bold in the API renderer (not the gateway host). No migrations. The real-model layout check (T7)
  was still open at release. The owner had merged develop into prod (`63161ec`) before the CHANGELOG commit, so the
  release commit was merged into prod a second time (merge-tree + commit-tree) before tagging.
- Previous: 1.15.1 (PATCH, Fixed only), annotated tag `v1.15.1` (tag object `d4f4fae`) on prod
  merge commit `842fa37` ("Merge develop into prod for v1.15.1"; parents prod `738c926`, develop release commit
  `3131c52`). Deploy run 44051050496 succeeded in 9m4s. `/releases` HTTP 200, newest heading 1.15.1.
- 1.15.1 ships BUG-032 (`9431584`, merge `219d5a3`): «سهم X را تحلیل/بررسی کن» take the comprehensive-analysis
  route; the agent-tool "not found" card keeps the 30-day sentence. No migrations. The 163 fallback behind it needs
  `SymbolGroundedFallback__Enabled=true` in the server `.env`; on 2026-11-17 the owner's key was misspelled
  («SymbloGroundedFallback»), so 163 was likely off in prod — not verified after the release.
- 2026-11-17 (1.15.1): a worktree under the scratchpad failed (path too long), so the prod merge was built with
  `git merge-tree --write-tree` + `git commit-tree` and pushed as `<sha>:refs/heads/prod`.
- prod differs from develop in 4 non-app files (`docker-compose.override.yml`, its `.bak-20261103`,
  `.deepcode/settings.json`, an `implementing-domain-driven-design/SKILL.md`); unchanged since at least 1.15.0.
- Previous: 1.15.0 (MINOR: notable Changed), annotated tag `v1.15.0` (tag object `81a8814`) on
  prod merge commit `738c926` ("Merge develop into prod for v1.15.0"; parents prod `7070079`, develop release
  commit `dd1947b`). Deploy run 44018545211 succeeded in 8m23s. `/releases` HTTP 200 and its HTML contains 1.15.0.
- 1.15.0 ships financial-query credit pricing (`e6c0d02`): answered financial-statement table/analysis and
  monthly sales/product trend turns cost 2 credits (were 1 via `AiQuery.Scanner`); NoData stays free. No
  migrations. The owner had merged develop into prod (`7070079`) before the CHANGELOG commit, so the release
  commit was merged into prod a second time before tagging.
- 2026-11-17: the owner ran the worktree commands from PowerShell, where `$TMP` was empty; the worktree landed at
  `C:/prod-release` and its removal failed while the shell's cwd was inside it (empty folder left behind, git
  worktree metadata gone). Use an explicit absolute path when handing PowerShell commands to the owner.
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
| 1.20.1 | 2026-11-21 | c4daca91 | run 44819110116 success (3m52s) | PATCH: BUG-038, Feature 161 cash flow report keeps a fixed light surface in both site themes (`dark:` classes followed the OS, not the site theme). Fix `dc31e32b`, docs `13c5a07c`, release `adba40db`. prod merge built with `git merge-tree --write-tree` + `git commit-tree` (no prod checkout); the coordinator pushed and tagged on the owner's direct approval in the session. `/releases` lists 1.20.1. No migrations, no Telegram gateway change. Open: BUG-039 (legend contrast), BUG-040 (2 pre-existing Vitest failures). |
| 1.20.0 | 2026-11-20 | 82970ec3 | run 44810836856 success (9m03s) | Feature 164 admin page «تاریخچه پرسش و پاسخ کاربران» (read-only, admin.users.read) + BUG-037 fix in 149 customer chat history. Migration `20261120193227_AddMessagesUserCreatedAtIndex` (partial index on Messages, CREATE INDEX CONCURRENTLY) ran at API start; owner acknowledged no backup. API Healthy, frontend 200, worker steady; `/releases` shows 1.20.0 as current. prod was merged twice for this version (`de728c78`, then `82970ec3` with the admin menu overflow fix `d5a4e833`); the tag is on the second. Open: OQ-120 (status-filter count exceeds 5 s timeout), 164 task 7.2 production read-only EXPLAIN; user-search slowness reported by the owner was not reproduced locally (DB steps 2–16 ms), no index added. No Telegram gateway change. |
| 1.19.0 | 2026-11-20 | f975690e | run 44801484039 success (20m40s) | grounded analysis on (Simple/Deep, web search, 8/12/16/25 credits, 163 offer after «تحلیل کن»); Feature 081 period summary switched off (its questions get the 163 offer); BUG-035 fix. No migrations. Telegram gateway change (offer button answered first) needs the separate gateway host updated. Launch items left to the owner: `.env` WebSearch capability on the OpenAI provider, `TELEGRAM_GATEWAY_REQUEST_TIMEOUT_SECONDS=120` on the gateway host, reverse proxy ≥120 s; T00/T15 of change sol-deep-analysis-web-search still open. Not merged: `feature/release-1.17.0-dastkhat` (rollback override, skill deletion), Feature 164 (uncommitted in another worktree). |
| 1.18.0 | 2026-11-18 | 420382fe | run 44326157068 success | analytical financial-report answers (162/163): amounts in همت / میلیارد تومان, newest-period analysis, 0–100 score for quarterly income statements |
| 1.17.1 | 2026-11-18 | bc38de3e | run 44307527081 success | BUG-033: fixed-price settlement for successful Report Discussion answers |
| 1.17.0 | 2026-11-18 | 3c68b8f | run 44228930762 success | Dastkhat Main + FaNum typography for web/mobile PWA; offline font cache |
| 1.16.0 | 2026-11-17 | dfd4730 | run 44158131482 success | readable layout for grounded report answers (163/162), web typography, Telegram bold |
| 1.15.1 | 2026-11-17 | 842fa37 | run 44051050496 success | «سهم X را تحلیل/بررسی کن» take the analysis route (BUG-032) |
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
