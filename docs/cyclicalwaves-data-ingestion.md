# CyclicalWaves Data Ingestion Runbook

This document explains how Financial Copilot receives data from the CyclicalWaves HTTP provider and
how operators should use it separately from NADPCO.

## Provider Role

`CyclicalWaves` is the default HTTP market/fundamental data provider for the original scanner MVP
path. It supplies a ticker list and per-ticker snapshots that are normalized into the same local
PostgreSQL model used by the rest of the system.

CyclicalWaves currently feeds these internal datasets:

| CyclicalWaves data | Internal dataset | Main storage |
|---|---|---|
| Ticker list | `Symbols` | `Companies`, `Symbols` |
| Per-ticker financial snapshot | `FinancialStatements` | `FinancialStatements`, `FinancialStatementLineItems` |
| Per-ticker monthly sales snapshot | `MonthlyProductionSales` | `MonthlyReports`, `MonthlyReportLineItems` |
| Vendor PE/PS fields in ticker snapshot | source line items `PE_RATIO`, `PS_RATIO` | promoted to `DerivedMetrics.PE_TTM`, `DerivedMetrics.PS_TTM` |

## Configuration

Set credentials through secrets or environment variables. Do not commit live credentials.

```powershell
$env:CyclicalWaves__BaseAddress = "https://back1.cyclicalwaves.com/api/"
$env:CyclicalWaves__UserName = "<vendor-user>"
$env:CyclicalWaves__Password = "<vendor-password>"
$env:CyclicalWaves__TimeoutSeconds = "30"
$env:CyclicalWaves__RetryCount = "2"
$env:CyclicalWaves__CircuitBreakSeconds = "60"
$env:CyclicalWaves__CircuitFailureThreshold = "5"
```

The configured provider name must remain:

```json
{
  "CyclicalWaves": {
    "ProviderName": "CyclicalWaves"
  }
}
```

## Authentication Flow

CyclicalWaves authentication uses:

```http
POST /api/auth/login
Content-Type: application/json

{
  "user_name": "<vendor-user>",
  "password": "<vendor-password>"
}
```

The provider expects `access_token` and `expires_in`, caches the token, sends bearer auth on data
requests, and retries once after a `401`.

## Remote Endpoints Used

Ticker list:

```http
GET /api/custom-filtering/tickers
```

Per-ticker snapshot:

```http
GET /api/custom-filtering/ticker/{ticker}
```

The same per-ticker payload is used by both financial-statement and monthly-sales normalizers.

## Data Flow

CyclicalWaves ingestion works through the provider-neutral sync processor:

1. A `DataSyncRequest` is created for `Symbols`, `FinancialStatements`, or
   `MonthlyProductionSales`.
2. The CyclicalWaves provider fetches raw JSON and stores it in `ProviderRawPayloads`.
3. The matching CyclicalWaves normalizer writes normalized rows.
4. The sync processor publishes `MetricRecalculationRequests`.
5. The Worker recalculates derived metrics into `DerivedMetrics`.
6. AI/scanner responses read only local persisted tables.

For PE and PS:

- CyclicalWaves source fields are `pe` and `ps`.
- The normalizer stores them as `PE_RATIO` and `PS_RATIO` line items on the latest quarter snapshot.
- The derived metric engine uses passthrough calculators to write `PE_TTM` and `PS_TTM`.
- The answer path reads `DerivedMetrics.PE_TTM`; it does not call CyclicalWaves at query time.

## Manual Usage

All endpoints require a `DataAdmin` actor.

Check provider health:

```http
GET /api/v1/admin/provider-health
```

Run the built-in CyclicalWaves full sync:

```http
POST /api/v1/admin/cyclicalwaves/full-sync
```

That full sync:

1. syncs the ticker list;
2. loads persisted CyclicalWaves tickers from `Symbols`;
3. keeps Persian tickers;
4. syncs `FinancialStatements` and `MonthlyProductionSales` per ticker.

Queue only the symbol list:

```http
POST /api/v1/admin/data-sync/symbols
Content-Type: application/json

{
  "idempotencyKey": "manual-cyclicalwaves-symbols-2026-06-07"
}
```

Queue one ticker financial snapshot:

```http
POST /api/v1/admin/data-sync/financial-statements
Content-Type: application/json

{
  "externalReference": "شپنا",
  "idempotencyKey": "manual-cyclicalwaves-fs-shapna-2026-06-07"
}
```

Queue one ticker monthly snapshot:

```http
POST /api/v1/admin/data-sync/monthly-reports
Content-Type: application/json

{
  "externalReference": "شپنا",
  "idempotencyKey": "manual-cyclicalwaves-monthly-shapna-2026-06-07"
}
```

Inspect recent sync runs:

```http
GET /api/v1/admin/data-sync/runs?limit=20
```

## When To Use CyclicalWaves

Use CyclicalWaves when you need:

- the original scanner MVP data path;
- vendor PE/PS snapshots for `PE_TTM` and `PS_TTM`;
- quick per-ticker refreshes by Persian ticker;
- full refresh of CyclicalWaves ticker snapshots through one admin endpoint.

Do not use CyclicalWaves when you need NADPCO-only fundamental indexes or NADPCO catalog metadata.
Use `NadpcoApi` for those datasets.

## Operational Notes

- The full-sync service currently processes tickers with conservative concurrency.
- The provider stores raw payloads by checksum, so repeated identical payloads are idempotent.
- The per-ticker endpoint is used for both statements and monthly reports; failures can be retried
  per ticker through the generic data-sync endpoints.
- CyclicalWaves symbol linkage may use provider ticker symbols and existing company rows. For
  cross-provider display and lookup consistency, the query path should prefer company display
  metadata when available.
