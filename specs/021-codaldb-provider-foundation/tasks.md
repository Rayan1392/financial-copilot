# Tasks

## Infrastructure — Options & Connection

- [ ] Add `CodalDbProviderOptions` with `SectionName = "CodalDb"`, `ProviderName` (default
      `"CodalDb"`), `ConnectionString`, `CommandTimeoutSeconds` (default 30),
      `MaxReadParallelism` (default 4). No secrets hardcoded.
- [ ] Add `CodalDbConnectionFactory` that creates read-only `Microsoft.Data.SqlClient`
      `SqlConnection` instances from `CodalDbProviderOptions.ConnectionString`, applying the
      configured command timeout. Expose an `OpenAsync(CancellationToken)` helper.
- [ ] Add `CodalDbSqlResilience` (small Polly or explicit policy): bounded transient-error
      retry with backoff + per-command timeout; classify transient `SqlException` numbers vs.
      permanent failures; map terminal failures to `FinancialProviderException`.

## Infrastructure — Provider Client (SQL → ProviderRawPayload)

- [ ] Add `…/Providers/CodalDb/CodalDbQueryModels.cs` — internal record types for projected
      query rows (e.g. `CodalCompanyRow`, `CodalStatementRow`, `CodalMonthlyActivityRow`) used
      for deterministic JSON serialization.
- [ ] Add `…/Providers/CodalDb/CodalDbPayloadSerializer.cs` — serializes a projected result set
      to a **canonical** JSON string (stable column order, rows ordered by key) and computes the
      SHA-256 checksum, so unchanged data produces an identical checksum.
- [ ] Add `…/Providers/CodalDb/CodalDbDataProviderClient.cs` implementing `ISymbolDataProvider`,
      `IFinancialStatementProvider`, `IMonthlyProductionSalesProvider`,
      `IFinancialDataProviderHealthService`:
      - `FetchSymbolsAsync` → query `Companies` (+ derived `Symbols`) → payload under
        `ProviderDataset.Symbols`, `Endpoint = "codaldb://companies"`.
      - `FetchFinancialStatementsAsync(externalCompanyId)` → query `Statements` (+ income &
        balance amounts for that company) → payload under
        `ProviderDataset.FinancialStatements`, `Endpoint = "codaldb://statements/{CoID}"`.
      - `FetchMonthlyReportsAsync(externalCompanyId)` → query `MonthlyActivity` (+ amounts) →
        payload under `ProviderDataset.MonthlyProductionSales`,
        `Endpoint = "codaldb://monthly-activity/{CoID}"`.
      - `CheckAsync` → `SELECT 1` + `SELECT COUNT(*) FROM Companies`; return health status with
        detail; map failures to `Unavailable`.
      - All payloads set `ProviderName = options.ProviderName`.
- [ ] Filter out soft-deleted source rows in every statement query
      (`Statements.isDeleted = 0 OR isDeleted IS NULL`).

## Infrastructure — DI Registration (coexistence)

- [ ] Register `CodalDbProviderOptions` from configuration section `"CodalDb"`.
- [ ] Register `CodalDbConnectionFactory` (singleton) and `CodalDbSqlResilience`.
- [ ] Register `CodalDbDataProviderClient` (scoped) and expose it as a **named/keyed** or
      provider-routed `ISymbolDataProvider` / `IFinancialStatementProvider` /
      `IMonthlyProductionSalesProvider` / `IFinancialDataProviderHealthService` so it coexists
      with the CyclicalWaves registrations (see `022` for the provider-routing approach).
- [ ] Do **not** register `CodalDbDataProviderClient` for `IMarketDataProvider`.

## Tests

- [ ] Add `CodalDbPayloadSerializerTests` (unit, ~4 tests): identical input → identical
      checksum; row/column reordering does not change the checksum; different data → different
      checksum.
- [ ] Add `CodalDbDataProviderClientTests` (unit, ~5 tests) using a faked
      `CodalDbConnectionFactory`/query layer (no live SQL): each `Fetch*Async` returns a payload
      with the correct `ProviderName`, `Dataset`, `Endpoint`, and `ExternalReference`;
      `CheckAsync` returns `Unavailable` and wraps `SqlException` in `FinancialProviderException`
      on connection failure.
- [ ] (Optional, gated) Add an integration smoke test behind a `CODALDB_INTEGRATION` env flag
      that runs `CheckAsync` against a real CodalDB instance; skipped by default in CI.
