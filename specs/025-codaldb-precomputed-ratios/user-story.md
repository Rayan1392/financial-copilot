# User Story — CodalDB Precomputed Ratios as Scannable Metrics

> Depends on `021`, `022`, `023`. Schema reference:
> [docs/codaldb-datasource.md](../../docs/codaldb-datasource.md).

## Story

As a scanner user,
I want the curated set of CodalDB precomputed financial ratios ingested as first-class,
queryable metrics,
so that I can scan companies by ratios such as Current ratio, ROE, Net profit margin, and Debt
to equity without the platform recalculating them.

## Context

`FinancialRatios` (5.1M rows) holds vendor-**precomputed** ratio values, keyed by `CompanyId`,
its own period columns (`PeriodEnd`/`PeriodType` + Jalali), audited/consolidated/restated flags,
and `ItemID → FinancialRatioItems`. The scanner reads scannable metrics from the
`DerivedMetrics` table (`DerivedMetricRow`). Therefore the chosen ratios are persisted as
**derived-metric observations** carrying provenance that marks them **vendor-precomputed**, so
the scanner can query them while the `006` derived-metrics engine remains authoritative for
engine-**calculated** metrics. Where a concept exists in both forms (e.g. a precomputed margin
vs. an engine ratio), they use **distinct metric codes** and distinct
`CalculationPolicyVersion`s so neither overwrites the other.

## Curated ratio mapping (Phase 1)

Verified `FinancialRatioItems.Id` → canonical `MetricCode` (unit in parentheses):

| CodalDB ratio (`Id`) | `MetricCode` | Unit |
|---|---|---|
| Current ratio (65) | `CURRENT_RATIO` | Ratio |
| Quick Ratio (8191) | `QUICK_RATIO` | Ratio |
| Net working capital (4069) | `NET_WORKING_CAPITAL` | Amount |
| Comprehensive liquidity index (6901) | `COMPREHENSIVE_LIQUIDITY_INDEX` | Ratio |
| Ratio of current assets to total assets (4071) | `CURRENT_ASSETS_TO_TOTAL_ASSETS` | Ratio |
| Current debt to total assets (41006) | `CURRENT_DEBT_TO_TOTAL_ASSETS` | Ratio |
| Asset turnover (4100) | `ASSET_TURNOVER` | Ratio |
| Tangible fixed assets turnover - Expensive (41067) | `TANGIBLE_FIXED_ASSETS_TURNOVER` | Ratio |
| Operating assets Ratio (20706) | `OPERATING_ASSETS_RATIO` | Ratio |
| Average collection period (4106) | `AVERAGE_COLLECTION_PERIOD` | Days |
| Return on assets (4136) | `RETURN_ON_ASSETS` | Percentage |
| Return on equity (4138) | `RETURN_ON_EQUITY` | Percentage |
| Return on investment (4139) | `RETURN_ON_INVESTMENT` | Percentage |
| Net return on working capital (4140) | `NET_RETURN_ON_WORKING_CAPITAL` | Percentage |
| Net profit margin (4135) | `NET_PROFIT_MARGIN` | Percentage |
| Debt to equity ratio (4117) | `DEBT_TO_EQUITY` | Ratio |

> `Current ratio` exists under two catalog ids (65 and 41066). The mapping uses the id with the
> better row coverage, resolved during implementation; the unused duplicate is documented.

## Acceptance Criteria

- A new `ProviderDataset.FinancialRatios` value and an `IFinancialRatioProvider` Application
  interface (`FetchFinancialRatiosAsync(externalCompanyId)`) are added; `CodalDbDataProviderClient`
  implements it by querying `FinancialRatios` (joined to `FinancialRatioItems`) for the mapped
  `ItemID`s of a company and returning a `ProviderRawPayload` under
  `ProviderDataset.FinancialRatios`.
- A `CodalDbRatioNormalizer` (`ProviderName = "CodalDb"`, `Dataset = FinancialRatios`):
  - Selects the canonical variant per `(CompanyId, PeriodEnd, PeriodType, ItemID)` using the
    same `CodalDbStatementSelectionPolicy` rules (audited → latest representment →
    consolidated/parent).
  - Persists each value as a `DerivedMetricRow` for the company's symbol with:
    `MetricCode` = the mapped code, `Value` = `ItemValue`, `Unit` = the mapped unit,
    `MetricVersion` = the metric's definition version, `CalculationPolicyVersion` =
    `"codal-ratio-source-v1"`, period from the ratio's period columns, and
    `SourceEvidenceJson` marking the value **vendor-precomputed (CodalDb, ratio item id N)**.
  - Is idempotent on the `DerivedMetricRow` unique key
    `(SymbolId, MetricCode, MetricVersion, CalculationPolicyVersion, PeriodEnd)`.
- Each mapped ratio has a governed `FinancialMetricDefinition` + bilingual `MetricAlias`es in the
  semantic catalog (`015`) so the parser resolves user terms (English + Persian) and the scanner
  exposes them as columns/conditions.
- The scanner can filter and rank by any mapped ratio with **no scanner-engine code changes**
  (it already queries `DerivedMetrics`); explanations cite `CalculationPolicyVersion =
  codal-ratio-source-v1` and the vendor source so users see the value was provider-supplied, not
  platform-calculated.
- These vendor-precomputed values never overwrite engine-calculated metrics: distinct
  `MetricCode`/policy version guarantees separation (e.g. a future engine `CURRENT_RATIO_V1`
  would be a different code/policy).

## Technical Notes

- **Architectural rationale (Clean Architecture / DDD):** a vendor-precomputed ratio is a
  *source observation*, not a domain calculation. Persisting it as a `DerivedMetricRow` with a
  source-marked policy version keeps the scanner read-path uniform while the `006` engine stays
  the single owner of *calculated* metrics. Do not route these through `IFinancialMetricCalculator`.
- Units: store `AVERAGE_COLLECTION_PERIOD` as Days, margins/returns as Percentage, the rest as
  Ratio. Confirm whether CodalDB stores percentages as fractions (0.18) or percents (18) by
  sampling, and normalize to the platform's `Percentage` convention.
- The `FinancialRatios` table is large (5.1M rows). The `FetchFinancialRatiosAsync` query MUST be
  filtered by `CompanyId` and the mapped `ItemID` set, and paged/streamed — never a full-table
  read (see Release-It! bounded-result guidance).

## Dependencies

- `021`, `022`, `023` (selection policy reuse), `015` (metric definitions/aliases),
  `008`/`009` (scanner + explanation read `DerivedMetrics`).
