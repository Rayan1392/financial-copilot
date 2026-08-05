# Feature 116 Tasks — AI Sales-Growth Symbol Scanner

Task 1 has been implemented as a repository-backed reuse and gap review. The
remaining tasks are implementation work and remain unchecked until their
acceptance criteria pass.

## [x] Task 1 — Confirm Reuse Boundaries and Gap Analysis

Implementation evidence: [task-1-gap-analysis.md](task-1-gap-analysis.md).

The evidence records the existing components to reuse, confirms non-overlap
with single-symbol, trend, product-mix, and generic scanner routes, and lists
the missing comparison, common-period, evidence, and renderer semantics that
must be implemented by later tasks.

Review Features `003`, `006`, `007`, `008`, `009`, `015`, `026`, `045`, `057`, `069`-`077`, and `089` plus the current implementation before adding code.

Document which existing components are reused for:

- monthly sales values;
- `MONTHLY_SALES_GROWTH_MOM`;
- `MONTHLY_SALES_GROWTH_YOY`;
- 12-month average values;
- natural-language scanner parsing;
- table rendering and Telegram pagination;
- evidence, Billing, and telemetry.

Acceptance:

- No duplicate parser, calculator, scanner engine, provider adapter, or table system is introduced.
- Any missing calculation semantics are identified before implementation.
- Existing single-symbol and trend routes remain authoritative for their use cases.

## [x] Task 2 — Define Governed Sales-Growth Scanner Semantics

Implementation evidence: [task-2-semantic-contract.md](task-2-semantic-contract.md).

The application contract and focused unit tests define the canonical intent,
baselines, threshold kinds, operator/origin reuse, versioned policies, formulas,
and invalid-threshold invariants.

Define canonical types/contracts for:

- `SalesGrowthSymbolScanner` intent/use case;
- comparison baseline:
  - `PreviousMonth`;
  - `SameMonthPreviousYear`;
  - `AveragePrevious12Months`;
- threshold kind:
  - `Positive`;
  - `Percent`;
  - `Multiple`;
- comparison operator;
- explicit/inferred/clarified origin;
- target-period policy and version;
- sales-growth calculation policy and version.

Acceptance:

- The contract contains no SQL, raw provider DTO, or executable user expression.
- `Positive` means `CurrentSales > BaselineSales`.
- Percentage and multiple formulas are canonical and versioned.
- `2×` is documented as current/baseline `2.0`, equivalent to `100%` growth.

## [x] Task 3 — Add Alias and Intent Coverage

Implementation evidence: [task-3-alias-intent-coverage.md](task-3-alias-intent-coverage.md).

The governed semantic catalog, alias normalization, and reusable sales-growth
intent predicate now cover Persian, English, mixed-language, ZWNJ, digit,
decimal, percent, and multiple variants. Routing precedence remains Task 4.

Extend the governed alias/intent registry rather than embedding phrase lists only in prompts.

Cover normalized Persian, English, and mixed forms for:

- list/discovery terms: `لیست`, `فهرست`, `کدام سهم‌ها`, `چه نمادهایی`, `شرکت‌هایی که`;
- growth terms: `رشد`, `افزایش`, `بیشتر شده`, `بهبود فروش`, `بالاتر رفته`, `چند برابر شده`;
- sales terms: `فروش`, `فروش ماهانه`, supported governed revenue wording;
- comparison terms:
  - `ماه قبل`, `دوره قبل`, `MoM`;
  - `سال گذشته`, `پارسال`, `ماه مشابه سال قبل`, `دوره مشابه سال قبل`, `YoY`;
  - `میانگین ۱۲ ماهه`, `متوسط دوازده ماه`, `12-month average`;
- numeric forms:
  - Persian and Latin digits;
  - `%`, `درصد`, `percent`;
  - decimal comma/dot where supported;
  - `برابر`, `x`, `×`, `times`.

Acceptance:

