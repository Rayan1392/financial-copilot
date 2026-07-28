# Tasks

## Infrastructure — Authentication

- [ ] Add `CyclicalWavesProviderOptions` with `SectionName = "CyclicalWaves"`, `ProviderName`,
      `BaseAddress`, `UserName`, `Password`, `TimeoutSeconds`, `RetryCount`,
      `CircuitBreakSeconds`, `CircuitFailureThreshold`.
- [ ] Add `CyclicalWavesTokenCache` (singleton) — thread-safe JWT storage with expiry tracking
      and a `TryGetToken` / `SetToken` / `Invalidate` API.
- [ ] Add `CyclicalWavesAuthHandler` (DelegatingHandler) — on each request: inject Bearer
      header from cache; on cache miss: POST `/api/auth/login`, cache result, retry; on 401
      from API: invalidate cache, re-login once, throw `FinancialProviderException(Unauthorized)`
      on second failure.

## Infrastructure — Provider Client

- [ ] Add `CyclicalWavesPayloadModels.cs` — internal records for JSON deserialization:
      `CyclicalWavesAuthResponse`, `CyclicalWavesTickerDetailResponse` (all fields with
      `[JsonPropertyName]` snake_case attributes).
- [ ] Add `CyclicalWavesDataProviderClient` implementing `ISymbolDataProvider`,
      `IFinancialStatementProvider`, `IMonthlyProductionSalesProvider`,
      `IFinancialDataProviderHealthService`:
      - `FetchSymbolsAsync` → `GET /custom-filtering/tickers` → store raw array payload.
      - `FetchFinancialStatementsAsync(ticker)` → `GET /custom-filtering/ticker/{PercentEncode(ticker)}`
        → store the combined raw object payload.
      - `FetchMonthlyReportsAsync(ticker)` must not force a second remote call during full sync
        when the financial-statement payload for the same ticker is already available. The shared
        payload is routed to the monthly normalizer through the ingestion processor.
      - `CheckAsync` → attempt login; return `Healthy` or `Unavailable`.
- [ ] Apply `FinancialProviderResilienceHandler` (existing) to the CyclicalWaves typed
      `HttpClient` for timeout, retry, and circuit-breaker behavior.

## Application — Interface Extension

- [ ] Add `string ProviderName { get; }` to `IFinancialPayloadNormalizer`
      in `FinancialProviderContracts.cs`.
- [ ] Update existing `SymbolPayloadNormalizer`, `FinancialStatementPayloadNormalizer`,
      `MonthlyReportPayloadNormalizer` to implement `ProviderName` (return
      `"ConfiguredFinancialProvider"`).
- [ ] Update `FinancialDataSyncProcessor` normalizer selection from `Dataset` alone to
      `(ProviderName, Dataset)` pair using `payload.ProviderName`.

## Infrastructure — Fiscal Period Resolver

- [ ] Add `CyclicalWavesRelativePeriodResolver` (static helper) — converts relative quarter
      and month labels to `(DateOnly Start, DateOnly End)` using Iranian fiscal-year calendar
      boundaries derived from a given `DateTimeOffset asOf`. Quarters: Q1 Mar 21–Jun 21,
      Q2 Jun 22–Sep 22, Q3 Sep 23–Dec 22, Q4 Dec 23–Mar 20 (Gregorian approximation).

## Infrastructure — Normalizers

- [ ] Add `CyclicalWavesSymbolNormalizer` (`ProviderName = "CyclicalWaves"`,
      `Dataset = Symbols`):
      - Deserialize `string[]` ticker list.
      - Upsert `NormalizedCompanyRow` (name = Persian ticker) + `NormalizedSymbolRow`
        (SymbolCode = Persian ticker, provisional; enriched by financial-statement normalizer).
- [ ] Add `CyclicalWavesFinancialStatementNormalizer` (`Dataset = FinancialStatements`):
      - Deserialize `CyclicalWavesTickerDetailResponse`.
      - Enrich `NormalizedSymbolRow.SymbolCode` with `enticker`; set `ExternalSymbolId` = `_id`.
      - For each of Q-0, Q-1, Q-4: upsert `NormalizedFinancialStatementRow` (IncomeStatement)
        + line items for REVENUE, NET_PROFIT, GROSS_PROFIT, OPERATING_PROFIT,
        NET_PROFIT_MARGIN, GROSS_PROFIT_MARGIN, OPERATING_PROFIT_MARGIN.
      - Add PE_RATIO and PS_RATIO line items on Q-0 row only.
      - Use `CyclicalWavesRelativePeriodResolver` for period dates; attach `StaleData` warning.
