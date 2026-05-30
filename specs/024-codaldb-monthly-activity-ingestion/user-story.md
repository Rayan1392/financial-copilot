# User Story — CodalDB Monthly Activity Ingestion

> Depends on `021`, `022`. Schema reference:
> [docs/codaldb-datasource.md](../../docs/codaldb-datasource.md).

## Story

As a scanner user,
I want CodalDB monthly production/sales activity normalized into the platform's
`NormalizedMonthlyReportRow` / `NormalizedMonthlyReportLineItemRow` tables,
so that monthly-sales metrics and their growth (`MONTHLY_SALES`, `MONTHLY_SALES_GROWTH_*`) are
available for Codal-covered companies, consistently with the CyclicalWaves monthly model.

## Context

`MonthlyActivity` (3,903 rows) is a per-company, per-month header (`Month`, `Year`,
`FiscalYearEnd` — all Jalali). Its children `MonthlyActivityAmounts` (6,342 rows) hold
per-product `ProductProduceAmount`, `ProductSaleAmount`, `ProductSaleRate`, `ProductSaleValue`.
The normalized model has `NormalizedMonthlyReportRow` (period window + provenance) and
`NormalizedMonthlyReportLineItemRow` (`ProductCode`, `ProductionQuantity`, `SalesQuantity`,
`SalesAmount`).

## Acceptance Criteria

- A `CodalDbMonthlyReportNormalizer` (`ProviderName = "CodalDb"`,
  `Dataset = MonthlyProductionSales`) deserializes the monthly-activity payload for a company and
  upserts, per `MonthlyActivity` row, one `NormalizedMonthlyReportRow`:
  - `ExternalReportId` = `MonthlyActivity.Id`,
  - `PeriodStart`/`PeriodEnd` = the first/last day of the activity month, converted from the
    Jalali `(Year, Month)` to Gregorian `DateOnly` using the project's existing Jalali↔Gregorian
    resolution (reused from the CyclicalWaves work — do not re-implement).
- For each `MonthlyActivityAmounts` row, one `NormalizedMonthlyReportLineItemRow`:
  - `ProductCode` = `ProductId` (string), `ProductionQuantity` = `ProductProduceAmount`,
    `SalesQuantity` = `ProductSaleAmount`, `SalesAmount` = `ProductSaleValue`.
  - `ProductTitle`/`ProductSaleRate`/`ProductUnit` are retained as evidence where the normalized
    model has no column (do not silently drop; note the deferral).
- The company-level `MONTHLY_SALES` source metric (consumed by the existing
  `MonthlySalesMetricInputSource`) is satisfied by summing `SalesAmount` across products for the
  month, matching the existing CyclicalWaves behavior, so `MONTHLY_SALES_GROWTH_YOY` /
  `MONTHLY_SALES_GROWTH_MOM` recompute for Codal companies with no engine changes.
- Upserts are idempotent on `(ProviderName, ExternalReportId)` and
  `(MonthlyReportId, ProductCode)`.
- After successful normalization, `DerivedMetricRecalculationRequested` is published.

## Technical Notes

- Confirm `MonthlySalesMetricInputSource` aggregates `SalesAmount` provider-agnostically; if it
  assumes a single product/line, ensure summing across products is correct for Codal multi-
  product months.
- Activity months are Jalali; convert via the shared resolver. A month with all-zero amounts is
  valid data (companies report zero-activity months) — keep it, do not filter.

## Dependencies

- `021`, `022`.
- `005` normalized monthly rows + pipeline; `006` monthly-sales growth calculators.
- Reuses the Jalali/fiscal-period resolution from `020-cyclicalwaves-data-provider`.
