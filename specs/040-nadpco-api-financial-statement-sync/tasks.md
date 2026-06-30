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

Known limitation documented after implementation:

- Current code posts curated `items` allowlists to the vendor and persists only governed mapped
  line items into `FinancialStatementLineItems`.
- Current code applies `NadpcoApiStatementSelectionPolicy.SelectAll(...)` before persistence, so
  same-period standalone and consolidated statements do not both survive into normalized storage.
- These limitations are intentionally addressed by
  `082-noavaran-financial-statement-full-item-and-variant-persistence`, not retroactively folded
  into the original scope of spec `040`.
