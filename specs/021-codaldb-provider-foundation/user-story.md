# User Story — CodalDB Provider Foundation

> Provider source: **CodalDB** — an MS SQL Server database sourced from **Noavaran Amin Data
> Processing Company** (data vendor), holding Tehran Stock Exchange / Codal financial-disclosure
> data. Full schema reference: [docs/codaldb-datasource.md](../../docs/codaldb-datasource.md).

## Story

As a backend service,
I want CodalDB read access exposed behind the existing financial-provider interfaces as a new
provider that **coexists** with CyclicalWaves,
so that the ingestion pipeline can pull Codal companies, statements, monthly activity, and
precomputed ratios into the platform's normalized PostgreSQL tables without any business code
knowing it is reading from SQL Server.

## Context

CodalDB is an **existing, queryable SQL Server source**, not an HTTP/JSON API. The existing
provider abstraction (`004`) and ingestion pipeline (`005`) are built around providers that
return a `ProviderRawPayload` (a JSON string + SHA-256 checksum) which an
`IFinancialPayloadNormalizer` then maps into `Normalized*Row` tables. This foundation story
makes CodalDB fit that contract: a SQL gateway queries CodalDB and **serializes the queried
rows into a JSON `ProviderRawPayload`**, so the rest of the pipeline (raw-payload audit,
checksum dedup, normalizer selection, derived-metric trigger) is reused unchanged.

Stories `022`–`026` build on this foundation (company/symbol sync, statement ingestion, monthly
activity, precomputed ratios, derived growth metrics).

## Acceptance Criteria

- A new provider name constant `"CodalDb"` identifies this provider throughout raw payloads,
  normalizers, and sync runs. It **coexists** with the existing `"CyclicalWaves"` and
  `"ConfiguredFinancialProvider"` providers; the `FinancialDataSyncProcessor` continues to
  select normalizers by the `(ProviderName, ProviderDataset)` pair (no replacement of existing
  registrations).
- CodalDB connection settings are read from a configuration section `CodalDb` (connection
  string, command timeout, max-degree-of-parallelism for batched reads) and **never hardcoded**;
  the connection string lives in configuration/secrets exactly like other provider credentials.
- A `CodalDbConnectionFactory` (or equivalent) opens **read-only** connections to CodalDB. All
  queries use parameterized SQL and an explicit command timeout; no write/DDL is ever issued
  against CodalDB.
- A `CodalDbDataProviderClient` implements `ISymbolDataProvider`, `IFinancialStatementProvider`,
  `IMonthlyProductionSalesProvider`, and `IFinancialDataProviderHealthService`:
  - Each `Fetch*Async` queries CodalDB, projects the rows into provider-local DTO records,
    serializes them to JSON, computes a SHA-256 checksum, and returns a `ProviderRawPayload`
    with `ProviderName = "CodalDb"`, the correct `ProviderDataset`, a synthetic `Endpoint`
    string identifying the query (e.g. `codaldb://companies`), and the `ExternalReference`.
  - `CheckAsync` runs a cheap `SELECT 1` (and a lightweight catalog count) against CodalDB and
    returns `ProviderHealthStatus.Healthy` / `Degraded` / `Unavailable` with detail.
- The "raw payload" for a SQL source is the **JSON serialization of the queried result rows**;
  idempotent checksum deduplication via the existing `IProviderRawPayloadStore` prevents
  re-normalizing an unchanged query result. The checksum is computed over a **stable,
  deterministic serialization** (ordered rows/columns) so identical data yields identical
  checksums across runs.
- CodalDB reads are wrapped in resilience behavior appropriate to a database dependency:
  explicit command timeout, bounded transient-error retry with backoff, and a fast-fail path
  when CodalDB is unreachable. Failures are logged and mapped to the existing
  `FinancialProviderException` internal error type (a SQL transport error must not surface as a
  raw `SqlException` to the Application layer).
- `CodalDbDataProviderClient` does **not** implement `IMarketDataProvider`; CodalDB is not used
  for real-time price quotes. The existing market-quote provider registration is unchanged.
- The provider is registered in the Infrastructure composition root alongside the existing
  providers; because the platform now has more than one financial provider, provider selection
  for ingestion is driven by the `DataSyncRequest.ProviderName`-equivalent routing established
  in `022` rather than a single hardcoded provider.

## Technical Notes

- CodalDB column/relationship facts (table list, keys, `PeriodType` semantics, dual Jalali +
  Gregorian dates, `Unit = 'N/A'` scale caveat, audited/consolidated/restated flags, company
  linkage columns) are documented in
  [docs/codaldb-datasource.md](../../docs/codaldb-datasource.md). Implementers must read it
  before writing queries.
- Mirror the CyclicalWaves provider file layout under a new `…/Providers/CodalDb/` folder. The
  CodalDB equivalent of `CyclicalWavesAuthHandler`/`CyclicalWavesTokenCache` is **not needed**
  (no auth token); instead add `CodalDbConnectionFactory` and `CodalDbProviderOptions`.
- Use `Microsoft.Data.SqlClient` for the SQL Server connection. Connections must be opened with
  `ApplicationIntent=ReadOnly` semantics where supported and the account used should have
  read-only rights; document this operational expectation.
- Reuse `FinancialProviderResilienceHandler` only conceptually — it is an `HttpMessageHandler`
  and does not apply to SQL. Implement an equivalent small resilience wrapper (Polly policy or
  explicit retry/timeout) for SQL command execution.
- Scheduling/triggering of CodalDB syncs is out of scope here; it is handled by the existing
  `012-admin-data-operations` admin endpoints (which publish `DataSyncRequest`) plus the routing
  added in `022`.

## Dependencies

- `004-third-party-data-provider-abstraction` (provider interfaces, `IProviderRawPayloadStore`,
  `ProviderDataset`, `FinancialProviderException`).
- `005-data-ingestion-and-normalization` (`FinancialDataSyncProcessor`, normalizer selection,
  raw-payload-before-normalization flow).
- Reference implementation: `020-cyclicalwaves-data-provider`.