- Normalization handles Persian/Arabic characters, ZWNJ, punctuation, spacing, and digit variants.
- The LLM may propose intent/parameters, but backend registry validation determines the executable result.
- Unknown or conflicting comparison phrases fail safely or clarify.

## [x] Task 4 — Implement Routing and Precedence

Implementation evidence: [task-4-routing-precedence.md](task-4-routing-precedence.md).

Feature 116 discovery requests now route deterministically to the existing
scanner boundary while single-symbol growth lookups and existing trend/product
mix routes retain their established precedence.

Integrate the use case into active and rollback AI routing paths.

Rules:

- plural/list + sales + growth routes to the scanner;
- single-symbol growth lookup stays on the single-symbol path;
- trend/chart questions stay on Feature `077`;
- product mix stays on Feature `075`;
- generic scanner supports composition when additional filters are requested;
- unresolved universe or conflicting period semantics follow existing clarification behavior.

Acceptance:

- Deterministic precedence tests cover all safeguards.
- Provider names and model-specific logic do not appear in intent selection.
- Feature behavior is identical across configured AI providers after structured validation.

## [x] Task 5 — Extend the Scanner Query Plan and Validator

Implementation evidence: [task-5-query-plan-validator.md](task-5-query-plan-validator.md).

The generic scanner plan now carries an optional governed sales-growth plan,
and validation enforces canonical selectors, thresholds, operators, universe,
sorting, pagination, and display-column limits.

Add or specialize validated plan fields for:

- current monthly observation selector;
- comparison baseline;
- threshold kind/operator/value;
- baseline and threshold origin;
- market universe;
- target common period;
- sort and pagination;
- requested display columns.

Implement validation for:

- supported operators;
- non-null and valid numeric thresholds;
- positive multiple values;
- configured default baseline policy;
- maximum page size/result limit;
- no arbitrary formula or SQL.

Acceptance:

- Generic `رشد فروش` creates `GrowthPercent > 0` using the governed default baseline and marks it `inferred-default`.
- Explicit wording is retained as evidence.
- Strict versus inclusive operators are preserved.

## [x] Task 6 — Implement Common Evaluation-Period Selection

Define a deterministic market-wide monthly evaluation cutoff.

Responsibilities:

- find candidate complete monthly periods;
- measure eligible-symbol coverage;
- select the latest period satisfying configurable minimum coverage;
- record target period, coverage numerator/denominator, and policy version;
- prevent silent ranking across unrelated latest periods.

Suggested configuration:

```json
{
  "SalesGrowthScanner": {
    "Enabled": true,
    "DefaultComparisonBaseline": "SameMonthPreviousYear",
    "AllowDefaultComparison": true,
    "MinimumCommonPeriodCoveragePercent": 70,
    "DefaultPageSize": 20,
    "MaximumPageSize": 100,
    "AllowMixedLatestPeriods": false
  }
}
```

Acceptance:

- Invalid option values fail startup validation.
- If no common period satisfies policy, return explicit unavailable/partial status rather than silently switching behavior.
- If mixed periods are ever enabled, each row exposes its period and the response declares mixed-period status.

## [x] Task 7 — Reuse or Add Deterministic Comparison Calculations

Implement a provider-neutral read/calculation service that returns, for each eligible symbol:

- current monthly sales and period;
- baseline value and period/window;
- growth difference;
- growth percent;
- growth multiple;
- value states;
- source/evidence/freshness.

Reuse:

- `MONTHLY_SALES_GROWTH_MOM` when policy-equivalent;
- `MONTHLY_SALES_GROWTH_YOY` when policy-equivalent;
- existing average metrics only when the current month is excluded and the window matches this feature.

If the previous-12-month average comparison is missing or semantically different, add a governed derived metric/input policy through Features `006`/`016`, not inline LLM or renderer arithmetic.

Acceptance:

- Decimal arithmetic is deterministic.
- Baseline `<= 0`, missing, invalid, or unusable values never generate infinity or fabricated growth.
- Calculation evidence identifies every input observation.
- Repeated execution on the same evidence snapshot returns identical values.

