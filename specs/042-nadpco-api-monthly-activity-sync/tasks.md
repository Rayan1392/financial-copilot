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

Not implemented.