- [ ] Add `CyclicalWavesMonthlyReportNormalizer` (`Dataset = MonthlyProductionSales`):
      - Deserialize `CyclicalWavesTickerDetailResponse`.
      - For each of M-0, M-1, M-12: upsert `NormalizedMonthlyReportRow` + REVENUE line item.
      - Use `CyclicalWavesRelativePeriodResolver` for month dates; attach `StaleData` warning.

## Infrastructure — DI Registration

- [ ] Register `CyclicalWavesProviderOptions` from configuration section `"CyclicalWaves"`.
- [ ] Register `CyclicalWavesTokenCache` as singleton.
- [ ] Register `CyclicalWavesAuthHandler` as transient.
- [ ] Register typed `HttpClient<CyclicalWavesDataProviderClient>` with
      `CyclicalWavesAuthHandler` + `FinancialProviderResilienceHandler` pipeline.
- [ ] Replace `MockFinancialDataProvider` registrations for `ISymbolDataProvider`,
      `IFinancialStatementProvider`, `IMonthlyProductionSalesProvider`,
      `IFinancialDataProviderHealthService` with `CyclicalWavesDataProviderClient`.
- [ ] Keep `MockFinancialDataProvider` for `IMarketDataProvider` (CyclicalWaves has no
      real-time price data).
- [ ] Register `CyclicalWavesSymbolNormalizer`, `CyclicalWavesFinancialStatementNormalizer`,
      `CyclicalWavesMonthlyReportNormalizer` as `IFinancialPayloadNormalizer`.

## Tests

- [ ] Add `CyclicalWavesNormalizerTests` (unit, ~10 tests) using EF Core in-memory:
      - Symbol normalizer parses ticker array and creates company + symbol rows.
      - Financial-statement normalizer produces 3 statement rows with correct line items
        including PE/PS on Q-0 only.
      - Monthly-report normalizer produces 3 report rows with REVENUE line item.
      - Second normalization of identical payload is idempotent (no duplicate rows).
      - `enticker` overwrites provisional `SymbolCode` set by symbol normalizer.
- [ ] Add single-fetch regression coverage for the ticker-detail full-sync path:
      - one remote `GET /custom-filtering/ticker/{ticker}` request per ticker;
      - one persisted raw payload for that ticker-detail body;
      - both `CyclicalWavesFinancialStatementNormalizer` and
        `CyclicalWavesMonthlyReportNormalizer` consume the shared payload successfully.
- [ ] Add `CyclicalWavesAuthHandlerTests` (unit, ~5 tests) using mock `HttpMessageHandler`:
      - First request calls `/auth/login`, caches token, adds Authorization header.
      - Cached token is reused without re-login.
      - 401 response triggers re-login and retry.
      - Expired token triggers re-login.
      - Login failure throws `FinancialProviderException(Unauthorized)`.
- [ ] Add `CyclicalWavesRelativePeriodResolverTests` (unit, ~6 tests):
      - Last-quarter resolves correctly for each of the four Iranian fiscal quarters.
      - Penultimate quarter crosses fiscal-year boundary correctly.
      - Monthly resolution matches expected calendar month.

## Change Request Tasks - 2026-06-05

- [x] Disable or remove CyclicalWaves company-row upserts from the symbol/ticker sync path.
- [x] Update CyclicalWaves financial-statement and monthly normalizers so they resolve company
      linkage from existing NADPCO-backed company/symbol metadata instead of creating company
      rows.
- [x] Add data-quality warnings when a CyclicalWaves ticker cannot be linked to an existing
      NADPCO-backed company/symbol row.
- [x] Add regression tests proving CyclicalWaves cannot overwrite NADPCO company catalog fields.
- [x] Add regression tests proving CyclicalWaves financial observations can still be persisted
      when linkage succeeds through NADPCO metadata.
