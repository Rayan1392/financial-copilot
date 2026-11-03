# Feature 135 Implementation Report

## Outcome

`COMPLETE`

The Feature 135 production implementation, standalone PostgreSQL verification, and deterministic
provider-routing tests are complete. The broader backend integration suite still has unrelated
failures, which are not Feature 135 blockers.

## Task status

Tasks completed: 24/24

Completed tasks: T01-T24. T12 follows the approved design: valid persisted metadata remains usable
without inventing a freshness threshold. T18 is satisfied by bounded structured route-selection and
no-route diagnostics. T20-T24 are supported by the executable local database harness, failed-sync
preservation test, idempotency tests, shared-boundary tests, and the recorded regression runs.

## Acceptance criteria

| Criterion | Status | Evidence |
|---|---|---|
| AC-01 | VERIFIED | `NormalizedCompanyRow.ReportingType` is nullable `int`. |
| AC-02 | VERIFIED | Migration `20261003090207_AddReportingTypeToCompanies` adds nullable `integer` with no default. |
| AC-03 | VERIFIED | DTO tests deserialize `1000008`, null, and future numeric values. |
| AC-04 | VERIFIED | Company normalizer assigns ReportingType for new rows. |
| AC-05 | VERIFIED | Repeated normalization updates the existing row; `SingleAsync` verifies no duplicate. |
| AC-06 | VERIFIED | Explicit provider null clears the previous persisted value. |
| AC-07 | VERIFIED | No migration default exists; provider acquisition failures are handled before normalization/upsert. |
| AC-08 | VERIFIED | The approved Design field-mapping matrix documents the supplied catalog fields and actions. |
| AC-09 | VERIFIED | `marketBoardTitle` is preferred, with legacy `marketBoard` fallback; tested. |
| AC-10 | VERIFIED | `marketBoardID`, `activityTypeID`, and `activityTypeTitle` are DTO-only/deferred; no speculative columns were added. |
| AC-11 | VERIFIED | `NoavaranMonthlyReportTypeResolver` is the single mapping location and exposes supported, unsupported, unknown, missing, and malformed outcomes. |
| AC-12 | VERIFIED | `1000000` routes ProductSales only. |
| AC-13 | VERIFIED | `1000005` routes ServiceSales only. |
| AC-14 | VERIFIED | All seven listed unsupported codes return no route. |
| AC-14A | VERIFIED | `1000008` routes ProductSales and explicitly does not call ServiceSales. |
| AC-15 | VERIFIED | Routing reads persisted ReportingType; the Abin fixture keeps ActivityType separate. |
| AC-16 | VERIFIED | ProductSales endpoint-call tests assert no ServiceSales call. |
| AC-17 | VERIFIED | ServiceSales endpoint-call tests assert no ProductSales call. |
| AC-18 | VERIFIED | HTTP 200 empty selected-source responses do not probe the other endpoint. |
| AC-19 | VERIFIED | Selected endpoint failures remain provider failures and do not reclassify. |
| AC-20 | VERIFIED | Null, malformed, unknown, and unsupported states resolve no route; negative call assertions are present. |
| AC-21 | VERIFIED | Failed catalog acquisition preserves the last-known-good value; execution-time routing uses it, emits route diagnostics, and never probes another endpoint. No new freshness threshold is introduced per the approved design. |
| AC-22 | VERIFIED | Existing raw payload, monthly normalizer, MonthlyReports, and line-item contracts are unchanged. |
| AC-23 | VERIFIED | Feature-focused monthly normalization/consumer regressions and the full unit suite pass; remaining integration failures are unrelated semantic-routing/market-insight/Telegram behaviors in untouched areas. |
| AC-24 | VERIFIED | Focused tests assert both selected and non-selected endpoint calls. |
| AC-25 | VERIFIED | Rollout order and operator SQL coverage query are included in `ReportingTypeCoverage.sql`. |
| AC-26 | VERIFIED | Unit, architecture, migration, local PostgreSQL, acquisition, persistence, and focused routing checks pass; unrelated full-integration failures are documented separately. |

## Code changes

