# Bug: Provider Sales Data Semantics and Unit Normalization Are Mixed

## Summary

Noavaran Amin monthly activity data and CyclicalWaves sales metrics currently risk being treated as the same semantic shape and unit. They are not equivalent:

- Noavaran Amin monthly activity is raw product/service line-item data. Monetary line-item values are source-unit million Rials and require ingestion-time aggregation into lookup-ready facts.
- CyclicalWaves sales metrics are provider-precomputed company-level facts. Monetary values are source-unit Rials and must be persisted as-is without recalculation or million-Rial conversion.

This ambiguity can lead to wrong monthly sales answers, double conversion, and incorrect recomputation of provider-precomputed facts.

## Required Semantics

### Noavaran Amin Monthly Activity

- Source shape: raw product/service line items.
- Source unit: million Rials for monetary sales values such as product sale value and service sales value.
- Required calculation: sum relevant line-item sales values per `ExternalCompanyId`, reporting period, provider, and `OutputType`.
- Canonical storage: normalized monetary values according to the platform canonical monetary unit policy.
- Timing: aggregation and unit normalization happen during ingestion/recalculation.
- Query path: AI and symbol lookup must only read persisted facts. They must not aggregate `MonthlyReportLineItems`.
- Required output types:
  - `OutputType = 0`: single-month sales.
  - `OutputType = 1`: fiscal-year-to-date sales.
  - `OutputType = 4`: fiscal-year-to-previous-month sales.
- Same-month prior fiscal-year comparison is resolved from persisted `OutputType = 0` monthly aggregates for the same company and same fiscal/Shamsi month one year earlier.

### CyclicalWaves

- Source shape: provider-precomputed company-level metrics.
- Source unit: Rials for monetary sales fields.
- Required calculation: none for precomputed fields.
- Canonical storage: persist values as-is with provider provenance and a passthrough/source policy.
- Forbidden behavior:
  - Do not recalculate CyclicalWaves sales metrics from Noavaran line items.
  - Do not apply Noavaran million-Rial conversion to CyclicalWaves values.
  - Do not recompute provider-precomputed PE/PS/sales averages.
- Required persisted facts include mapped equivalents for:
  - last-month sale
  - penultimate-month sale
  - last-year same-month sale
  - average 12-month sale
  - last-quarter sale
  - last-year same-quarter sale
  - PE and PS ratios

## Acceptance Criteria

- Related specs explicitly document raw-vs-precomputed semantics, source units, canonical unit behavior, calculation policy, storage target, and query-time restrictions.
- Noavaran monthly sales aggregates are normalized from million Rials during ingestion/recalculation.
- CyclicalWaves precomputed sales values are persisted as-is in Rials.
- CyclicalWaves PE/PS values are persisted as unitless ratios without transformation.
- Symbol lookup for monthly sales reads persisted facts only.
- Regression tests cover Noavaran aggregation/unit normalization, CyclicalWaves passthrough/no conversion, and composite monthly sales lookup behavior.

