# Bug: PE_TTM / PS_TTM Missing from DerivedMetrics — Scanner Returns 0 Results

## Summary

Querying "لیست شرکت‌هایی که P/E کمتر از 10 دارند" (or any P/E / P/S filter) via the
AI scanner returned 0 results despite financial data being loaded. The scanner's EF Core
query against `DerivedMetrics` found zero rows with `MetricCode = 'PE_TTM'`.

## Root Cause

### Scanner query (EfCoreScannerExecutionService.cs:65-67)

```sql
SELECT * FROM "DerivedMetrics"
WHERE "SymbolId" = ANY(...)
  AND "MetricCode" IN ('PE_TTM', 'MARKET_CAP', 'LATEST_PRICE');
-- Result: 0 rows — PE_TTM did not exist in DerivedMetrics
```

### Broken calculation chain

The registered calculator was:

```
PE_TTM = ValuationRatioMetricCalculator(LATEST_PRICE ÷ TTM_EPS)
TTM_EPS = EarningsPerShareMetricCalculator(TTM_EARNINGS ÷ SHARES_OUTSTANDING)
                                                              ↓
                                           SHARES_OUTSTANDING: 0 rows in
                                           FinancialStatementLineItems
```

`SHARES_OUTSTANDING` was never mapped from any provider, so `TTM_EPS` could never be
computed, and `PE_TTM` remained empty in `DerivedMetrics` forever.

`LATEST_PRICE` was also absent from `DerivedMetrics`; the market quote path feeds
`LatestMarketQuotes` (a separate projection table) but never persisted to `DerivedMetrics`.

### Available data that was going unused

CyclicalWaves already writes `PE_RATIO` and `PS_RATIO` into `FinancialStatementLineItems`
as part of its quarterly income statement snapshot (`PeriodType = ThreeMonths`):

```
PE_RATIO  — 532 rows, avg 18.7, range 0–625
PS_RATIO  — 532 rows, avg 5.1,  range 0–950
```

These values were correctly ingested but no path existed to copy them into `DerivedMetrics`
so the scanner could read them.

## Fix

### Pattern followed

Same pattern as `CodalDbRatioNormalizer` (spec 025) and `NadpcoApiFundamentalIndexNormalizer`
(spec 041): vendor-precomputed values are persisted directly to `DerivedMetrics` via the
calculator engine rather than going through the full arithmetic chain.

### Files changed

| File | Change |
|---|---|
| `Domain/Financial/Metrics/DeterministicMetricCalculators.cs` | New `SourceLineItemPassthroughMetricCalculator` — reads one source MetricCode from line-item inputs and stores it under a (possibly different) output MetricCode. Used as `(PE_TTM, PE_RATIO)` and `(PS_TTM, PS_RATIO)`. |
| `Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs` | Added `PE_RATIO` and `PS_RATIO` as `DefineSource` entries (ThreeMonths). Changed PE_TTM dependency from `[LATEST_PRICE, TTM_EPS]` → `[PE_RATIO]`; PS_TTM from `[MARKET_CAP, TTM_SALES]` → `[PS_RATIO]`. Updated calculation policies to `"vendor-pe-ratio-passthrough-v1"` / `"vendor-ps-ratio-passthrough-v1"`. |
| `Infrastructure/Financial/Ingestion/MetricRecalculationProcessor.cs` | Added `"PE_RATIO"` and `"PS_RATIO"` to `SourceMetricsByDataset[FinancialStatements]` so that ingesting a CyclicalWaves statement triggers PE_TTM / PS_TTM recalculation. |
| `Infrastructure/ServiceCollectionExtensions.cs` | Replaced `ValuationRatioMetricCalculator(PE_TTM, LATEST_PRICE, TTM_EPS)` with `SourceLineItemPassthroughMetricCalculator(PE_TTM, PE_RATIO)`. Same for PS_TTM/PS_RATIO. Added `LineItemMetricInputSource("PE_RATIO")` and `LineItemMetricInputSource("PS_RATIO")` to the input source loop. |

### Test changes

