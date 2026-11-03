# Feature 135 — Noavaran ReportingType Synchronization and Deterministic Monthly-Report Routing

## Status

Planned. This specification is analysis-only; no production code is changed by this feature-spec task.

## Repository evidence used

The design is based on the current implementation and the supplied `GET api/v3/BaseInfo/Companies`
sample. The closest existing terminology is retained from specs `039`, `042`, `053`, `057`, `059`,
`076`–`078`, and `134`.

Relevant current components include:

- `NadpcoApiDataProviderClient` — one Noavaran client currently calling `ProductSales` and
  `ServiceSales`.
- `NadpcoApiCompanyRecord` and `NadpcoApiCompanyNormalizer` — provider DTO and company upsert.
- `NormalizedCompanyRow` / `Companies` — persisted company catalog.
- `NoavaranCompanyScope` and `NoavaranEligibleCompanies` — eligible-company enumeration.
- `NadpcoApiScheduledSyncService`, `MonthlyActivityBackfillCoordinator`, and
  `SingleCompanyMonthlyIngestionService` — scheduled, backfill, and direct acquisition paths.
- `FinancialDataSyncProcessor` and `NadpcoApiMonthlyActivityNormalizer` — raw capture, normalize,
  persist, and derived-data recalculation.
- `CompanyMonthlyActivityTrendSnapshotCalculator` and the existing monthly-sales query/chart path —
  downstream consumers that must remain compatible.

## Problem

The Noavaran company catalog now returns an authoritative numeric `reportingType`, but the local
company contract and `Companies` persistence model do not store it. The current monthly activity
provider therefore uses response-driven behavior: it calls `ProductSales`, examines whether the
successful response contains usable rows, and may then call `ServiceSales`.

That behavior creates unnecessary calls and latency, does not establish a durable company
classification, and cannot safely grow to additional reporting categories. It also makes an HTTP 200
empty response part of company-type detection even though an empty response is a data outcome, not
classification metadata.

## Business value

- Reduces avoidable Noavaran monthly-report calls.
- Makes endpoint selection deterministic, explainable, and observable.
- Preserves future `ReportingType` values without forcing an incorrect default.
- Allows supported manufacturing, agriculture, and service acquisition to evolve independently.
- Keeps normalized monthly sales, derived metrics, snapshots, AI lookup, and chart behavior stable.

## Scope

This feature covers:

1. Reading `reportingType` from `api/v3/BaseInfo/Companies`.
2. Persisting it as nullable numeric metadata on Noavaran current-API company rows.
3. Updating it on every successful catalog upsert, including existing rows.
4. Documenting every supplied sample field against the current DTO, local model, table, and mapping.
5. Synchronizing the verified safe company-field gap `marketBoardTitle` into the existing
   `MarketBoard` string when present, while retaining the legacy `marketBoard` alias.
6. Resolving supported `ReportingType` values to the existing monthly provider endpoints.
7. Removing response probing as the normal company-type selection mechanism.
8. Defining fail-safe handling for null, malformed, unknown, unsupported, stale, and failed metadata.
9. Preserving the existing normalized monthly-report and monthly-sales consumer contracts.

`ReportingType` is stored as the raw provider code. A typed resolver may classify it in application
code, but the database must retain the numeric value so a future code such as `1000010` is not lost.

## User stories

### Story 1 — Synchronize company reporting metadata

As the FinancialCopilot data-acquisition system, I want to synchronize each company’s
`ReportingType` from Noavaran Amin, so that downstream acquisition services know which reporting
model applies without probing multiple APIs.

### Story 2 — Route monthly reports by ReportingType

As the FinancialCopilot monthly-report acquisition system, I want to select the correct Noavaran
monthly-report endpoint using the company’s persisted `ReportingType`, so that only the appropriate
report API is called and report-type detection is deterministic.

### Story 3 — Complete verified Noavaran company metadata mapping

As a data steward, I want the verified company-catalog gaps to be explicitly mapped, persisted, or
deferred with evidence, so that missing fields are not silently ignored and classification fields
are not conflated.

