# Tasks

1. Add DTOs and bounded fetch calls for product-sales and service-sales endpoints.
2. Reuse the shared Jalali-to-Gregorian month resolver.
3. Audit the normalized monthly model against both payloads and document whether a migration is
   required for service-sales facts or additional evidence fields.
4. Add `NadpcoApiMonthlyActivityNormalizer` for product and service activity with stable
   external report and line-item keys.
5. Preserve unsupported vendor fields as evidence until a governed normalized column is
   justified.
6. Ensure monthly aggregation remains provider-agnostic and publish recalculation requests.
7. Add tests for product rows, service rows, zero activity, date conversion, missing product
   IDs, idempotency, cross-provider coexistence, aggregation, and recalculation publication.

## Implementation Status

Completed on 2026-06-03.

- Added bounded NADPCO product-sales and service-sales fetch requests with company id,
  Jalali date bounds, and optional output type.
- Added shared Jalali month resolution and reused it from CodalDB and NADPCO monthly
  normalizers.
- Audited the monthly schema: no migration is required because service-sales facts fit
  `SalesQuantity`/`SalesAmount`; unsupported vendor fields are retained in evidence JSON.
- Added `NadpcoApiMonthlyActivityNormalizer` with idempotent report/line-item upserts,
  zero-period retention, natural-key fallback for missing product/service ids, and existing
  recalculation publication through the data-sync processor.

