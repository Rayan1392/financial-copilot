# Tasks - CyclicalWaves Direct Period Metric Query Coverage

## Task 1 - Audit Existing Coverage

Status: Planned

Requirements:

- Verify that specs `020` and `071` already persist the required CyclicalWaves source fields into
  normalized rows and `DerivedMetrics`.
- Verify whether `NET_PROFIT_MARGIN`, `GROSS_PROFIT_MARGIN`, `OPERATING_PROFIT_MARGIN`,
  `AVG_12M_MONTHLY_SALES`, `MONTHLY_SALES`, `PE_TTM`, and `PS_TTM` exist in the semantic catalog.
- Identify any currently unsupported aliases, display names, or period selectors for the coverage
  matrix in the user story.

Acceptance:

- Produce a small implementation note or test fixture listing supported, unsupported, and newly
  added direct-question phrases.
- Confirm no provider ingestion schema migration is required unless the audit proves a metric is
  not persisted.

## Task 2 - Add Period Selector Model To Symbol Lookup

Status: Planned

Requirements:

- Extend the internal symbol lookup parse result with an optional period selector, for example:
  `LatestQuarter`, `PreviousQuarter`, `SameQuarterLastYear`, `LatestMonth`, `PreviousMonth`,
  `SameMonthLastYear`, `LatestAverage12Month`, and `LastYearAverage12Month`.
- Keep the public `POST /api/ai/v1/query` contract backward-compatible.
- Preserve the original user wording in parser diagnostics and feedback records.
- Do not reintroduce `Symbols`, `SymbolId`, `ISymbolNameResolver`, or `EfCoreSymbolNameResolver`.

Acceptance:

- Parser unit tests prove period words are extracted independently from the symbol phrase.
- Existing PE/PS and monthly-sales parser tests still pass.

## Task 3 - Register Aliases And Display Labels

Status: Planned

Requirements:

- Add Persian and English aliases for all rows in the required coverage matrix.
- Add display-label metadata for each period-specific view:
  - `حاشیه سود خالص آخرین فصل`
  - `حاشیه سود خالص فصل قبل`
  - `حاشیه سود خالص فصل مشابه سال قبل`
  - `حاشیه سود ناخالص آخرین فصل`
  - `حاشیه سود ناخالص فصل قبل`
  - `حاشیه سود ناخالص فصل مشابه سال قبل`
  - `حاشیه سود عملیاتی آخرین فصل`
  - `حاشیه سود عملیاتی فصل قبل`
  - `حاشیه سود عملیاتی فصل مشابه سال قبل`
  - `متوسط فروش ۱۲ ماهه`
  - `متوسط فروش ۱۲ ماهه سال قبل`
  - `فروش آخرین ماه`
  - `فروش ماه قبل`
  - `فروش ماه مشابه سال قبل`
  - `نسبت قیمت به سود`
  - `نسبت قیمت به فروش`
- Add PS aliases if absent: `ps`, `P/S`, `پی به اس`, `قیمت به فروش`, `نسبت قیمت به فروش`.
- Keep longest-match precedence so period-specific aliases win over generic margin or sales aliases.

Acceptance:

- Alias tests cover every row in the matrix.
- Persian display labels do not expose internal metric codes.

## Task 4 - Implement Period-Aware DerivedMetric Lookup

Status: Planned

Requirements:

- Extend `EfCoreSymbolMetricLookupService` or its current equivalent so it can apply the period
  selector while querying `DerivedMetrics` by `ExternalCompanyId` and `MetricCode`.
- For CyclicalWaves-origin queries, use persisted CyclicalWaves source evidence and do not mix
  Noavaran/Codal rows when resolving period-specific snapshots.
- Q0 selects latest-quarter CyclicalWaves metric row; Q1 selects previous-quarter row; Q4 selects
  same-quarter-last-year row.
- M0 selects latest-month row; M1 selects previous-month row; M12 selects same-month-last-year row.
- `last_year_average_12_month_sale` resolves to M12 `AVG_12M_MONTHLY_SALES` and is not calculated
  from M0 averages.
- If the exact requested period row is missing, return Missing/null with a warning instead of
  substituting a different period.

Acceptance:

- Repository/integration tests seed all Q0/Q1/Q4 and M0/M1/M12 rows and prove exact-period retrieval.
- Provider-isolation tests prove CyclicalWaves-origin period lookups do not cite unintended provider
  evidence.

## Task 5 - Rendering Rules

Status: Planned

Requirements:

- Use the coverage matrix Persian display label as the table header for single-metric direct
  lookups.
- Keep monthly-sales snapshot renderer behavior from spec `070` for general sales questions.
- For explicit single-metric requests such as `فروش ماه قبل کچاد؟`, return only identity columns
  and the requested period-specific sales metric unless the monthly snapshot renderer is explicitly
  selected by the existing rules.
- Continue omitting market quote columns from monthly production/sales answers.
- Preserve existing SymbolLookup quote enrichment for PE/PS point lookups only.

Acceptance:

- API integration tests verify table columns for margins, sales, average-sales, PE, and PS.
- Tests verify no `LATEST_PRICE` or `DAILY_CHANGE_PCT` appears in monthly sales/average sales
  responses.
- Tests verify PE/PS point lookups still include quote context when seeded quote data exists.

## Task 6 - End-To-End AI Regression Tests

Status: Planned

Requirements:

- Add end-to-end `POST /api/ai/v1/query` tests for all queries listed in the user story regression
  coverage section.
- Assert resolved symbol, `ExternalCompanyId`, metric code, period selector, display label,
  formatted value, source evidence, confidence score, and absence/presence of quote context.
- Add negative tests for missing Q1/Q4/M1/M12 rows to ensure Missing/null is returned without
  period substitution.

Acceptance:

- All new tests pass with the existing test suite.
- Noavaran monthly-sales regression tests from specs `057`, `069`, and `070` remain green.
- Scanner PE/PS regression tests remain green.

## Task 7 - Documentation And Diagnostics

Status: Planned

Requirements:

- Update the metric-alias/routing registry documentation with period selector examples.
- Add internal routing diagnostics for normalized query, matched alias, resolved metric code,
  period selector, selected renderer, and quote-context decision.
- Add missing-answer feedback mapping for period-specific data gaps and parser limitations.

Acceptance:

- Developers can add another period-aware direct metric without editing unrelated orchestration
  logic.
- Diagnostics are internal or behind the existing diagnostic mechanism and do not change the public
  response contract unless already supported.
