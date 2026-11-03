# Feature 134 — Noavaran Service Sales Fallback and Monthly Trend

## Status

`[x]` Implemented and verified, including manual and Telegram trigger inheritance

## User story

As a FinancialCopilot user, I want the existing monthly activity synchronization and monthly
sales trend chart to support service companies, so that a monthly-sales trend request works when
Noavaran returns activity through `ServiceSales` instead of `ProductSales`.

The feature extends the existing Noavaran monthly-activity, persistence, recalculation, snapshot,
AI query, and chart contracts. It does not introduce a second ingestion or chart pipeline.

## Manual and Telegram trigger audit

The earlier audit was specification-only. The implementation that follows this audit applies one
shared provider correction and focused regression coverage; no separate Admin or Telegram pipeline
is introduced.

The exact manual entry point is:

```text
POST /api/v1/admin/noavaran-current/monthly-backfill/single-company-month
```

The route is owned by `NoavaranMonthlyBackfillController.RunSingleCompanyMonth`. It resolves an
optional symbol through `NoavaranEligibleCompanies`, validates the company/year/month, and calls
`ISingleCompanyMonthlyIngestionService.ExecuteDirectAsync` for one company-month. This is distinct
from `POST /api/v1/admin/noavaran-current/single-company-monthly-ingestion`, which is the older
range-enqueue operation and is not the audited direct endpoint.

The Telegram path is:

```text
Telegram channel_post/caption
  -> TelegramGatewayPollingWorker
  -> PrimaryApiClient
  -> POST /api/v1/telegram/assistant/updates
  -> TelegramAssistantController
  -> TelegramChannelMonthlyReportHandler
  -> ISingleCompanyMonthlyIngestionService.ExecuteDirectAsync
```

The two paths share the direct ingestion service and therefore share its provider and processor.
However, `SingleCompanyMonthlyIngestionService.ExecuteDirectAsync` calls
`INadpcoMonthlyProductSalesDirectProvider.FetchProductSalesAllOutputTypesAsync` with the direct
path's current behavior, while `NadpcoApiDataProviderClient` enables `ServiceSales` fallback only
for the scheduled `FetchMonthlyReportsAsync` path. Consequently, the Feature 134 fallback is
**not currently inherited automatically** by either the audited admin direct path or the Telegram
trigger path. A successful empty ProductSales type-0 response can therefore skip the required
ServiceSales request in both paths. The shared direct path needs one production correction, followed
by path-specific regression coverage; no second fallback implementation should be added.

The existing normalization, persistence, source-precedence, recalculation, snapshot, and chart
contracts remain compatible with both triggers once the direct provider selects the same type-0
fallback decision. Feature 133's existing Telegram recognition, authorization, deduplication,
freshness, billing, rendering, and delivery behavior must remain unchanged.

## Dependencies and inherited contracts

This feature depends on and preserves the contracts from specs `042`, `053`, `057`, `059`, and
`076`–`078`:

- Current-API monthly activity is permitted from Shamsi `1404/01` onward. Monthly activity before
  Shamsi 1404 remains owned by the frozen `NoavaranArchiveSql` path and must not cause current-API
  `ServiceSales` requests.
- All per-company current-API requests use the authoritative `NoavaranCompanyScope`. The
  `NoavaranEligibleCompanies` PostgreSQL view mirrors that scope for operators; it is not a second,
  independently defined eligibility rule. The company-catalog synchronization remains unscoped.
- ProductSales output types 0–4 continue to be fetched and persisted according to spec `059`.
  Output types 1–4 remain available for their existing downstream uses.
- Monthly trend bars and monthly sales aggregation use ProductSales `OutputType = 0` or the
  ServiceSales monthly equivalent selected by the source-precedence rule below. The AI query path
  reads persisted snapshots and never calls Noavaran or aggregates raw line items.
- ServiceSales uses the same Noavaran monetary semantics as ProductSales: provider values are in
  million Rials, normalized through the existing Noavaran ingestion/recalculation path, promoted
  to canonical monetary metrics using the existing conversion policy, and exposed through the
  existing trend/chart display contract. No new unit conversion is introduced here.

