# Feature 141 — Core Capabilities Slice 3b Report: Numeric Threshold Filters (2026-10-29)

Verdict: **FILTERING_SLICE_PASSED**

F141-E15 and F141-E17 are answered end to end with the live model (`OpenAI / gpt-5.6-luna`), the production
binding, planner, validator, SQL compiler, catalog authorizer, restricted read-only executor and Persian renderer, and
an isolated PostgreSQL 18 database. Three metric variations and three essential negatives also pass live. No numeric value was wrong
in any run. The model never writes SQL. No new layer, interface, catalog record or approval workflow was added, and
chat routing is unchanged (statement comparison plans, including filters, are still refused in chat).

## Implemented functionality

One reusable numeric predicate on the existing structured plan:

- Operators `>`, `>=`, `<`, `<=`, `=`; one threshold, or one flat AND of two thresholds.
- Each threshold is on a requested statement item with an approved catalog binding. Net profit, operating profit and
  EPS are covered.
- Both items of an AND are read from **the same statement** of each company: same company, latest common reporting
  period, consolidation basis and provider.
- The threshold must already be in the item's effective unit (MillionIRR, IRRPerShare). No conversion is performed.
  A threshold without a unit is `Incomplete`; any other unit is `Unsupported`.
- Result: the companies that satisfy every predicate, listed by the first measure (descending; an explicit ranking
  direction or top-N is honored), with each condition's value and unit and the applied conditions stated.
- Missing values are never zero. A population member without a filter value is excluded and disclosed; a named
  company without one makes the answer `Incomplete`. Competing statements with different filter values are `Ambiguous`.
- `Unsupported` before SQL: OR, NOT, nested groups, more than two thresholds, monthly measures, periods other than
  latest, items on different statements.

How it maps onto the existing chain:

| Stage | Change |
|---|---|
| Canonical intent (LLM) | None in the contract (`MeasureThresholdFilter`, `FilterGroup` already existed). The canonicalizer gained four deterministic normalizations for observed live variants (below). |
| `StatementQueryShapeClassifier` | New `Filter` shape: threshold leaf or flat AND of ≤ 2, requested statement items, one statement scope. |
| `CanonicalIntentCatalogBinding` | Binds up to two measures for a filter. It checks the threshold unit against the bound effective unit and that both items are on the same statement. The requested-unit check now runs before the shape check, which changes only the reported diagnostic. |
| `FinancialSourcePlannerHandoff.CreateCrossCompany` | Builds typed `PlanFilter(catalogId, operator, Decimal literal)`s and sorts by the listed value. It takes the catalog references to bind the second item. |
| Planner / validator | Unchanged. Filter catalog ids are already part of the required context; `ConditionalCompanyFiltering` was already a registered operation. |
| `CompileStatementComparison` | Joins each filter item on the compared statement and projects `(value <op> @filter_threshold_n)`. Filter items join the query's source list and the queryable, semantic and identity authorization flags. |
| Authorizer / executor | Unchanged. |
| `CrossCompanyStatementComparisonPolicy` | Filters after per-company resolution and comparability, using the database's evaluation of each predicate. |
| `PersianStatementComparisonRenderer` | Filter header, per-condition values and units, exclusion and missing-value notes. |

Compiled predicate (F141-E17); every value is a bound parameter:

```sql
li."Value" AS "FilterValue0", (li."Value" > @filter_threshold_0) AS "FilterMatched0",
lf0."Value" AS "FilterValue1", (lf0."Value" > @filter_threshold_1) AS "FilterMatched1"
...
LEFT JOIN "public"."FinancialStatementLineItems" lf0 ON lf0."FinancialStatementId" = st."Id"
  AND lf0."SourceItemCatalogId" = @filter_source_item_catalog_id_0
```

The predicate is evaluated per row in SQL. It is not a `WHERE` clause, because that would silently pick one of two
conflicting duplicate statements and would misreport an excluded company as a company without data.

## Changed production files

- `Application/FinancialData/Semantics/Navigation/CrossCompanyStatementComparison.cs`: classifier shape,
  `StatementComparisonFilter`, policy filter step, duplicate-statement check on filter values.
