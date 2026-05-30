# Tasks

## Infrastructure — Normalizer

- [ ] Add `…/Ingestion/CodalDb/CodalDbMonthlyReportNormalizer.cs`
      (`ProviderName = "CodalDb"`, `Dataset = MonthlyProductionSales`):
      - Deserialize the monthly-activity payload (headers + per-product amounts).
      - Convert Jalali `(Year, Month)` → Gregorian month start/end via the shared
        Jalali↔Gregorian resolver (reuse the CyclicalWaves resolution; do not duplicate).
      - Upsert `NormalizedMonthlyReportRow` keyed `(ProviderName, ExternalReportId = Id)`.
      - Upsert `NormalizedMonthlyReportLineItemRow` per product: `ProductCode = ProductId`,
        `ProductionQuantity = ProductProduceAmount`, `SalesQuantity = ProductSaleAmount`,
        `SalesAmount = ProductSaleValue`.
      - Retain `ProductTitle`/`ProductSaleRate`/`ProductUnit` as evidence (note deferral if no
        column exists).
      - Idempotent re-run; publish `DerivedMetricRecalculationRequested`.
- [ ] Register `CodalDbMonthlyReportNormalizer` as `IFinancialPayloadNormalizer`.
- [ ] Verify `MonthlySalesMetricInputSource` sums `SalesAmount` across products correctly for
      Codal multi-product months (provider-agnostic).

## Tests

- [ ] `CodalDbMonthlyReportNormalizerTests` (unit, ~6 tests, EF in-memory): header + per-product
      line items created; Jalali month → correct Gregorian window; multi-product month sums to
      expected `MONTHLY_SALES`; zero-activity month retained; idempotent re-run;
      `DerivedMetricRecalculationRequested` published.
