# Financial Statements By-Period Synchronization Implementation Report

## Feature and specification

- Existing feature: `040-nadpco-api-financial-statement-sync` — this is the acquisition and
  normalization feature. Full-item persistence and statement-variant guarantees remain owned by
  `082-noavaran-financial-statement-full-item-and-variant-persistence`.
- Updated documents: `user-story.md`, `tasks.md`, `specs/README.md`.

## API contract

- New route: `POST /api/v1/admin/data-sync/financial-statements/by-period`.
- Authorization: existing `DataAdmin` policy and authenticated-actor rate limit.
- Request:

  ```json
  {
    "companyId": null,
    "fiscalYear": 1405,
    "period": null,
    "idempotencyKey": null
  }
  ```

- `companyId` is nullable `Guid`, matching `Companies.Id`. It is optional; null selects all
  eligible companies. `fiscalYear` is required and validated from 1400 through 1500. `period`
  is null or 3, 6, 9, or 12. `idempotencyKey` is optional.
- Response is `202 Accepted` with a logical request ID, status (`Queued`, `QueuedWithFailures`,
  or `Failed`), eligible/queued/failed company counts, and failed internal company IDs. Per-run
  processing status remains available through existing data-sync run/activity monitoring.
- The existing `POST /api/v1/admin/data-sync/financial-statements` route and its
  `externalReference` / `idempotencyKey` / `providerName` request remain unchanged.

## Resolution and filtering

- Company IDs are resolved through `NoavaranEligibleCompanies`. The view's `Id` is the internal
  company `Guid`; its numeric `ExternalCompanyId` is sent as the Noavaran provider `companyIds`
  value. A requested company absent from the eligible view returns 404.
- Noavaran supports provider-side `fromYear`, `toYear`, and `perTId` filters. The worker sets
  both year bounds to the requested Jalali fiscal year and sends `perTId` for the selected period.
  A null period makes four bounded provider requests (3, 6, 9, and 12 months) across the three
  statement endpoints. Returned responses are combined by statement type before being persisted.
- The existing normalizer and raw-payload store handle persistence. The normalizer retains vendor
  fiscal-year-end evidence and existing Gregorian fiscal-period mapping. Existing idempotency,
  correction, full-item, and variant behavior remains in place. No migration or new sync model was
  added.
- The API queues one request per eligible company through the existing data-sync publisher. A
  company enqueue failure is recorded and does not stop other companies.

## Changed production files

- `AdminDataOperationsContracts.cs`
- `AdminDataOperationsController.cs`
- `FinancialIngestionContracts.cs`
- `FinancialProviderContracts.cs`
- `FinancialDataSyncProcessor.cs`
- `NadpcoApiDataProviderClient.cs`

## Tests and verification

- Added unit coverage that verifies exact Jalali year bounds and all four provider period filters.
- Added integration coverage for DataAdmin authorization and invalid-period rejection.
- `dotnet build src/backend/FinancialCopilot.sln --no-restore -m:1` — passed with 0 warnings and
  0 errors.
- Focused unit tests:
  `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --no-restore --filter FullyQualifiedName~NadpcoApiProviderTests -m:1`
  — passed, 42 tests.
- Focused integration tests:
  `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~FinancialStatementsByPeriod -m:1`
  — passed, 2 tests.
- Full unit suite — 2,150 passed and 21 failed. Failures include unrelated Feature 128 semantic
  routing, semantic dialogue governance, structured-query catalog authorization, and source
  planner assertions.
- Full `AdminDataOperationsEndpointTests` class — 61 passed and 7 failed. The failures include
  existing endpoint assertions receiving empty responses and `TooManyRequests` in the shared
  test fixture. The by-period tests pass when run as the focused filter above.
- `git diff --check` — passed (Git reported line-ending normalization notices).

## Remaining blocker

The feature changes build and the new focused tests pass. The complete regression suites are not
green because of the unrelated failures listed above; rerun those after resolving the existing
suite/test-fixture issues. Successful company-to-provider-ID resolution is implemented against
the eligible-company view, but no live provider or development database was used for this run.