## [x] Task 8 — Execute Filtering, Sorting, and Pagination

Extend the scanner engine/repository query path to:

- filter by positive, percent, or multiple threshold;
- preserve strict/inclusive operator;
- combine with other supported scanner conditions using existing AND semantics;
- sort by growth percent descending by default;
- apply a stable symbol tie-break;
- paginate within existing limits.

Acceptance:

- The LLM never selects rows or calculates match status.
- Count metadata includes total universe, eligible, evaluated, matched, and excluded-by-reason counts.
- A zero-match execution returns a valid empty table.
- Query execution is bounded and uses appropriate indexes/read models; avoid N+1 symbol reads.

## [x] Task 9 — Define the Structured Result Table

Use the Feature `008` scanner table contract.

Default columns:

1. `نماد`
2. `شرکت`
3. `فروش آخرین دوره`
4. dynamic comparison column:
   - `فروش ماه قبل`;
   - `فروش ماه مشابه سال قبل`;
   - `میانگین فروش ۱۲ ماهه`;
5. `درصد رشد`
6. `نسبت فروش`, when requested by multiple semantics or enabled by policy.

Include in row/result metadata:

- current and baseline periods/window;
- unit/scale;
- provider/source evidence;
- freshness;
- threshold and operator;
- explicit/inferred origin;
- policy/metric versions;
- deterministic match reason.

Acceptance:

- No automatic price, daily-change, valuation, market-cap, score, or debug column is rendered.
- Column count follows Feature `008` limits.
- Missing values render as unavailable, never zero unless zero is a valid observed sales fact.
- Persian formatting uses consistent separators, decimals, and units.

## [x] Task 10 — Implement Explainable Persian Answer Shaping

Create deterministic answer framing for:

- interpreted comparison;
- threshold;
- target period;
- default-policy disclosure;
- coverage/freshness warnings;
- empty results.

Example:

`نمادهایی نمایش داده شده‌اند که فروش آخرین ماه کامل آن‌ها بیش از ۳۰٪ نسبت به ماه مشابه سال قبل رشد کرده است.`

Default disclosure:

`چون مبنای مقایسه مشخص نشده بود، رشد نسبت به ماه مشابه سال قبل در نظر گرفته شد.`

Acceptance:

- The explanation is generated from validated plan/result facts.
- The LLM may summarize but cannot change values, periods, threshold, or comparison.
- No investment-advice language is added.

## [x] Task 11 — Web Conversation Rendering

Add/extend the structured scanner table renderer so that:

- dynamic baseline-column titles render correctly;
- pagination preserves ordering and query interpretation;
- current/baseline period and freshness are visible without cluttering mandatory columns;
- conversation reload restores the same contract version and values;
- RTL layout and Persian number formatting are correct.

Acceptance:

- Rendering requires no provider call.
- Missing/partial data status is visible.
- User-requested extra columns still obey scanner table rules and limits.

## [x] Task 12 — Telegram Rendering and Pagination

Reuse Feature `089` conventions to render compact rows containing:

- symbol;
- current sales;
- baseline sales;
- growth percent;
- optional multiple.

Add:

- comparison/period/freshness footer;
- replay-safe next/previous callbacks;
- deterministic ordering across pages;
- deep link to the full web table where available.

Acceptance:

- Telegram values match web values exactly.
- Pagination does not rerun with changed semantics unless the evidence snapshot is intentionally refreshed and disclosed.
- Long outputs are split without reordering or dropping rows.

## [x] Task 13 — Billing, Entitlement, Security, and Telemetry

Integrate existing:

- plan/entitlement checks;
- Billing reservation/finalization;
- rate and execution limits;
- actor/tenant isolation where applicable;
- audit and correlation identifiers.

Emit telemetry for:

