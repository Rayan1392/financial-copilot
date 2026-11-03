# Tasks

1. Add DTOs for balance-sheet, income-statement, and cash-flow requests and responses.
2. Define governed, reviewed item-ID mappings to canonical metrics and document units/scales.
3. Add bounded statement fetch methods with company, year, period, variant, and item filters.
4. Add `NadpcoApiFinancialStatementNormalizer` with distinct statement types, source-date
   mapping, provenance evidence, and idempotent upserts.
5. Publish recalculation requests after successful normalization.
6. Verify input readers remain provider-agnostic and do not prefer `CodalDb` by name.
7. Add tests for each statement type, bounded fetch parameters, period mapping, current governed
   metric persistence, idempotency, cross-provider coexistence, malformed payloads, and
   recalculation publication.

## Period-scoped admin extension (2026-10)

- [x] Preserve the existing generic `data-sync/financial-statements` route and request contract.
- [x] Add `POST /api/v1/admin/data-sync/financial-statements/by-period` under DataAdmin auth.
- [x] Resolve internal `Companies.Id` (`Guid`) and all-company selection through the existing
  `NoavaranEligibleCompanies` view; use `ExternalCompanyId` only for provider requests.
- [x] Validate Persian fiscal year and optional 3/6/9/12-month period.
- [x] Carry exact year and period in the queued request and make bounded provider requests for
  all four periods when period is null.
- [x] Add endpoint authorization/validation and exact-scope provider tests; run focused tests and
  regression suite. Focused tests pass; the full suites retain unrelated existing failures (see
  `ImplementationReport.md`).

## Implementation Status

Implemented.

- Added statement DTOs for the three NADPCO endpoints and bounded provider fetches that post
  company-id batches plus curated item allowlists.
- Added reviewed source item maps for income statement, balance sheet, and cash flow. The mapped
  codes are governed semantic metrics, including `OPERATING_CASH_FLOW`.
- Added `NadpcoApiFinancialStatementNormalizer` with distinct `FinancialStatementType` rows,
  Gregorian period mapping, Jalali/variant evidence, idempotent upserts, and recalculation
  request publication.
- Verified provider-agnostic input reading and cross-provider coexistence with `CodalDb`.
- Added unit coverage for statement types, item allowlists, period mapping, audited selection,
  evidence, idempotency, malformed payloads, and recalculation routing.

Original `040` delivery limitations, now superseded by `082`:

- The first `040` implementation posted curated `items` allowlists and persisted only governed
  mapped line items into `FinancialStatementLineItems`.
- The first `040` implementation applied
  `NadpcoApiStatementSelectionPolicy.SelectAll(...)` before persistence, so same-period
  standalone and consolidated statements did not both survive into normalized storage.
- Those gaps are now implemented by
  `082-noavaran-financial-statement-full-item-and-variant-persistence`, which widened the request
  body to `items: []`, added full vendor-item persistence through a shared source-item catalog and
  persisted mapping table, and kept statement variants structurally separate.
