# User Story — CyclicalWaves Data Provider

## Story

As a scanner user,
I want Tehran Stock Exchange financial data fetched from the CyclicalWaves API and stored in
the platform's normalized tables,
so that the scanner can filter symbols by up-to-date quarterly metrics, monthly sales, and
valuation ratios without depending on a specific vendor.

## Acceptance Criteria

- The platform authenticates with CyclicalWaves using a username/password login that returns a
  JWT Bearer token (10-day expiry); the token is cached in-process and refreshed automatically
  on expiry or a 401 response.
- A symbol-sync step calls `GET /api/custom-filtering/tickers` and upserts every ticker into
  `NormalizedCompanyRow` + `NormalizedSymbolRow`, using the Persian ticker name as the company
  name and the CyclicalWaves `_id` as the external provider reference.
- A financial-snapshot sync step calls `GET /api/custom-filtering/ticker/{ticker}` (percent-
  encoded) for each known ticker and normalizes the response into three layers:
  - Three `NormalizedFinancialStatementRow` records (IncomeStatement type) — one per relative
    period: last quarter (Q-0), penultimate quarter (Q-1), and last-year same quarter (Q-4) —
    each with line items for REVENUE, NET_PROFIT, GROSS_PROFIT, OPERATING_PROFIT,
    NET_PROFIT_MARGIN, GROSS_PROFIT_MARGIN, OPERATING_PROFIT_MARGIN. PE_RATIO and PS_RATIO are
    stored on the Q-0 row only.
  - Three `NormalizedMonthlyReportRow` records — last month (M-0), penultimate month (M-1), and
    last-year same month (M-12) — each with a REVENUE line item.
  - The `enticker` field (e.g. `IRO7SHLP0001`) is stored as the canonical `SymbolCode`; the
    Persian `ticker` field is stored as the company display name.
- Because CyclicalWaves returns relative period labels ("last quarter") rather than absolute
  dates, fiscal period start/end dates are estimated from the request timestamp using the
  Iranian fiscal-year calendar (year starts ≈ 21 March Gregorian). Estimated periods are
  flagged with a `StaleData` quality warning so consumers know the dates are approximate.
- All raw JSON responses are stored in `ProviderRawPayloads` before normalization; idempotent
  checksum deduplication prevents double-processing identical payloads.
- After successful normalization, `DerivedMetricRecalculationRequested` is published so the
  derived-metric engine can compute growth metrics (QoQ, YoY revenue growth) that the scanner
  consumes.
- The CyclicalWaves client integrates with the existing ingestion pipeline:
  `DataSyncRequest` → RabbitMQ → `DataSyncConsumerWorker` → `FinancialDataSyncProcessor`
  → CyclicalWaves normalizers → derived-metric trigger.
- The normalizer selection in `FinancialDataSyncProcessor` matches on both `ProviderName` and
  `Dataset` so CyclicalWaves normalizers coexist with any future provider's normalizers without
  ambiguity.
- Concurrent ticker fetching is throttled (max 10 simultaneous HTTP calls) to avoid overloading
  the CyclicalWaves server.
- Provider credentials (username, password) are read from configuration section `CyclicalWaves`
  and never hardcoded.
- The CyclicalWaves client does NOT implement `IMarketDataProvider`; real-time price data is
  out of scope for this provider.
- The existing `MockFinancialDataProvider` continues to be used in development/test for
  `IMarketDataProvider`.

## Scanner Metrics Enabled

| MetricCode | Source field | Period |
|---|---|---|
| `REVENUE` | `last/penultimate/last_year_same_quarter_sale` | Q-0, Q-1, Q-4 |
| `NET_PROFIT` | `*_net_profit` | Q-0, Q-1, Q-4 |
| `GROSS_PROFIT` | `*_gross_profit` | Q-0, Q-1, Q-4 |
| `OPERATING_PROFIT` | `*_operating_profit` | Q-0, Q-1, Q-4 |
| `NET_PROFIT_MARGIN` | `*_net_profit_margin` | Q-0, Q-1, Q-4 |
| `GROSS_PROFIT_MARGIN` | `*_gross_profit_margin` | Q-0, Q-1, Q-4 |
| `OPERATING_PROFIT_MARGIN` | `*_operating_profit_margin` | Q-0, Q-1, Q-4 |
| `PE_RATIO` | `pe` | Q-0 only |
| `PS_RATIO` | `ps` | Q-0 only |
| `REVENUE` (monthly) | `last/penultimate/last_year_same_month_sale` | M-0, M-1, M-12 |
| `REVENUE_GROWTH_QOQ` | derived: Q-0 vs Q-1 | Derived |
| `REVENUE_GROWTH_YOY` | derived: Q-0 vs Q-4 | Derived |

## Technical Notes

- `CyclicalWavesTokenCache` is a singleton holding the cached JWT with expiry; all scoped
  `CyclicalWavesDataProviderClient` instances share it.
- `CyclicalWavesAuthHandler` is a `DelegatingHandler` that transparently handles login,
  caching, and 401-triggered refresh without exposing auth concerns to the client class.
- Fiscal period resolution is encapsulated in `CyclicalWavesRelativePeriodResolver`; Iranian
  fiscal quarter boundaries are computed in Gregorian terms (Q1 ≈ Mar 21–Jun 21,
  Q2 ≈ Jun 22–Sep 22, Q3 ≈ Sep 23–Dec 22, Q4 ≈ Dec 23–Mar 20).
- `IFinancialPayloadNormalizer` gains a `string ProviderName { get; }` property; the
  processor selects by `(ProviderName, Dataset)` pair. Existing normalizers add the property
  with the value `"ConfiguredFinancialProvider"` — no behavioral change.
- This spec does not cover a scheduling/trigger mechanism for daily sync; that is the
  responsibility of `012-admin-data-operations` admin endpoints or a future cron spec.

## Change Request - 2026-06-05

CyclicalWaves must no longer be used to create or update PostgreSQL `Companies` catalog rows.
NADPCO `/api/v3/BaseInfo/Companies` is the authoritative company catalog source.
This change request supersedes the original CyclicalWaves company-creation acceptance criteria
above wherever they conflict.

Updated acceptance constraints:

1. CyclicalWaves may continue to provide financial snapshots, monthly values, and valuation
   ratio observations where still needed.
2. CyclicalWaves symbol/ticker reads must not insert, update, or overwrite `Companies` rows.
3. Any CyclicalWaves data that requires company linkage must resolve against existing
   NADPCO-backed company/symbol metadata or emit a data-quality/linkage warning.
4. CyclicalWaves must never overwrite NADPCO company names, symbols, industry, market, ISIN,
   registration, or listing metadata.
