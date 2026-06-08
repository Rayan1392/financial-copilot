# NADPCO Data Ingestion Runbook

This document explains how Financial Copilot receives data from the NADPCO HTTP API provider and
how operators should use it. For the lower-level endpoint and mapping details, see
[`nadpco-api-provider.md`](nadpco-api-provider.md).

## Provider Role

`NadpcoApi` is a named HTTP data provider. It does not replace `CyclicalWaves` or `CodalDb`; it
coexists with them and is selected through `ProviderName = "NadpcoApi"` in data-sync requests.

NADPCO currently feeds these normalized datasets:

| NADPCO data | Internal dataset | Main storage |
|---|---|---|
| Company catalog | `CompanyCatalog` / `Symbols` | `Companies`, `Symbols`, industry/group/market tables |
| Financial statements | `FinancialStatements` | `FinancialStatements`, `FinancialStatementLineItems` |
| Fundamental indexes | `FundamentalIndexes` | `DerivedMetrics` with source policy `nadpco-api-fundamental-index-source-v1` |
| Monthly product/service activity | `MonthlyProductionSales` | `MonthlyReports`, `MonthlyReportLineItems` |

## Configuration

Set credentials through secrets or environment variables. Do not store live credentials in committed
configuration files.

```powershell
$env:NadpcoApi__BaseAddress = "https://data3.nadpco.com/"
$env:NadpcoApi__UserName = "<vendor-user>"
$env:NadpcoApi__Password = "<vendor-password>"
$env:NadpcoApi__BatchSize = "100"
$env:NadpcoApi__MaxReadParallelism = "4"
```

The scheduled workflow is controlled by `NadpcoScheduledSync` in both API and Worker settings:

```json
{
  "NadpcoScheduledSync": {
    "Enabled": false,
    "CadenceSeconds": 86400,
    "ExecutionTimeUtc": null,
    "DatasetSelection": [
      "CompanyCatalog",
      "Symbols"
    ],
    "BatchSize": 100,
    "MaxConcurrency": 4,
    "RetryCount": 1,
    "RetryDelaySeconds": 30,
    "MissedScheduleRecoveryPolicy": "RunOnceImmediately",
    "MaxMissedExecutionsToCatchUp": 1,
    "MaxRunDurationSeconds": 3600,
    "LockLeaseSeconds": 7200,
    "AlertingEnabled": true,
    "AlertSeverity": "Error"
  }
}
```

Recommended production progression:

1. Start with only `CompanyCatalog` and `Symbols`.
2. After catalog quality is verified, add `FinancialStatements`.
3. Add `FundamentalIndexes` after index mappings and scale assumptions are accepted.
4. Add `MonthlyProductionSales` after monthly activity date bounds are reviewed.
5. Enable `NadpcoScheduledSync:Enabled=true` only after a successful manual/full backfill.

## Authentication Flow

The provider calls:

```http
POST /api/v2/Token
Authorization: Basic base64(username:password)
```

It caches the returned bearer token, sends `Authorization: Bearer <token>` on data requests, and
retries once after a `401` by invalidating and refreshing the token.

## Data Flow

NADPCO ingestion is intentionally two-stage:

1. An admin endpoint or the Worker coordinator creates `DataSyncRequest` rows/messages with
   `ProviderName = "NadpcoApi"`.
2. The provider fetches raw JSON, stores it in `ProviderRawPayloads`, and the matching NADPCO
   normalizer writes normalized PostgreSQL rows.

After normalization:

- statement and monthly datasets publish `MetricRecalculationRequests`;
- the Worker drains those requests and writes calculated `DerivedMetrics`;
- fundamental indexes are already vendor-precomputed and are written directly to `DerivedMetrics`;
- scanner cache is invalidated after successful sync.

No user query should call NADPCO directly. AI answers read persisted normalized tables and
`DerivedMetrics`.

## Manual Usage

All endpoints require a `DataAdmin` actor.

Check provider health:

```http
GET /api/v1/admin/provider-health
```

Load company catalog from NADPCO:

```http
POST /api/v1/admin/nadpcoapi/company-catalog/refresh
```

Use clean-slate only for first authoritative backfill or maintenance:

```http
POST /api/v1/admin/nadpcoapi/company-catalog/clean-slate
```

Run full NADPCO orchestration:

```http
POST /api/v1/admin/nadpcoapi/full-sync
```

Run incremental/overlap orchestration:

```http
POST /api/v1/admin/nadpcoapi/incremental-sync
```

Inspect sync state:

```http
GET /api/v1/admin/nadpcoapi/sync-state
```

Trigger the same scheduled workflow manually:

```http
POST /api/v1/admin/nadpcoapi/scheduled-sync/run
Content-Type: application/json

{
  "reason": "manual operator refresh after catalog verification"
}
```

Inspect scheduled runs:

```http
GET /api/v1/admin/nadpcoapi/scheduled-sync/status
GET /api/v1/admin/nadpcoapi/scheduled-sync/runs?limit=20
```

Queue one company/dataset directly:

```http
POST /api/v1/admin/data-sync/financial-statements
Content-Type: application/json

{
  "externalReference": "685",
  "providerName": "NadpcoApi",
  "idempotencyKey": "manual-nadpco-fs-685-2026-06-07"
}
```

For fundamental indexes, the endpoint forces `ProviderName = "NadpcoApi"`:

```http
POST /api/v1/admin/data-sync/fundamental-indexes
Content-Type: application/json

{
  "externalReference": "685",
  "idempotencyKey": "manual-nadpco-indexes-685-2026-06-07"
}
```

## When To Use NADPCO

Use NADPCO when you need:

- authoritative company catalog enrichment;
- broad normalized financial statements;
- curated fundamental indexes;
- monthly product/service activity from NADPCO endpoints;
- scheduled incremental refresh with persisted run history.

Do not use NADPCO for query-time answers. Run ingestion first, let normalization/recalculation
complete, then answer from the local database.

## Operational Notes

- Keep `DatasetSelection` small until the previous dataset is verified.
- NADPCO does not expose a reliable modified-since cursor for every covered endpoint. Incremental
  sync uses an overlap window from `NadpcoApi:OrchestrationOverlapDays`.
- `CompanyCatalog` scheduled refresh is non-destructive. `company-catalog/clean-slate` is a
  maintenance operation, not a recurring job.
- `InstrumentRefPlaceholder` is provenance only and must not be used as a symbol key.
- NADPCO symbol linkage prefers `tseCode`/instrument code first for company catalog rows.