## Source selection per company-month

The decision is made for one logical `(ExternalCompanyId, ShamsiYear, ShamsiMonth)` at a time.

1. Enumerate only companies in `NoavaranCompanyScope` / its mirrored
   `NoavaranEligibleCompanies` view and use their Noavaran company IDs.
2. Fetch ProductSales output types 0–4 using the existing bounded request behavior. The output
   types may be fetched by separate existing queue messages, but source selection must not be
   performed independently by each output-type worker.
3. Wait until the ProductSales `OutputType = 0` outcome for the company-month is known.
4. If ProductSales `OutputType = 0` contains usable monthly activity data, ProductSales is the
   authoritative monthly source and `ServiceSales` is not called for that logical attempt. Valid
   zero-activity report rows remain valid data under spec `042` and do not trigger fallback.
5. If ProductSales `OutputType = 0` succeeds but is empty or contains no usable monthly activity
   rows, call `POST /api/v3/MonthlyActivity/ServiceSales` exactly once for that company-month and
   logical ingestion attempt. Data in ProductSales output types 1–4 must not suppress this
   fallback.
6. If the ProductSales type-0 request fails because of transport, timeout, authorization, or
   invalid payload, do not call `ServiceSales` as a classification fallback. Preserve the existing
   failure and retry behavior.
7. A concurrent or retried worker must use the existing idempotency/run coordination so that one
   logical company-month attempt cannot issue multiple ServiceSales fallback calls. A later retry
   is a new logical attempt and may call ServiceSales again.

These rules separate ProductSales multi-output ingestion from monthly-sales source selection.
The other ProductSales output types may complete independently, but they must not mark the
company-month complete before the type-0 decision and fallback outcome are resolved.

## Ingestion lifecycle and no-data semantics

The following states are distinct:

- An HTTP/API request can be technically successful.
- A successful response can contain usable business rows, or can be empty.
- A company-month is complete only when usable monthly report rows have been persisted.

If ProductSales type 0 is successful but empty and ServiceSales is also successful but empty, the
result is a successful no-data observation, not successful ingestion. The company-month remains
`NoDataYet` / `Retryable` according to the existing `053`/`057` lifecycle and must not make the
backfill permanently complete or return `AlreadyCompleted`.

If either selected source produces usable rows and those rows are persisted, the company-month
may be marked completed under the existing run-status rules. Provider failures remain failures and
are retried through the existing path; they are never interpreted as evidence that the company is
a service company.

No permanent Production/Service company classification is persisted or inferred. The decision is
repeated for each later refresh so newly published or corrected reports can be discovered.

## ServiceSales mapping and stable identity

Reuse the existing Noavaran credential/configuration boundary. Credentials must never appear in
source, logs, fixtures, or this specification.

ServiceSales normalization must preserve provider evidence for:

- `companyId`, `companyTSESymbol`, `companyTitle`, and `instCode`;
- year, month, `publishDateTime`, fiscal/publication metadata, category, and industry metadata;
- `serviceTitle`;
- `revenueDuringThePeriod`, `revenueFromTheBeginning`, and `revenueEndOfLastPeriod` when supplied.

`revenueDuringThePeriod` is the only ServiceSales value used as the reported single-month sales
fact. The cumulative revenue fields are evidence only and must never be substituted for monthly
sales or counted again in the monthly total. Existing raw-payload/evidence storage may be reused
when a dedicated normalized column is not required by the current model.

The logical report identity is the canonical ServiceSales identity from spec `042`:

```text
ServiceSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-none
```

Category, industry, `serviceTitle`, and line-item grouping metadata must never be part of the
logical report key.

Service line identity must preserve legitimate distinct rows deterministically:

1. Use a stable provider service ID/code when it uniquely identifies the line within the logical
   report.
2. If the same provider ID/code occurs with different legitimate normalized `serviceTitle` values,
   include the normalized title in the line-item identity so those rows remain distinct.
