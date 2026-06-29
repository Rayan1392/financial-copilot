# Bug: Duplicate MonthlyReportLineItems Due to Natural Key Hash Collision on CategoryId

**Date:** 2026-06-25  
**Severity:** High — downstream `CompanyProductRevenueMix` totals are doubled for affected products

---

## Observed Symptom

For `ExternalCompanyId=202`, `ProviderName=NoavaranCurrentApi`, `PeriodEnd=2026-06-21`, `OutputType=0`, the `MonthlyReportLineItems` table contains **duplicate rows** for the same logical product — same `Title`, same quantities, same amounts — but with two distinct `ProductCode` values (both in the `PRODUCT:NATURAL:*` namespace):

```
برگشت از فروش: برگشت از فروش  →  PRODUCT:NATURAL:3ae80e05a76090d7  (qty=0, amount=0)
برگشت از فروش: برگشت از فروش  →  PRODUCT:NATURAL:13aab39bb9fffef6  (qty=0, amount=0)

بستر کاتالیست ها               →  PRODUCT:NATURAL:2084766d389cbadf  (salesAmount=62413)
بستر کاتالیست ها               →  PRODUCT:NATURAL:5486e647ca58ae81  (salesAmount=62413)

ضدسایش ها                      →  PRODUCT:NATURAL:7cbfe6a5f8ddd893  (salesAmount=3036158)
ضدسایش ها                      →  PRODUCT:NATURAL:52ed87fb8f74c117  (salesAmount=3036158)
```

Each duplicated product has an identical row in every measurable dimension (title, unit, quantities, amounts) but a different `ProductCode`. This causes `CompanyProductRevenueMix` to count each product **twice**, producing doubled revenue figures.

---

## Root Cause

### Natural key formula

`BuildLineItemCode` in `NadpcoApiMonthlyActivityNormalizer.cs:356-371` falls back to a natural key hash when the Noavaran API does not supply a `vendorCode`:

```csharp
var naturalKey = string.Join("|", [title, category, unit, index.ToString(...)]);
return $"{prefix}:NATURAL:{HashShort(naturalKey)}";
```

The `category` component is resolved in `BuildProductItem` from category title metadata. Numeric
`CategoryID` must never be used as a distinguishing component for the natural key because it
splits one logical product into multiple `ProductCode` values when parent records differ only by
vendor category id.

Historical buggy form:

```csharp
var category = item.CategoryTitle ?? parent.CategoryTitle
             ?? categoryId?.ToString(CultureInfo.InvariantCulture);
```

### What the Noavaran API sends

The Noavaran API (v2 nested shape) returns **one top-level parent record per category** for a given company-month. Each parent record carries:
- A shared `activityId` (which becomes the `ExternalReportId` → same `MonthlyReport` row for all)
- A distinct `CategoryID` and `CategoryTitle` per parent
- Nested `productSales` items that themselves **do not carry `CategoryID`/`CategoryTitle`**

For example, the same logical product "بستر کاتالیست ها" appears in two parent records:
- Parent A: `categoryId=X`, `categoryTitle="بستر کاتالیست ها"` → `category="بستر کاتالیست ها"`
- Parent B: `categoryId=Y`, `categoryTitle="بستر کاتالیست ها"` (different numeric ID, same display string) → `category="بستر کاتالیست ها"`

When `categoryTitle` is present and identical, the hashes are the same and the upsert works correctly.

**But for products without a meaningful category** (e.g. "برگشت از فروش", "تخفیفات", "سایر"), `CategoryTitle` is null or empty on the nested item, so the fallback `categoryId?.ToString()` is used. Two parent records that share the same product title and unit but have **different `CategoryID` numeric values** produce two different `category` strings → two different natural-key hashes → **two separate `ProductCode` values for the same logical line item**.

### Why both survive the upsert

The upsert lookup at line 81-83 keys on `(MonthlyReportId, ProductCode)`. Since the two rows have different `ProductCode` values, neither is seen as a duplicate of the other. Both are inserted as new rows. The unique index on `(MonthlyReportId, ProductCode)` (defined in `FinancialIngestionConfigurations.cs:155`) only prevents the same `ProductCode` from appearing twice; it does not prevent the same logical product from appearing under two different codes.

---

## Impact Chain

```
Noavaran API
  └─ parent record A (categoryId=X, no vendorCode) → PRODUCT:NATURAL:hash(title|X|unit|idx)
  └─ parent record B (categoryId=Y, no vendorCode) → PRODUCT:NATURAL:hash(title|Y|unit|idx)
       ↓ both keyed to same MonthlyReport (shared activityId)
MonthlyReportLineItems: 2 rows for same logical product
       ↓
CompanyProductRevenueMixCalculator reads all line items for the report
       ↓
Revenue mix totals doubled for affected products
       ↓
Monthly sales charts and AI-reported revenue figures are incorrect
```

---

## Affected Components

| Component | File | Notes |
|-----------|------|-------|
| Natural key builder | `NadpcoApiMonthlyActivityNormalizer.cs:356-371` | `category` derived from `CategoryID` int when `CategoryTitle` is null |
| Product item builder | `NadpcoApiMonthlyActivityNormalizer.cs:208-264` | `categoryId` from nested item overrides parent only when non-null |
| Upsert loop | `NadpcoApiMonthlyActivityNormalizer.cs:79-102` | Keys on `ProductCode`; does not detect same-title duplicates |
| Revenue mix calculator | `CompanyProductRevenueMixCalculator` | Sums all line items — doubles on duplicate rows |
| Trend snapshot calculator | `CompanyMonthlyActivityTrendSnapshotBackfillService` | Same risk if it sums line items |

---

## Fix Direction (not implemented here)

The natural key should not include `CategoryID` (the numeric database/vendor ID) as a distinguishing component for products that share the same human-readable title. Options:

1. **Never use `CategoryID.ToString()` in the natural key.** Prefer `CategoryTitle` when present;
   otherwise treat category as missing evidence rather than a logical identity component. If two
   items have the same `title`, `unit`, and `CategoryTitle` (or both have no category), they are
   the same product and should hash to the same code.

2. **Deduplicate by `(title, unit, categoryTitle)` before upsert** — after building all items for a report group, merge items with identical `(title, unit, categoryTitle)` by summing quantities and amounts, then upsert the merged set.

3. **Omit `category` from the natural key entirely** for items without a vendor code — use only `title|unit|index` (where index is global across all parent records for the same report, not per-parent-local).

Option 1 is lowest-risk and targets the root cause; options 2 and 3 add a correction layer on top.

---

## Reproduction

```sql
-- Find the MonthlyReport
SELECT "Id" FROM "MonthlyReports"
WHERE "ExternalCompanyId" = '202'
  AND "ProviderName" = 'NoavaranCurrentApi'
  AND "PeriodEnd" = '2026-06-21'
  AND "OutputType" = '0';

-- Observe duplicate titles in line items
SELECT "Title", COUNT(*) AS cnt, SUM("SalesAmount") AS total
FROM "MonthlyReportLineItems"
WHERE "MonthlyReportId" = '<id from above>'
GROUP BY "Title"
HAVING COUNT(*) > 1
ORDER BY cnt DESC;
```
