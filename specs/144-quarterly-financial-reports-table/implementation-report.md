# Feature 144 Implementation Report

> Historical report: this documents the original Feature 144 compatibility-policy implementation before the 2026-10-20 EPS/share-basis correction. Its cumulative-EPS subtraction arithmetic and test outcomes are superseded by [the corrected EPS/share-basis investigation](eps-share-basis-investigation.md) and [the regression bug record](BUG-quarterly-eps-share-basis-qasem-2026-10-20.md).


**Outcome: `FEATURE_144_FINANCIAL_COMPATIBILITY_POLICY_UPDATED`**

## Delivery status

- **Tasks with execution evidence:** 41/41. Focused normalizer tests and an actual read-only Development database use-case smoke were rerun after the policy change.
- **Acceptance criteria fully verified:** 21/22. AC 22 remains unverified because three pre-existing Feature 143 handler unit tests still fail for fixture evidence gaps; see below.
- No V1 code, schema migrations, or production financial records were changed. Existing dirty Feature 142/143 worktree files were preserved.
- The confirmed compatibility policy is implemented. AC 22 and full-suite cleanliness remain unverified because unrelated existing Feature 143 and baseline Feature 141 tests fail.

## Acceptance criteria status, individually

| AC | Status | Evidence / remaining verification |
|---|---|---|
| 1 | Verified | `V2QuarterlyTable_PersistsStructuredResult_ReplaysIt_AndChargesOnlyOnce` posts a quarterly request through `/api/ai/v1/query`, asserts `FinancialStatementTableLookup`, and verifies structured output/replay. No V1 code was changed. |
| 2 | Verified | `QuarterlyFinancialReports144ResolutionTests` covers canonical, unresolved, and ambiguous company outcomes. |
| 3 | Verified | The real read-only query returned five consecutive persisted fiscal quarters newest first; Feature 144 tests cover requested periods. |
| 4 | Verified | The real query enforces provider, company, statement type, and standalone scope. Separate/consolidated scope conflicts are rejected; currency/basis/lineage equality is not imposed across adjacent periods. |
| 5 | Verified | Focused tests and real typed result confirm 16 approved rows and ordering; Gross Margin is derived without a SourceItemId. |
| 6 | Verified | PostgreSQL test includes a null `MetricCode` and confirms provider-scoped SourceItemId mapping. |
| 7 | Verified | `Normalize_UsesAdjacentYtdDifferencesAndPreservesZeroMissingAndProvenance`, `Normalize_DoesNotSubtractAcrossMissingIntermediatePeriod`, and non-Esfand calendar coverage verify pass-through/adjacent-period rules. |
| 8 | Verified | Focused tests cover four-level report ranking, scope/provider/type variants, unresolved equal-priority conflicts, missing predecessors/intermediate periods, non-Esfand and unsupported boundaries. |
| 9 | Verified | Decimal arithmetic fixtures assert the revenue Q2 result; no float conversion is used. |
| 10 | Verified | EPS fixture and read-only real-data tests assert Q1/Q2 values and traceable operands; selected real Q2 value is 14,812. |
| 11 | Verified | Unit and PostgreSQL coverage confirm share count is reported unchanged, in thousand shares, without subtraction. |
| 12 | Verified | Focused tests verify Gross Margin as the only derived row and unavailable behavior for absent inputs/zero revenue. |
| 13 | Verified | Typed zero remains `Available` and numeric; PostgreSQL and real-data checks verify display as `-`. |
| 14 | Verified | Focused typed-response and V2 persistence/replay tests preserve null versus zero and their states; frontend component test distinguishes missing from zero. |
| 15 | Verified | Focused tests preserve a six-decimal value and unit scale in typed output, assert signed negative subtraction, and verify accounting-negative presentation; frontend formatting does not replace the typed numeric API value. |
| 16 | Verified | Focused comparison tests cover absolute QoQ, positive-base relative change, and margin percentage-point semantics. |
| 17 | Verified | PostgreSQL test and actual result preserve source report/line, provider, period, scope, unit, revision selection, and calculation provenance where present. |
| 18 | Verified | `markdown-message.test.tsx` verifies Persian row order, RTL direction, five-quarter table structure, negative formatting, zero dash, and missing marker. Pixel-level browser verification is not claimed. |
| 19 | Verified | `V2FinancialStatementAnalysisEndpointTests` verifies typed response persistence and replay, including zero/null and provenance. |
| 20 | Verified | The same V2 endpoint suite passes authorization and once-only Billing assertions. |
| 21 | Verified | The tested Application path reads persisted records only; no query-time provider, generated SQL, or LLM arithmetic path was introduced. |
| 22 | Unverified | The focused V2/Feature 142/143 integration run passed 17/17 (the prior Feature 142/143-only run was 16/16), but the Feature 143 handler unit suite retains three failures; full unchanged behavior is therefore not certified. |