- detected intent and aliases;
- baseline and origin;
- threshold kind/operator/value;
- target period and coverage;
- eligible/evaluated/matched/excluded counts;
- query latency and timeout;
- stale/partial/unavailable status;
- Billing outcome;
- parser ambiguity/failure.

Acceptance:

- User text never becomes arbitrary SQL.
- Failed or duplicate executions do not create duplicate ledger entries.
- Sensitive configuration and provider credentials are never included in responses/logs.

## [x] Task 14 — Unit Tests

Add unit tests for:

- intent detection and routing precedence;
- Persian/Arabic character and ZWNJ normalization;
- Persian/Latin digits and decimals;
- `%`/`درصد` and `برابر` parsing;
- strict versus inclusive operators;
- generic default baseline and disclosure;
- previous-month, same-month-previous-year, and previous-12-month-average resolution;
- `2×` equals `100%` growth equivalence;
- invalid/missing/zero baseline exclusion;
- average window excludes current month;
- deterministic sort/tie-break;
- dynamic column title selection.

## [x] Task 15 — Integration Tests

Create deterministic fixtures covering at least:

1. `لیست سهم‌هایی که رشد فروش داشته‌اند`
2. `لیست سهم‌هایی با رشد فروش بالای ۳۰ درصد نسبت به سال گذشته`
3. `نمادهای با رشد فروش حداقل ۲۰ درصد نسبت به دوره قبل`
4. `سهم‌هایی که نسبت به دوره مشابه سال قبل بیشتر از ۵۰٪ رشد فروش دارند`
5. `لیست سهم‌هایی که فروششان نسبت به ماه قبل رشد کرده`
6. `نمادهایی که فروششان حداقل دو برابر ماه مشابه سال قبل شده`
7. `شرکت‌هایی که فروششان ۱.۵ برابر میانگین ۱۲ ماهه است`
8. no matching symbols;
9. missing previous month;
10. missing same month last year;
11. fewer than 12 eligible average observations;
12. zero baseline;
13. stale provider evidence;
14. common-period coverage below policy;
15. composition with another scanner filter;
16. web/Telegram result parity;
17. Billing failure/no duplicate usage entry;
18. conversation reload contract parity.

Acceptance:

- Exact ordered rows and financial values are asserted.
- No external provider is called during AI query execution.
- Empty/partial/unavailable states contain no fabricated rows.

## [x] Task 16 — AI Regression Dataset

Add golden and adversarial utterances for natural phrasing, including:

- omitted word `لیست` but plural discovery meaning;
- colloquial `کدوما فروششون بهتر شده؟`;
- mixed Persian/English `sales growth بالای 30 درصد`;
- punctuation and spacing variants;
- ambiguous `رشد فروش ماه قبل`;
- single-symbol counterexamples;
- trend/product-mix/net-profit counterexamples;
- prompt-injection attempts requesting SQL or invented symbols.

Acceptance:

- Evaluations assert intent, structured parameters, clarification/default behavior, and routing target.
- The fake/provider-neutral AI model path is used in automated tests.

## [x] Task 17 — Documentation and Checklist

Update:

- `specs/README.md` feature index;
- `specs/implementation-checklist.md` with Feature `116` as pending before implementation;
- semantic alias/metric documentation;
- supported AI question examples;
- Telegram capability documentation where maintained.

Acceptance:

- Documentation states the default comparison policy clearly.
- Dependencies and non-overlap with Features `069`, `070`, `075`, and `077` are explicit.
- Implementation evidence is added only after all completion gates pass.

## Completion Gate

Keep the feature unchecked until:

- all three comparison baselines are deterministic and governed;
- percent/multiple/positive thresholds pass unit and integration tests;
- common-period and missing-baseline policies are implemented;
- parser, active/rollback routing, scanner execution, web, and Telegram regressions pass;
- no synchronous external-provider call exists in the AI request path;
- structured output and conversation reload are deterministic;
- Billing, entitlement, telemetry, and explainability requirements pass;
- existing single-symbol, trend, product-mix, and generic scanner regressions remain green.
