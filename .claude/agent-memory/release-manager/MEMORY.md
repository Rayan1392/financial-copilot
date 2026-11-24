# release-manager memory

Release log and open items. Policy: `docs/release/release-process.md`. Source of truth: `CHANGELOG.md`.
Older detail and the full version table: `release-history.md` (same folder).

## Release 1.24.1

- Published on 2026-11-24 (PATCH, BUG-046). Fix commit `d1f6fd90`, develop release commit `19fc6ee0`,
  prod merge `ed54795e`, annotated tag `v1.24.1` (tag object `066b8b1c`).
- Release-note tests passed 12/12 and the tag/changelog check passed. Deploy run 45390728992 succeeded
  in 11m43s. The public releases page lists 1.24.1 first, API health is healthy, and all three deployed
  containers use image tag `ed54795` with restart count zero.
- The one-time production Instruments sync and valuation coverage verification are separate operations
  following deployment.

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