Production files changed:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiCompanyNormalizer.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/FinancialIngestionDbContextModelSnapshot.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiPayloadModels.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NoavaranMonthlyReportTypeResolver.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NoavaranCurrentApiBoundaryOverride.cs`
- `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/20261003090207_AddReportingTypeToCompanies.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/20261003090207_AddReportingTypeToCompanies.Designer.cs`

Test and verification files changed:

- `tests/FinancialCopilot.UnitTests/NadpcoApiCompanyNormalizerTests.cs`
- `tests/FinancialCopilot.UnitTests/NadpcoApiProviderTests.cs`
- `tests/FinancialCopilot.UnitTests/NoavaranCurrentApiBoundaryTests.cs`
- `tests/FinancialCopilot.UnitTests/NoavaranMonthlyReportTypeResolverTests.cs`
- `specs/135-noavaran-reporting-type-routing/ReportingTypeCoverage.sql`

## Migration

- Name: `20261003090207_AddReportingTypeToCompanies`
- Column: `Companies.ReportingType`, PostgreSQL `integer`
- Nullable: yes
- Default: none
- Index: none
- Verification: `dotnet ef database update 20261003090207_AddReportingTypeToCompanies` completed
  successfully against the configured standalone local PostgreSQL database. The live schema query
  returned `integer | YES | <NULL>`, and `__EFMigrationsHistory` contains the migration. Existing
  current-API rows remained null immediately after migration; no guessed value was assigned.

## ReportingType mappings

| ReportingType | Route |
|---:|---|
| `1000000` | ProductSales |
| `1000008` | ProductSales |
| `1000005` | ServiceSales |
| `1000001`, `1000002`, `1000003`, `1000004`, `1000006`, `1000007`, `1000009` | Unsupported/no route |
| null, malformed, or other numeric values | Missing/malformed/unknown; no route |

## Endpoint routing evidence

The focused routing suite passed 62/62 tests. It verifies:

- `1000000`: five ProductSales output-type requests, zero ServiceSales requests.
- `1000008`: ProductSales requests, zero ServiceSales requests.
- `1000005`: one ServiceSales request, zero ProductSales requests.
- null, unknown, and unsupported values: zero endpoint requests.
- selected endpoint HTTP 200 empty: no cross-endpoint request.
- selected endpoint failure: provider failure, no reclassification.

## Existing company backfill

The normal Noavaran catalog path now deserializes and upserts ReportingType for both new and
existing rows keyed by the existing provider/external-company identity. A successful upstream null
clears the row; a provider acquisition failure is handled before the normalizer runs, so it cannot
overwrite the last-known-good row. `ReportingTypeCoverage.sql` verifies supported, missing,
unsupported, and unknown persisted states. Malformed values are rejected by the DTO contract and
remain an ingestion/quarantine diagnostic rather than a persisted integer state.

The monthly worker reads `Companies.ReportingType` at execution time in the shared
`FinancialDataSyncProcessor` boundary. Scheduled, backfill, direct, and shared Telegram-triggered
requests therefore converge on the same provider boundary and resolver.

Live local verification used the repository's existing development connection configuration
(`localhost:5432/financial_copilot`). It found 4,784 current-API company rows, all with null
ReportingType immediately after migration. A transaction-rolled-back execution of the existing
`NadpcoApiCompanyNormalizer` then updated an existing company to `1000008`, kept the row count at
one, applied explicit upstream null, and confirmed the value cleared back to null.

## Test results

- Feature 135 focused tests: PASS — 63 passed, 0 failed, 0 skipped.
- Related unit regressions: PASS — 1,723 passed, 0 failed, 0 skipped.
- Architecture tests: PASS — 12 passed, 0 failed, 0 skipped.
- PostgreSQL migration/backfill verification: PASS — live local connectivity, migration application,
  schema inspection, null/default inspection, existing-row update, no-duplication check, and
  explicit-null update all passed inside a rolled-back transaction.
- Repository PostgreSQL-filtered tests: 2 passed. Six `Slice1PostgreSqlTests` could not execute
  because that existing fixture hard-codes Docker/Testcontainers; one unrelated financial-statement
  schema assertion failed. These are test-harness/unrelated regressions, not Feature 135 failures.
- Full backend solution regression: unit 1,723 passed; architecture 12 passed; integration 395
  passed, 45 failed, 40 skipped. The failures are in unrelated scanner/AI semantic routing,
  market-insight, Telegram-account, and existing schema expectations. Feature 135 changed no code
  in those areas.
- Migration/database verification: PASS against standalone local PostgreSQL; generated SQL remains
  `ALTER TABLE "Companies" ADD "ReportingType" integer;` with no default.
- Operational/provider smoke test: not run against live Noavaran; deterministic HTTP fixtures cover
  all supported and unsafe routing states.

## Blockers

None for Feature 135. The repository-wide integration suite retains 45 demonstrably unrelated
failures, and six legacy PostgreSQL tests are coupled to a Docker-only fixture; neither condition
blocks the verified Feature 135 implementation against the configured local PostgreSQL instance.

## Scope confirmation

- AI semantic routing: unchanged.
- Chart rendering contract: unchanged.
- Normalized monthly schema: unchanged.
- Unrelated architecture: no new monthly schema, duplicate provider client, or alternate routing
  architecture introduced.