3. If provider ID/code is missing or insufficient, use a deterministic natural key containing the
   company instrument code, normalized service title, normalized unit/category evidence, and a
   deterministic occurrence discriminator where required. The key must be stable across identical
   payload replays and must not depend on an arbitrary database-generated ID.

The difficult case is mandatory: for one company and month, repeated or missing service IDs/codes
with different legitimate service titles must produce separate persisted line items, and each line
must be included exactly once in aggregation.

## ProductSales versus ServiceSales precedence

Persisted normalized records and the authoritative monthly source are separate concepts. Raw and
normalized ServiceSales evidence may remain persisted after a later ProductSales publication.

For the same company-month:

- usable ProductSales `OutputType = 0` takes precedence over ServiceSales;
- the trend calculator, monthly aggregate input sources, and snapshot rebuild must exclude
  ServiceSales from the monthly total whenever usable ProductSales type-0 data exists;
- when no usable ProductSales type-0 data exists, ServiceSales is the monthly source;
- the aggregation must never calculate `ProductSales total + ServiceSales total` for the same
  company-month merely because both records are stored.

Recalculation and backfill must apply this rule deterministically, including when a ServiceSales
fallback was previously persisted and ProductSales type 0 becomes available later.

## Trend, snapshot, and AI/chart behavior

Successful ServiceSales ingestion must reuse the existing recalculation path and must update the
same `CompanyMonthlyActivityTrendSnapshots` pipeline used by ProductSales.

The existing monthly trend path must be extended consistently in all places that select monthly
activity data:

- incremental recalculation after ServiceSales persistence;
- monthly trend snapshot calculation;
- snapshot backfill/rebuild candidate discovery and processing;
- any persisted monthly-sales aggregation used by the trend feature.

The AI/tool layer and chart contract remain unchanged. A request such as `روند فروش ماهانه قاسم`
must read the persisted snapshot and return the existing chart payload regardless of whether the
authoritative monthly source was ProductSales or ServiceSales. Missing months remain `null`/missing,
not zero. ServiceSales quantities and production quantities must not be invented or aggregated when
the endpoint does not provide them.

## Acceptance criteria

1. Current-API monthly activity, including ServiceSales fallback, is limited to Shamsi 1404 onward;
   pre-1404 monthly activity remains archive-owned.
2. Company enumeration uses the existing authoritative Noavaran eligibility scope and does not
   create a separate eligibility rule.
3. ProductSales output types 0–4 retain the existing spec `059` fetch and persistence behavior.
4. Monthly-sales fallback selection is coordinated per company-month, not independently per output
   type or worker.
5. ProductSales type 0 with usable data is authoritative, persists through the existing path, and
   prevents a ServiceSales call for that logical attempt.
6. ProductSales type 0 with HTTP success but no usable rows triggers exactly one ServiceSales
   fallback attempt for that company-month and logical ingestion attempt.
7. ProductSales data existing only in output types 1–4 does not suppress the ServiceSales fallback.
8. A ProductSales type-0 transport, timeout, authorization, or invalid-payload failure does not
   trigger ServiceSales classification fallback and remains on the existing failure/retry path.
9. ServiceSales is not called before the ProductSales type-0 outcome is known, and concurrent
   workers cannot issue duplicate fallback calls for one logical attempt.
10. ServiceSales rows are normalized through the existing `MonthlyReports` and
    `MonthlyReportLineItems` path with publication and provider evidence preserved.
11. ServiceSales monthly sales uses `revenueDuringThePeriod`; cumulative revenue fields are not
    counted as monthly sales.
12. Multiple legitimate ServiceSales lines remain distinct and idempotent, including repeated or
    missing service IDs/codes with different service titles.
13. If both ProductSales type 0 and ServiceSales records are stored for a company-month, trend and
    monthly-sales aggregation use ProductSales only when usable ProductSales type-0 data exists.
14. If ProductSales type 0 has no usable data and ServiceSales has usable rows, ServiceSales becomes
    the authoritative monthly source and the company-month is completed after persistence.
15. If both ProductSales type 0 and ServiceSales are empty, no false completion occurs; the
    company-month remains `NoDataYet` / `Retryable` according to specs `053` and `057`.
