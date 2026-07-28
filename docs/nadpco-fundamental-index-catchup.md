# NADPCO All-Index Fundamental-Index Catch-up Coverage (spec 050)

## Curated sync vs. all-index catch-up

There are now **two** fundamental-index paths against the same NADPCO endpoint
(`POST /api/v2/CompanyFundamentalIndex/Values`):

| | Curated sync (spec 041) | All-index catch-up coverage (spec 050) |
|---|---|---|
| `companyIndexIds` | the reviewed allowlist (`NadpcoApiFundamentalIndexMap.MappedIndexIds`) | **empty `[]`** — the vendor returns every index |
| Year range | configured `FundamentalIndexFromYear`/`ToYear` | the catch-up window (default Shamsi **1403→1405**) |
| Dataset | `ProviderDataset.FundamentalIndexes` | `ProviderDataset.FundamentalIndexCoverage` |
| Persistence | governed `DerivedMetrics` (scannable) | `NadpcoFundamentalIndexObservations` (**non-scannable** staging) |
| Promotion to scanner | yes, for reviewed/mapped indexes | never automatic |

The catch-up is **coverage data**: it captures everything the vendor offers so we can later decide,
with review, which additional indexes to promote. It does **not** make any vendor index a governed
scanner metric — that remains exclusively the curated 041 path. The coverage normalizer flags which
observations are governed candidates (`IsGovernedCandidate`, true when the 041 allowlist maps the
index id) but never writes `DerivedMetrics`.

## When to run

Run the all-index catch-up once as a backfill after the company catalog is populated, to build local
coverage of every vendor index for 1403–1405. Use the curated sync (and the scheduled current-API
worker) for ongoing governed-metric freshness. The catch-up is DataAdmin-only and is **not** on any
recurring worker.

## DataAdmin endpoints

| Endpoint | Purpose |
|---|---|
| `POST /api/v1/admin/nadpcoapi/fundamental-index-catch-up` | Enumerate all local NADPCO companies and enqueue bounded all-index coverage requests. Body: `{ "fromShamsiYear": 1403, "toShamsiYear": 1405 }` (defaults 1403/1405; validated 1380–1500, From ≤ To). |
| `GET /api/v1/admin/nadpcoapi/fundamental-index-catch-up/runs?limit=N` | Recent catch-up run history. |

## Pipeline

1. `FundamentalIndexCatchUpCoordinator` enumerates local NADPCO companies (`Companies` rows whose
   `ProviderName = NoavaranCurrentApi`), then enqueues one bounded
   `ProviderDataset.FundamentalIndexCoverage` `DataSyncRequest` per company (bounded by
   `MaxReadParallelism`), carrying the Shamsi year range in `SourceDateRangeStart/EndJalali`.
   Per-company enqueue failures are isolated; a second concurrent run is rejected via a run lease.
   Run history is persisted in `FundamentalIndexCatchUpRuns`.
2. The worker consumes each request; `FinancialDataSyncProcessor` routes the coverage dataset to
   `NadpcoApiDataProviderClient.FetchAllFundamentalIndexesAsync` (empty `companyIndexIds`, explicit
   `fromYear`/`toYear`), stores the raw payload with checksum/idempotency, then runs
   `NadpcoApiFundamentalIndexCoverageNormalizer`.
3. The coverage normalizer applies the same deterministic variant selection as 041 (prefer audited,
   not-represented, composing, later announcement, higher statement id) and converts Jalali period
   dates to Gregorian, then upserts every index into `NadpcoFundamentalIndexObservations` (unique key
   `(provider, company, indexId, periodType, periodEnd)`), preserving company/index/group/value/unit,
   audited/represented/composing flags, Jalali dates, announcement date, and the source checksum.

## Persistence

- `NadpcoFundamentalIndexObservations` — one canonical observation per
  `(ProviderName, ExternalCompanyId, CompanyIndexId, PeriodType, PeriodEnd)`. Non-scannable; the
  scanner and metric registry never read it.
- `FundamentalIndexCatchUpRuns` — run history (status, requestedBy, year range, counts, failed company
  ids, diagnostics, lease).
- Migration `20260609105852_AddNadpcoFundamentalIndexCoverage` (two new tables; additive).

## Out of scope

- Exposing every returned vendor index to scanner users automatically.
- Creating metric aliases/formulas/calculators from vendor titles without review.
- Query-time calls to NADPCO; replacing the curated 041 path.
- Scheduled selection of the catch-up (DataAdmin one-off only for this story).
