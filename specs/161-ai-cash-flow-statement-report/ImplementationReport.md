# Feature 161 implementation report

Status: partially implemented locally; not committed, pushed, or deployed. Feature 161 is enabled only in local Development.

## Owner-approved UI redesign (2026-11-15)

- Removed the KPI card grid. Five headline figures are now shown in a flat summary, while secondary indicators sit in a collapsed text list.
- Consolidated repeated warnings and notices while retaining distinct severity, message, and period scope.
- Replaced the wide, horizontally scrolling five-quarter table with a compact desktop table. The latest quarter's comparison is shown inside its amount cell. On mobile, each statement section stacks period/value pairs newest first.
- Aligned the PNG export with the new summary, comparison, and warning presentation.
- Focused frontend verification passed: cash-flow Vitest suite (8 tests), ESLint and Prettier on touched files, isolated Vite production build, and browser checks at 320, 360, 390, 768, 1024, 1280, and 1440 CSS pixels. No horizontal page overflow was detected; the desktop table fit its container and mobile sections had no horizontal overflow.
- Screenshots were visually reviewed in RTL light and dark layouts, and desktop/mobile PNG exports were reviewed. The visual fixture is explicitly labeled as synthetic; it validates layout only and is not evidence for live فولاد accounting figures.
- Sign-off and evidence are recorded in [evidence/browser/cash-flow-ui-redesign-signoff.md](evidence/browser/cash-flow-ui-redesign-signoff.md).
- The API remains stopped at the owner's request. No live query was sent. The isolated frontend build output is `src/frontend/dist/cash-flow-ui-redesign-validation`.

This UI sign-off closes only the owner-approved frontend redesign tasks. It does not mark Feature 161 as a whole complete; the open backend, database integration, and live-data verification items below remain open.

### Follow-up display correction

- Currency-unit labels are no longer repeated beside each monetary KPI; the report header states the unit once. Ratio units such as percent and multiple remain with their values.
- The unavailable FX-share explanation for mixed-sign changes is localized in Persian. The fallback for unrecognized metric units/reasons is also Persian, so internal codes are not exposed in the report.
- Applied to both the on-screen report and PNG export.

## Verified

- The application core derives Codal operating cash flow as item 122 minus item 121, carries item 128 as a cross-check, and uses the derived value for reconciliation, metrics, and waterfall inputs.
- Default report selection returns five latest independent-quarter columns when the request does not specify a period.
- API project Release build succeeded with 0 warnings and 0 errors.
- Targeted backend suite passed: 207 tests across `CashFlowReport161` and `AssistantHelpReply121Tests`.
- Targeted frontend suites passed: 4 cash-flow report tests and 9 message-list tests.
- Browser fixture passed RTL and page-overflow checks at 320, 360, 390, 768, 1280, and 1440 px. Desktop and mobile PNG exports completed; light and dark screenshots are recorded in `evidence/browser/`.
- Frontend production compilation succeeded when directed to the isolated output folder `dist/feature161-validation`.
- No schema migration was added. The default `appsettings.json` flag remains `false`; the local Development override is `true` for owner-approved verification.
- The quarterly table now follows the income-statement and balance-sheet palette: pale teal headers, apricot row labels, yellow subtotal rows, and rose cells for negative signed amounts. Quarter-over-quarter comparison shows the amount delta and a percentage only when the server reports a meaningful percentage; the latest independent quarter is compared with the immediately preceding independent quarter.
- The downloadable PNG table uses the same period comparison and visual cues as the on-screen table.

## Open verification and implementation work