- `Navigation/CanonicalIntentCatalogBinding.cs`: filter binding checks, shared `ApprovedMatches`, unit check order.
- `Navigation/FinancialSourcePlannerHandoff.cs`: `ThresholdFilters`, filter requirements.
- `Navigation/StructuredQueryCompilation.cs`: filter compilation; `StructuredSqlDirectSource.TitleFa`.
- `Navigation/StructuredQueryCatalogSnapshotBuilder.cs`: carries the catalog `titleFa`.
- `Navigation/PersianStatementComparisonRenderer.cs`: filter rendering.
- `Intent/CanonicalBusinessMeasureConceptRegistry.cs` (canonicalizer).

Tests: `Feature141CrossCompanyStatementComparisonTests.cs` (unit) and `Feature141EngineVerticalSliceTests.cs` (runner:
filter fixture, eight live cases, intent validation diagnostics in the trace).

## Executed questions and actual answers (final live pass)

Fixture (`proposed.companies`, latest quarter 1405/01/01–1405/03/31): کگل NP 1200 / OP 1500 / EPS 2400; کگهر 900 / 1100 /
1800; فملی 1600 / 2000 / 3200. The prior quarter carries values that would flip every predicate. فولاد (9,999 on every
item) is outside the authorized universe.

| Case | Question | Answer | Expected | Result |
|---|---|---|---|---|
| F141-E15 | شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال کدام‌اند؟ | فملی ۱٬۶۰۰؛ کگل ۱٬۲۰۰ میلیون ریال; ۱ شرکت excluded | فملی, کگل; کگهر excluded | PASS |
| F141-E17 | شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال و EPS بالای ۲۰۰۰ ریال را نشان بده | فملی ۱٬۶۰۰ (EPS ۳٬۲۰۰)؛ کگل ۱٬۲۰۰ (EPS ۲٬۴۰۰) | فملی, کگل | PASS |
| F141-E29 | شرکت‌هایی با سود عملیاتی بیشتر از ۱۲۰۰ میلیون ریال کدام‌اند؟ | فملی ۲٬۰۰۰؛ کگل ۱٬۵۰۰ میلیون ریال | فملی, کگل | PASS |
| F141-E30 | شرکت‌هایی با سود هر سهم کمتر از ۲۵۰۰ ریال کدام‌اند؟ | کگل ۲٬۴۰۰؛ کگهر ۱٬۸۰۰ ریال به ازای هر سهم | کگل, کگهر | PASS |
| F141-E31 | شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیون ریال و سود هر سهم بالای ۲۵۰۰ ریال کدام‌اند؟ | فملی only (کگل passes NP, fails EPS) | فملی | PASS |
| F141-E32 | شرکت‌هایی با سود خالص بالای ۱ میلیارد ریال کدام‌اند؟ | Unsupported `requested_result_unit_conversion_not_supported`, no SQL | Unsupported | PASS |
| F141-E33 | E17 question; کگل has no EPS line | فملی only; "کگل مقدار ثبت‌شده‌ای برای بررسی شرط ندارد … (صفر فرض نمی‌شود)" | فملی, کگل disclosed | PASS |
| F141-E34 | …سود خالص بالای ۱۰۰۰ میلیون ریال یا EPS بالای ۲۰۰۰ ریال… | Unsupported `statement_filter_expression_not_supported`, no SQL | Unsupported | PASS |

Every listed value equals an independent administrative read. فولاد, the prior quarter and the 77,777 decoy item never
appear. Each executed case makes one executor call, and the writable principal is refused
(`database_read_only_identity_check_failed`). Each answered case used 2 LLM calls, about 7.9k input tokens and 10–16 s.

Persian answer, F141-E17:

```text
شرکت‌های دامنهٔ مجاز شما با شرط «سود خالص بیشتر از ۱٬۰۰۰ میلیون ریال» و «سود خالص هر سهم بیشتر از ۲٬۰۰۰ ریال به ازای هر سهم»، در آخرین دورهٔ گزارش مشترک (دورهٔ سه‌ماهه ۱۴۰۵/۰۱/۰۱ تا ۱۴۰۵/۰۳/۳۱ شمسی؛ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۶-۲۱ میلادی):
- فملی: سود خالص ۱٬۶۰۰ میلیون ریال؛ سود خالص هر سهم: ۳٬۲۰۰ ریال به ازای هر سهم
- کگل: سود خالص ۱٬۲۰۰ میلیون ریال؛ سود خالص هر سهم: ۲٬۴۰۰ ریال به ازای هر سهم
مبنا: صورت‌های مالی جداگانه (غیرتلفیقی)؛ واحد سود خالص: میلیون ریال؛ منبع: NoavaranCurrentApi.
همهٔ شرکت‌های دامنهٔ مجاز شما که در این دوره دادهٔ معتبر دارند بررسی شده‌اند.
توضیح: ۱ شرکت دیگر دارای داده، شرط را نداشتند و در فهرست نیامده‌اند.
```

