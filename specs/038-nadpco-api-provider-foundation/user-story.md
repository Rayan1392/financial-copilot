# NADPCO HTTP API Provider Foundation

## User Story

As a backend service, I want Noavaran Amin Data Processing Company HTTP APIs available behind
the existing financial-provider abstraction so the platform can ingest vendor data into
normalized PostgreSQL tables without coupling scanner logic to remote endpoints.

## Context

The completed `021`-`027` chain reads CodalDB through a read-only SQL Server adapter. This
story introduces a separate HTTP source named `NadpcoApi` for the attached
`https://data3.nadpco.com` APIs. It coexists with `CodalDb`, `CyclicalWaves`, and
`ConfiguredFinancialProvider`; it does not replace the completed CodalDB synchronization path.

The attached examples expose:

```http
POST /api/v2/Token
GET  /api/v3/BaseInfo/Companies
POST /api/v2/FS/BalanceSheet/Values
POST /api/v2/FS/IncomeStatement/Values
POST /api/v2/FS/CashFlow/Values
POST /api/v2/CompanyFundamentalIndex/Values
POST /api/v2/MonthlyActivity/ProductSales
POST /api/v3/MonthlyActivity/ServiceSales
```

The successful token response shape and token lifetime are not present in the attachment.
Implementation must verify them against the vendor contract before activation.

## Acceptance Criteria

1. Add a provider registration named `NadpcoApi` that coexists with all existing providers.
2. Read base URL, username, password, timeout, retry, circuit-breaker, and batching settings
   from secrets-backed configuration. Never persist raw credentials or tokens.
3. Acquire an API token through `POST /api/v2/Token` using Basic authentication, cache the first
   token obtained during each Tehran calendar day in Redis-compatible `IDistributedCache` until
   `23:59:59` Tehran time, apply Bearer authentication to data requests, and refresh only after
   Tehran-day expiry or one token-rejection response (`401`, `403`, or vendor message
   `توکن صحیح نیست`).
4. Validate the successful token payload and expiry behavior against vendor documentation or a
   controlled live smoke test before enabling scheduled reads.
5. Apply timeout, retry, bounded concurrency, circuit-breaker, error mapping, and structured
   telemetry through provider-local infrastructure.
6. Persist raw JSON responses through the existing `ProviderRawPayload` audit path before
   normalization, with deterministic checksum deduplication.
7. Map vendor failures to internal provider errors. Do not expose passwords, Basic
   credentials, Bearer tokens, or raw remote exception bodies containing secrets.
8. Add a cheap provider health check that distinguishes authentication failure, transport
   failure, and a healthy authenticated dependency.
9. Do not implement `IMarketDataProvider`; these endpoints do not provide live quotes.

## Out Of Scope

- Replacing the CodalDB SQL adapter.
- Scanner-engine changes.
- Live quote ingestion.
- Hardcoding vendor credentials.