## Functional requirements

### FR-1 — Company contract and persistence

- Add a nullable numeric `ReportingType` to the Noavaran company provider DTO.
- Add a nullable numeric `ReportingType` to `NormalizedCompanyRow` and the `Companies` table.
- Use a PostgreSQL integer-compatible type based on the confirmed numeric codes; do not persist a
  localized description or a closed enum value.
- Map explicit provider null to local null.
- Upsert the value for both new and existing rows keyed by the existing
  `(ProviderName, ExternalCompanyId)` identity.

### FR-2 — Catalog gap handling

- Keep the existing verified mappings for identity, symbols, dates, legal fields, and dimension
  references.
- Map `marketBoardTitle` to the existing `MarketBoard` text field, with the legacy provider
  `marketBoard` field retained as a compatibility fallback.
- Add DTO coverage for `marketBoardID`, `activityTypeID`, and `activityTypeTitle` so their presence
  is not accidentally ignored by a future contract review, but do not add speculative database
  columns for them in this feature.
- Keep `ActivityType` separate from `ReportingType`.

### FR-3 — Authoritative resolver

- Introduce one authoritative resolver/classification table for Noavaran monthly routing.
- The resolver must return an explicit outcome such as `Manufacturing`, `Service`, `Unsupported`,
  `Unknown`, `Missing`, or `Malformed`; it must not default to either endpoint.
- The resolver must use persisted `Company.ReportingType`, never company name, symbol, industry,
  floor, `ActivityType`, report contents, or an empty response as a substitute when metadata exists.

### FR-4 — Verified endpoint mapping

- `1000000` maps to the existing manufacturing `ProductSales` endpoint:
  `POST api/v2/MonthlyActivity/ProductSales`.
- `1000005` maps to the existing service endpoint:
  `POST api/v3/MonthlyActivity/ServiceSales`.
- `1000008` maps to the existing agriculture `ProductSales` endpoint:
  `POST api/v2/MonthlyActivity/ProductSales`, including applicable ProductSales output types.
- Codes `1000001`, `1000002`, `1000003`, `1000004`, `1000006`, `1000007`, and `1000009`
  have no verified endpoint mapping in the repository and must remain unsupported until provider
  evidence is added.
- A future or otherwise unknown code must not be mapped to manufacturing or service.

### FR-5 — Acquisition behavior

- A manufacturing or agriculture company may continue to use the existing ProductSales output-type
  behavior (0–4, where applicable) and existing normalizer/persistence path.
- A service company must call ServiceSales directly and must not first call ProductSales as a type
  probe.
- A correctly classified supported company must make no cross-endpoint fallback call merely because
  the selected endpoint returns HTTP 200 with no rows.
- Provider failures remain failures; they are not reclassified as another company type.

### FR-6 — Missing and unsafe metadata

- Null metadata, malformed metadata, unknown future codes, and known-but-unsupported codes must
  produce a deterministic no-route outcome with structured diagnostics.
- Existing rows must not receive a default `ReportingType` during migration.
- A successful catalog response with `reportingType: null` sets the row to null; a failed catalog
  synchronization does not overwrite the last successful row.
- Stale but valid persisted metadata may be used for a run, with a stale-metadata diagnostic; it
  must not trigger endpoint probing. A later successful catalog synchronization is the remediation.

### FR-7 — Downstream compatibility

- Preserve `MonthlyReports`, `MonthlyReportLineItems`, `DerivedMetrics`, and
  `CompanyMonthlyActivityTrendSnapshots` contracts.
- Preserve ProductSales-over-ServiceSales precedence for already persisted historical rows and late
  publications; this feature changes acquisition selection, not the normalized analytical model.
- Do not change AI semantic routing, public AI tools, chart rendering, or Telegram behavior.

### FR-8 — Observability

Routing diagnostics should follow existing logging/telemetry conventions and include, where
available, `CompanyId`/`ExternalCompanyId`, `CompanySymbol`, `ReportingType`, resolver outcome,
provider type, selected endpoint/client, sync result, and unsupported code. Credentials and secrets
must never be logged, and batch jobs must not emit unbounded per-record noise.