## Feature implementation

- Added typed company-resolution outcomes and a directly callable quarterly Application query use case.
- Updated report selection to rank persisted flags in order: audited+restated, audited+not-restated, unaudited+restated, unaudited+not-restated. `IsAudited` and `IsRepresented` retain their verified meanings; separate/consolidated scope is isolated by `IsComposing`. Existing announcement-date and statement-ID tie-breaks resolve same-priority versions; tied conflicting source values remain unavailable with `conflicting_report_revision`.
- Removed cross-period requirements for equal audit/restatement flags, Currency metadata, accounting basis, or report revision lineage. Same-company/provider/statement type/fiscal year/scope/source identity, adjacent cumulative duration, source unit, and supported `MillionRials` storage-scale checks remain enforced. Missing Currency does not block Noavaran observations under its confirmed provider unit contract.
- Added selected-report, fiscal-period, unit/currency/scale, source-line and source-report provenance, normalization operands, and typed availability/reason metadata to the result.
- Added a PostgreSQL repository integration test for provider/company/type/scope isolation, null `MetricCode`, approved catalog mapping, zero values, 3M/6M/9M/12M periods, duplicate revisions, and provenance.
- Added a local-development read-only smoke test that invokes the actual Application use case and backend table rendering path. The test enforces `default_transaction_read_only=on` and only accepts the local Development database.
- Added a frontend React test for 16-row quarterly table parsing, row order, `dir="rtl"`, zero-as-dash, missing marker, and accounting-negative display.
- Existing V2 structured payload, replay, authorization, and outer Billing integration remains in place; no query-time provider call or generic SQL path was introduced.

## Verification performed

- Release solution build — passed, 0 warnings and 0 errors.
- Feature 144 focused unit tests — 27/27 passed after the policy update, including four-level priority, audited/non-restated precedence, missing Currency, cross-period variant/revision provenance, units, zero and missing behavior.
- Prior implementation evidence records Feature 144 + Feature 131 isolated PostgreSQL tests 8/8 passed. In this policy rerun, those repository tests were skipped because the admin connection variable was unset; the read-only real-data smoke passed 1/1.
- Real-data query — passed again through the actual Application use case for five consecutive quarters of `چخزر`, standalone scope. All 80 cells are now available; the renderer emitted the complete 16×5 Persian table. The selected Q2 revenue is `9,388,580`. See `real-data-validation.md` and linked JSON/Markdown/database evidence.
- V2 financial-statement endpoint suite — 6/6 passed, including structured response, persistence/replay, authorization, and once-only Billing behavior.
- Focused V2/Feature 142/143 integration tests (`V2QuarterlyTable_PersistsStructuredResult_ReplaysIt_AndChargesOnlyOnce`, `NoavaranFinancialStatementBackfillEndpointTests`, `FinancialStatementInventoryTests`, `TelegramFinancialStatement143EndToEndTests`) — 17/17 passed. The Feature 142/143-only classes account for 16 of those passes.
- Frontend `markdown-message.test.tsx` — 18/18 passed, including the new 16-row quarterly RTL table case.
- Feature 141 focused unit tests — 1 passed, 2 failed. Both failures (`ApiClientMustMatchConfiguredApiClientAllowlistAndActorMode`, `UserIdentityRequiresDevelopmentOptInAllowlistAndMatchingServerActor`) were already present at clean HEAD.
- Feature 143 handler unit tests — 5 passed, 3 failed. Exact failures and expected/actual values are documented below. The two completion failures use a fake that returns a run ID but does not persist the completed `SyncRuns` evidence/checksum required by the handler. The interim fiscal-evidence fixture seeds `NadpcoApi`, while the handler queries `NoavaranCurrentApi`. Feature 143 handler logic was not changed.
- Full unit suite evidence from the earlier baseline comparison: 2,129 passed, 21 failed, 0 skipped (2,150 total) versus clean HEAD 2,071 passed, 19 failed. Eighteen failure identities are shared baseline failures: Feature 128 semantic routing (3), financial-source planner handoff (5), structured-query compilation (5), schema navigator (2), Feature 141 developer/UAT authorization (2), and semantic dialogue governance (1). Three current-only failures are the Feature 143 handler cases described above. One baseline-only provider-manifest hash test did not recur. No unrelated Feature 141/143 test was changed to force a green result.