- `CashFlowReport161PostgreSqlTests` compiled but skipped because `FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING` is not configured. The Development database was not used as an integration-test target.
- The local Development API was rebuilt and restarted with Feature 161 enabled. Its `/health` endpoint returned HTTP 200 after startup; startup migrations completed without error. The production/default flag remains off.
- The owner supplied a live-query response that returned the legacy all-line-items table, confirming the previous running service was not executing Feature 161. A new billed query was not issued after restart; live financial output checks for the requested sample set remain open.
- Cash-flow V2 endpoint coverage for typed persistence/replay, billing, no-data outcomes, provider name, and company isolation now exists in `CashFlowReport161EndpointTests` using a deterministic report-use-case fixture. Real database-backed endpoint evidence is still open under T11/T20.
- Routing coverage does not yet exercise every matrix row and U1–U7 on both V2 paths; the direct V1 freeze assertions are focused, not a full route snapshot comparison.
- Frontend fixtures include A, B, quarterly gaps, and a failed outcome, but not every API outcome. UI state coverage, bank-specific presentation assertions, and dedicated quarter cards remain incomplete.
- The on-screen waterfall currently displays step values and bars from typed `from`/`to` data; export content includes the statement table and metadata, but is not yet a full UX-spec export for all KPIs, waterfall visuals, and every notice. The frontend appearance update has not yet been rerun through its automated browser sign-off.
- `npm run build` could not clear the default `dist/client/sapio-logo.png` while the local development server held the file (`EPERM`). The same Vite build completed successfully using the isolated output folder. Full `npm run lint` fails repository-wide with 43,090 CRLF/Prettier errors and 13 warnings; the new Feature 161 frontend files pass focused ESLint with no warnings. An earlier full `npm run typecheck` run surfaced unrelated existing route/test-fixture errors; it was not rerun after the final frontend edits. Full `npm run test` was not run.
- `CHANGELOG.md` is pending. Per `.claude/agents/release-manager.md`, a user-facing Unreleased line is recorded after the change lands on `develop`; this work remains local and uncommitted.

## Evidence

- Browser visual evidence: [`evidence/browser/visual-signoff.md`](evidence/browser/visual-signoff.md)
- PostgreSQL reader test: `tests/FinancialCopilot.IntegrationTests/CashFlowReport161PostgreSqlTests.cs`
- Unit tests: `tests/FinancialCopilot.UnitTests/CashFlowReport161*Tests.cs`
- Frontend fixture test: `src/frontend/src/components/app/__tests__/cash-flow-report.test.tsx`

## Accounting and analytics enhancement — verification addendum (2026-11-15)

The owner-approved enhancement in `changes/financial-accounting-analytics/` is implemented locally. This addendum supersedes older statements above that the new frontend appearance had not yet received browser sign-off; those statements describe the earlier pass. The historical Feature 161 baseline is still incomplete.

### Changes in this enhancement

- Backend: the typed cash-flow contract now exposes period metadata, separate main-total and cash-balance checks, and fail-closed per-section detail coverage. Bounded all-source reads identify unknown catalog items; over-limit reads cannot appear complete. Computed residuals retain their formula and statement/line/item provenance. Period-specific opening/closing labels and conservative verified-input analytics are applied without promoting unresolved mappings.
- Frontend: replaced the remaining English fallback states/reasons with Persian text, kept the currency unit in the header once, added a separate comparison column and expanded accounting detail, and refined the PNG to include metadata, validation, supported metrics, a zero-based flow-only chart, table, and source notes. PNG height now follows rendered content.
- No database migration, source-data write, route addition, V1 change, commit, push, or deploy was made in this enhancement pass. The Development API was not started; builds did not run it.

### Verification

- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --filter "FullyQualifiedName~CashFlowReport161" --no-restore`: 85 passed.
- `dotnet build src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --configuration Release --no-restore`: succeeded, 0 warnings and 0 errors.
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --filter "FullyQualifiedName~CashFlowReport161PostgreSqlTests" --no-restore`: compiled and skipped because `FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING` is not configured. The fixture requires an isolated PostgreSQL administrative database; the Development application database was not used.
- Feature 161 frontend Vitest: 9 passed. Income Statement and Balance Sheet regression tests: 29 passed. Targeted ESLint and Prettier checks passed.
- Browser fixture passed RTL and no-overflow checks at 320, 360, 390, 768, 1024, 1280, and 1440 CSS pixels. Desktop, mobile, dark-theme, and PNG output are recorded in `evidence/browser/` and are synthetic layout evidence only.
- The full frontend `typecheck` remains red with 37 existing errors across unrelated route, test-fixture, and chart files. After this change's local fixes, no `cash-flow-report` errors remain in its output.
- The final isolated production build succeeded for both client and server bundles at `src/frontend/dist/cash-flow-feature161-final-1612-r2`. Existing warnings remain for a test-named route file and large application chunks.
- Vite copied `.dev.vars` into the two isolated build output directories. Cleanup attempts were rejected by the automatic safety policy, so the local build artifacts remain; their contents were not read or exposed.