## Acceptance criteria

1. **AC-01:** `NormalizedCompanyRow` exposes nullable numeric `ReportingType`.
2. **AC-02:** An EF Core migration adds nullable `Companies.ReportingType` with no incorrect default.
3. **AC-03:** `NadpcoApiCompanyRecord` deserializes numeric `reportingType`, including the supplied
   `1000008` sample.
4. **AC-04:** A new company catalog row persists `ReportingType`.
5. **AC-05:** A later catalog sync updates an existing company’s `ReportingType` without duplicating
   the company.
6. **AC-06:** An explicit upstream `reportingType: null` is persisted as null.
7. **AC-07:** A failed catalog synchronization does not assign or overwrite a value through a
   migration default or guessed fallback.
8. **AC-08:** The field-mapping matrix documents every field in the supplied sample through API DTO,
   local model, table/related table, synchronization mapping, nullability, and action.
9. **AC-09:** `marketBoardTitle` is mapped to the existing `MarketBoard` field without losing support
   for the legacy `marketBoard` payload name.
10. **AC-10:** `marketBoardID`, `activityTypeID`, and `activityTypeTitle` are explicitly classified
    as deferred/provider-only or otherwise scoped; no speculative columns are added.
11. **AC-11:** The resolver has one authoritative mapping location and distinguishes supported,
    unsupported, unknown, missing, and malformed outcomes.
12. **AC-12:** Reporting code `1000000` resolves only to ProductSales.
13. **AC-13:** Reporting code `1000005` resolves only to ServiceSales.
14. **AC-14:** Codes `1000001`, `1000002`, `1000003`, `1000004`, `1000006`, `1000007`, and `1000009`
    do not silently resolve to either existing endpoint.
14A. **AC-14A:** A company with `ReportingType = 1000008` resolves to ProductSales, calls the
    ProductSales endpoint, and does not call ServiceSales.
15. **AC-15:** The resolver uses `ReportingType` and does not use `ActivityType`, industry, floor,
    company identity text, report contents, or response probing when ReportingType is available.
16. **AC-16:** A manufacturing acquisition calls ProductSales and does not call ServiceSales.
17. **AC-17:** A service acquisition calls ServiceSales and does not call ProductSales.
18. **AC-18:** An empty HTTP 200 response from the selected endpoint does not automatically invoke
    the other endpoint for a correctly classified supported company.
19. **AC-19:** Provider failure is surfaced through the existing retry/failure lifecycle and is not
    interpreted as a different reporting category.
20. **AC-20:** Null, malformed, unknown, and known-but-unsupported values produce no-route behavior,
    structured diagnostics, and no unrelated provider call.
21. **AC-21:** Stale valid metadata has deterministic behavior and observable staleness; a failed
    metadata refresh does not silently reclassify the company.
22. **AC-22:** Existing monthly-report normalization and persistence remain schema-compatible.
23. **AC-23:** Existing monthly-sales metrics, snapshots, AI lookup, and chart-generation regressions
    pass for ProductSales and existing ServiceSales data.
24. **AC-24:** Focused tests assert both the selected endpoint call and that the other endpoint was not
    called.
25. **AC-25:** Deployment and backfill documentation verifies catalog synchronization before normal
    type-driven monthly acquisition is enabled.
26. **AC-26:** The relevant unit, integration, migration, acquisition, persistence, and chart/query
    regression suites pass, or documented environment blockers are recorded.

## Out of scope

- Implementing production code in this specification task.
- Inventing endpoint support for construction, investment, banking, leasing, insurance, maritime,
  investment banking, or future categories.
- Deriving ReportingType from ActivityType, industry, floor, names, symbols, or report contents.
- Adding a second company table, a permanent Production/Service classification table, or duplicate
  provider clients.
- Redesigning the AI orchestration layer, semantic routing, chart rendering, Telegram behavior, or
  public endpoints.
- Deleting or rewriting existing normalized monthly reports.