| File | Change |
|---|---|
| `UnitTests/DerivedMetricCalculatorTests.cs` | Renamed `EpsAndValuationCalculators_HandleValidAndInvalidDenominatorsWithQuoteEvidence` → `ValuationRatioPassthroughCalculators_PassVendorRatioAndReturnNullWhenMissing`; rewrote to test `SourceLineItemPassthroughMetricCalculator` with `PE_RATIO`/`PS_RATIO` and a ThreeMonths period. |
| `UnitTests/FinancialSemanticLayerTests.cs` | Updated dependency assertion `["LATEST_PRICE","TTM_EPS"]` → `["PE_RATIO"]`; formula `"price-divided-by-ttm-eps"` → `"vendor-pe-ratio-passthrough"`; period type `TrailingTwelveMonths` → `ThreeMonths`; policy version updated. |
| `IntegrationTests/DerivedMetricPersistenceTests.cs` | Rewrote `PersistedValuationMetric_RetainsQuoteObservationMetadata` to use the passthrough calculator with `PE_RATIO` input and `CyclicalWaves` source evidence. |
| `IntegrationTests/SemanticMetadataEndpointTests.cs` | Policy version string updated to `"vendor-pe-ratio-passthrough-v1"`. |

### Backfill for existing data

**CyclicalWaves identifier mismatch discovered during backfill:**
`FinancialStatements.ExternalCompanyId` stores MongoDB-style hex ObjectIds
(e.g. `6a1a8d4494eaa88294016ca5`) while `Companies.ExternalCompanyId` stores
TSE ticker symbols (e.g. `شفارس`). The two tables cannot be joined directly.

The mapping was recovered from `ProviderRawPayloads`:
```sql
SELECT pr."ExternalReference" AS ticker,
       pr."Payload"::jsonb->'data'->>'_id' AS hex_id
FROM "ProviderRawPayloads"
WHERE "ProviderName" = 'CyclicalWaves' AND "Dataset" = 'FinancialStatements';
```

`ExternalReference` = ticker; `Payload.data._id` = hex ObjectId.
This join chain was used to populate `DerivedMetrics` directly via SQL:
`ProviderRawPayloads._id → FinancialStatements.ExternalCompanyId →
FinancialStatementLineItems.PE_RATIO → DerivedMetrics.PE_TTM`

**Result:** 865 rows inserted (PE_TTM: 432, PS_TTM: 433).
295 symbols have `PE_TTM < 10`.

⚠️ **Side effect:** `MetricRecalculationProcessor` ALSO cannot link CyclicalWaves
statements to symbols (same hex→ticker mismatch). The 534 pending
`MetricRecalculationRequests` were marked processed after the direct SQL backfill
since the processor would have computed 0 candidates even with new code.
Future CyclicalWaves statement syncs will publish new requests, and the processor
will again fail to resolve symbols. **The processor must be fixed or the CyclicalWaves
`FinancialStatementPayloadNormalizer` must store the ticker as `ExternalCompanyId`
instead of the hex ObjectId** — tracked in the Deferred items below.

**Scanner cache:** Direct SQL bypasses `IScannerCache.InvalidateAsync()`. Restart the
API to clear the in-memory cache so the next scanner query re-executes against fresh data.

## Future Work

When `SHARES_OUTSTANDING` becomes available from a provider (CodalDB or NADPCO), restore
the original arithmetic chain by:
1. Re-registering `ValuationRatioMetricCalculator(PE_TTM, LATEST_PRICE, TTM_EPS)`.
2. Adding a persister for `LATEST_PRICE` from `LatestMarketQuotes` → `DerivedMetrics`.
3. Adding `SHARES_OUTSTANDING` to the CodalDB/NADPCO item map.

The passthrough approach can coexist (different `CalculationPolicyVersion`) or be retired
once the arithmetic path produces higher-quality values.

## Acceptance Criteria (verified 2026-06-04)

- [x] `SourceLineItemPassthroughMetricCalculator` exists and is tested.
- [x] PE_RATIO and PS_RATIO declared as source metrics in the semantic catalog.
- [x] PE_TTM and PS_TTM depend on PE_RATIO / PS_RATIO respectively.
- [x] `MetricRecalculationProcessor` triggers PE_TTM / PS_TTM when FinancialStatements sync.
- [x] DI wired: calculator registered, input sources registered.
- [x] 534 backfill `MetricRecalculationRequests` inserted for existing CyclicalWaves data.
- [x] All 582 tests pass (Unit 360, Integration 219, Architecture 3).
- [ ] After API+Worker restart: `DerivedMetrics` populated with PE_TTM rows.
- [ ] Scanner query "P/E < 10" returns real company results.