### Data limits and remaining review

- The read-only Development review found no source statements for خودرو، فولاد, or وبملت; no live company values can be certified from this pass. Synthetic fixtures are not live financial evidence.
- The isolated PostgreSQL integration test could not execute, and endpoint/persistence/billing/provider integration remains open. An independent peer review also remains open; see D03 and D06 in the enhancement task list.
- Proposed and industry-limited source mappings remain unresolved and are not treated as verified. Operating detail coverage therefore remains explicitly insufficient where no approved detail mapping exists.

## Financial query credit pricing enhancement — verification addendum (2026-11-16)

The owner-approved pricing change in `changes/financial-query-credit-pricing/` is implemented locally.

### Changes

- V2 billing now selects `AiQuery.FinancialStatement` for cash-flow, income-statement, balance-sheet, and financial-statement period-analysis requests. When billing reserves before a semantic frame exists, the existing deterministic statement rules—including quarterly-report tables—select the same operation.
- V2 monthly company and product sales trends use `AiQuery.MonthlySalesTrend`. Both operation codes cost exactly two credits in the existing default pricing policy; unrelated requests keep the existing scanner operation.
- The existing no-data and other zero-charge statuses, completion multipliers, and V1 billing path are unchanged. No migration, runtime configuration, secret, release, or deployment change was made.

### Verification

- `CashFlowReport161`, selector, and new default-pricing unit cases: 100 passed. The pricing cases assert two credits for completed turns and zero for `NoData` on both new operation codes.
- Focused V2 integration set: 14 passed. It includes the cash-flow report endpoint (typed persistence/replay, exact billing, no-data, provider name, and company isolation), cash-flow statement table, quarterly income statement, balance-sheet, period-analysis, monthly company/product trend charges, and Feature 161 route/V1 baselines. The completed statement and trend charge assertions confirm the expected operation code and exactly two credits; persisted-message replay keeps one ledger charge.
- The focused V2 financial-statement table test `V2AiQuery_FullIncomeStatementTable_ReturnsStructuredTableWithoutToolLoop` still fails because the test response has no `source` value before the pricing assertion. A broader filter also found stale monthly-sales expected values (0.095 expected, 0.08975 actual) and several quarterly table cases with null result carriers; those failures occur before the new billing assertions. They are recorded as unresolved integration failures, not treated as passing evidence.
- The required full command `dotnet test src/backend/FinancialCopilot.sln --configuration Release` exited nonzero. The architecture test's whole-file scan also matched statement rules used by a different executor; its scope was corrected to the migrated executors it is intended to guard, and the architecture project then passed 17/17. The unit run reported a CyclicalWaves fixture-manifest mismatch. Integration tests reported 199 failed, 676 passed, and 232 skipped, including unrelated endpoint failures and the monthly-sales mismatch above. The full solution was not rerun after the architecture-test correction, so Feature 161's overall Definition of Done remains open.
- PostgreSQL and live Development checks were not rerun. The isolated PostgreSQL connection variable is unavailable in this environment; no billed live Development query was sent.

This addendum closes the credit-pricing change only. Feature 161 remains partially implemented: its own V2 report persistence/provider endpoint coverage, full routing matrix, PostgreSQL verification, live-data acceptance checks, and other listed tasks remain open in `tasks.md`.
- The working tree contains many unrelated pre-existing modifications and other feature folders. This enhancement remains uncommitted; only files listed under Feature 161 belong to this change.