16. A later ProductSales type-0 publication supersedes ServiceSales for analytical purposes without
    double-counting, and the result is deterministic on recalculation and snapshot rebuild.
17. ServiceSales changes trigger the existing derived recalculation path and update the existing
    monthly trend snapshot.
18. Snapshot calculation and snapshot backfill/rebuild include ServiceSales-only company-months
    through the same persisted snapshot table and chart contract.
19. The existing AI monthly-sales trend query returns service-company chart data from persistence,
    without a query-time Noavaran call or raw line-item aggregation.
20. ServiceSales values follow the existing Noavaran monetary normalization and chart display units;
   no new unit conversion or service-specific chart contract is introduced.

### Additional trigger-inheritance acceptance criteria

21. The exact admin route `POST /api/v1/admin/noavaran-current/monthly-backfill/single-company-month`
    invokes the shared direct single-company/month ingestion path with the resolved eligible
    company and requested Shamsi month; it must not use the older range-enqueue route.
22. The admin direct path applies the Feature 134 type-0 decision: usable ProductSales suppresses
    ServiceSales, successful empty ProductSales calls ServiceSales exactly once, output types 1–4
    do not suppress that fallback, and type-0 provider failure does not classify the company as a
    service company.
23. Admin direct ingestion persists a usable ServiceSales fallback through the existing normalized
    report, recalculation, trend snapshot, and no-data/retry lifecycle without a second pipeline.
24. The admin direct path applies ProductSales-over-ServiceSales precedence and does not
    double-count a previously persisted ServiceSales report when ProductSales type 0 arrives later.
25. A Telegram original `channel_post` or textual caption reaches the existing
    `/api/v1/telegram/assistant/updates` boundary and `TelegramChannelMonthlyReportHandler`, which
    invokes the same shared direct ingestion service for the recognized company-month.
26. The Telegram direct path applies the same Feature 134 type-0 decision and ServiceSales request
    contract as the scheduled path and the audited admin direct path.
27. Telegram retry/replay and persisted claim behavior prevent duplicate direct ingestion or
    duplicate ServiceSales fallback calls for one logical channel post; existing Feature 133
    disabled, failure, freshness, billing, rendering, and delivery outcomes remain unchanged.
28. The admin and Telegram paths use the same Noavaran 1404+ boundary, monetary semantics,
    normalized identity, source precedence, no-data/retry behavior, and snapshot/chart contract as
    the scheduled path.
29. No permanent Production/Service classification, new public endpoint, new AI tool, new chart
    contract, or separate Telegram ingestion pipeline is introduced to close the trigger gap.
30. Focused regressions cover the admin direct path and Telegram direct path for usable ProductSales,
    empty ProductSales with ServiceSales rows, type-0 failure, both sources empty, output types 1–4
    present, late ProductSales precedence, 1404 boundary, and monetary/source compatibility.

## Required scenarios

| Scenario | Expected result |
|---|---|
| Manufacturing company: ProductSales type 0 has usable data | ProductSales is used; ServiceSales is not called; trend uses ProductSales. |
| Service company: ProductSales type 0 is successful but empty; ServiceSales has rows | ServiceSales is called once, persisted, considered usable completion, and included in the existing trend snapshot/chart. |
| Neither source has data | No false completion; company-month remains `NoDataYet` / `Retryable`. |
| ProductSales type 0 empty; type 1/2/3/4 has data | ServiceSales fallback still occurs; non-zero output types do not decide monthly-sales source selection. |
| ServiceSales persisted first; ProductSales type 0 arrives later | ProductSales becomes authoritative; trend does not double-count ServiceSales. |
| Multiple service lines with repeated/missing IDs or codes and different titles | Each legitimate line survives normalization and is counted once. |

## Out of scope

- Permanent company-sector classification or Production/Service schema fields.
- Query-time calls to Noavaran.
- New monthly-trend tables, public endpoints, AI tools, chart contracts, or separate workers.
- Product/service revenue-mix analysis.
- Service quantity or production calculations when the ServiceSales endpoint does not provide those
  facts.
