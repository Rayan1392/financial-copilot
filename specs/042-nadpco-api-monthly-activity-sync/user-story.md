# NADPCO API Monthly Activity Synchronization

## User Story

As a scanner user, I want NADPCO product-sales and service-sales activity synchronized into
normalized PostgreSQL monthly data so monthly revenue and growth analysis covers both
manufacturing and service companies.

## Source Endpoints

```http
POST /api/v2/MonthlyActivity/ProductSales
POST /api/v3/MonthlyActivity/ServiceSales
```

Product-sales requests accept bounded company IDs, Jalali date bounds, and output type.
Service-sales requests accept bounded company IDs and Jalali date bounds. Payloads include
company identity, industry context, instrument code, month/year, fiscal date, and product or
service activity details.

## Access Constraint (Monthly Activity)

Our Noavaran current-API credentials are authorized for monthly product/service activity only from
Shamsi **1404 onward**. Requesting Shamsi **1403 or earlier** is not permitted and the vendor
endpoints respond with HTTP 500. Therefore:

- The monthly-activity request start date must never be earlier than `1404/01/01`. The configured
  `MonthlyActivityFromDate` default is `1404/01/01`, and the provider client clamps any
  earlier-than-permitted value up to `1404/01/01` so a misconfiguration cannot reintroduce the
  permission failure.
- Monthly data for periods before Shamsi 1404 must come from the **archive** source
  (`NoavaranArchiveSql`), not the current API. This is the archive-vs-current source boundary from
  spec 051; the monthly start year is therefore later than the statement/fundamental-index start
  years, which remain permitted further back.

## Acceptance Criteria

1. Fetch product and service activity in bounded company/date batches and store raw responses
   before normalization.
2. Normalize Jalali activity months to Gregorian period windows with the shared calendar
   resolver.
3. Monthly report identity is canonical per logical report. For `ProductSales` and
   `ServiceSales`, `MonthlyReports.ExternalReportId` must never include `categoryId`,
   category title, industry, or other line-item grouping metadata. Category data is evidence on
   line items only, not part of report identity.
4. Reuse normalized monthly reports where the existing model is sufficient. Extend the model
   only when service-sales facts cannot be represented without data loss.
5. Preserve product/service title, unit, quantity, rate, value, output type, category, and
   publication metadata as normalized fields or provenance evidence.
6. Retain valid zero-activity periods.
7. Aggregate monthly sales provider-agnostically and publish recalculation requests so existing
   monthly growth metrics continue to work.
8. Keep upserts idempotent and ensure `NadpcoApi` rows coexist with `CodalDb` monthly rows.

## Out Of Scope

- Query-time remote calls.
- Fabricating product IDs where the vendor omits them.
- Replacing the deterministic monthly-growth engine.