Artifacts: `artifacts/engine-e2e-vertical-slice-2026-10-29-0757*/0758*/0759*-F141-E{15,17,29..34}-*.json`. They are listed in
`engine-e2e-suite-v1.json` and summarized in `engine-e2e-results.json#thresholdFilterSlice3b`.

## Test results

| Run | Scope | Result | Note |
|---|---|---|---|
| Live, first filter run | 8 filter cases | 7/8 | E17: inferred `netProfitValueGrain=companyTotal` constraint |
| Live, full class A | 30 (slice 1, 3a, 3b) | 27/30 | E21 (3a) measure repeated per company; E17/E34 EPS threshold in `irr` |
| Live, full class B | 30 | 28/30 | E29 requested field `companyLabel`; E30 EPS requested unit `irr` |
| **Live, full class final** | **30** | **29/30** | all 8 filter cases and all slice 1/3a answers pass; E24 (3a) gave the correct `Unsupported` outcome, but the shape diagnostic was reported before the unit diagnostic |
| Live, E24 rerun after the binding-order change | 1 | 1/1 | |
| Feature 141 integration without live opt-in | 68 | 36 passed, 31 skipped (live), 1 failed | the failure was the suite-consistency test before E29–E34 were recorded; it passes after the suite update |
| Focused unit (comparison, canonicalizer, binding) | 88 | 88 passed | 3 new filter tests and extended canonicalizer tests |
| Full unit suite | 2,454 | 2,434 passed, 20 failed | same 20 pre-existing failures; the 9 Feature 141 ones (handoff, navigator, UAT) and 5 compilation ones were re-run on pristine HEAD `ff73d3c` (`D:\Source\fc-base141`) and fail there too |

Every failure in the live runs was a typed non-answer. No run produced an executed wrong value. Each model variant is now
normalized deterministically and covered by a unit test:

- An inferred `<concept>ValueGrain=companyTotal` constraint on a statement item is dropped (extends the slice 1 rule).
- An identical canonical measure repeated per company becomes one measure. Different statement scopes are kept.
- Rials (`irr`) on a per-share measure or threshold become rials per share. The magnitude is unchanged; this is the
  registry's reviewed implied unit. `millionIrr` on EPS is still rejected.
- The requested field `companyLabel` or `companySymbol` is dropped for multi-company results (extends the slice 3a rule).

Model variance can still produce new variants. These come back as typed non-answers, not wrong numbers.

Suite (`engine-e2e-suite-v1.json` v1.2.0): 34 cases, 29 PASS, 5 BLOCKED (E10 discovery decisions; E11, E12, E13, E16
next algebra slices), 0 FAIL, 0 wrong numbers.

## Remaining genuine blockers

1. **Monthly threshold filters are not implemented.** Monthly production quantity and monthly sales amount were part of
   the requested lightweight validation. They go through the general planner, and the monthly compiler refuses plan
   filters. The statement filter path returns `Unsupported` (`statement_filter_requires_statement_items`) for them. The
   chat route sends any filtered intent to the general planner. Before chat routing, that path must refuse a monthly
   intent that carries a threshold, so the predicate cannot be silently dropped. That is a small guard.
2. **Population answers need tenant configuration** (unchanged from 3a): discovery scope and reviewed company universe
   (D2/D3) per tenant, and the owner countersign of D1 and the slice 1 directive records.
3. **Population row cap** (unchanged from 3a): more than 100 candidate statement rows returns `Truncated`, not a partial answer.

## Recommendation: fastest path to chat

1. Keep the existing `Feature141:Enabled` flag. In `Feature141ChatQueryService`, route statement-item intents whose
   shape is `MultiCompanyComparison`, `Ranking` or `Filter` to `CreateCrossCompany`, passing the catalog references the
   service already loads. Remove the `StatementComparisonPlans` refusal behind the same flag. The planner, compiler,
   executor and renderer calls are identical to the engine runner's.
2. Add the monthly-threshold guard from blocker 1 in the same change.
3. Configure one pilot tenant's discovery scope and reviewed universe (D2/D3), and get the D1 countersign.
4. Run the live vertical-slice class once through the chat service for that tenant, then enable the flag for the pilot.

No further architecture is needed for this.