## Real-data evidence and limitations

- Database: local PostgreSQL 18, Development database `financial_copilot`; credentials omitted. Read-only transaction mode was enabled. No production database or source-record mutations were used.
- Scope was explicitly set to standalone (`IsComposing=false`). Audit/restatement preference was not forced; persisted report priority selected each period independently.
- Periods: 1405 Q2, Q1 and 1404 Q4, Q3, Q2. Persisted fiscal-year evidence was used; the selected reports identify fiscal year ends 1405/12/29 and 1404/12/29.
- Before change: 18/80 available (16 nonzero, 2 zero); 62/80 unavailable (44 missing approved source rows, 14 incompatible adjacent metadata, 4 unavailable Gross Margin cells). After change: 80/80 available; 62 newly available; 0 unavailable.
- Remaining typed unavailable states: IncompatibleObservation 0; MissingPredecessor 0; MissingObservation 0; Gross Margin unavailable 0. No unavailable cells or reasons remain in the requested five-quarter table.
- Q2 revenue and EPS now publish the approved arithmetic (`16,754,501 − 7,365,921 = 9,388,580`; `24,967 − 10,155 = 14,812`). The complete selected report identities and flags are in the evidence JSON.
- Noavaran DTOs omit explicit Currency; persisted storage evidence records `AssumedScale=MillionRials`. Typed units are million IRR for monetary rows, IRR/share for SourceItemId 160, thousand shares for SourceItemId 153, and percent for Gross Margin. No exchange-rate conversion is performed.
- Backend rendering, React table parsing, row ordering, RTL direction, zero/missing distinction, and accounting-negative formatting were tested. Browser pixel layout/screenshot verification was not performed.

## Remaining blockers

- The authorized read-only Development query had no unavailable cells. The disposable PostgreSQL repository test was skipped in this rerun because its admin connection variable was unset; prior isolated PostgreSQL evidence remains in the record.
- The known full-suite baseline failures remain; two Feature 141 and three Feature 143 failures are classified above. Focused Feature 142/143 integration coverage passed 16/16; the combined V2/Feature 142/143 filter passed 17/17.
- AC 22 remains unverified as complete. The three current-only failures are unrelated to Feature 144: `Annual_announcement_calls_each_existing_category_service_and_replays_completed_post` and `Partial_retry_reuses_successful_category_checkpoints_and_retries_only_failure` expected `Feature143:Completed` but got `Feature143:ReviewRequired` because their fake does not persist the completed Noavaran `SyncRuns` row/checksum. `Interim_announcement_uses_persisted_fiscal_year_end_evidence` expected 4 calls but got 0 because its fiscal evidence uses provider `NadpcoApi`, while the handler filters `NoavaranCurrentApi`. No Feature 143 handler changes were made.

## Artifacts

- `specs/144-quarterly-financial-reports-table/tasks.md`
- `specs/144-quarterly-financial-reports-table/real-data-validation.md`
- `specs/144-quarterly-financial-reports-table/real-data-validation-result.json`
- `specs/144-quarterly-financial-reports-table/real-data-database-evidence.json`
- `specs/144-quarterly-financial-reports-table/real-data-rendered-answer.md`
