# Financial Statement Table Persian Line-Item Title Fallback

Date: 2026-07-21
Status: Fixed in current working tree

## Observed Behavior

Query:

```text
آخرین صورت سود و زیان فولاژ؟
```

The financial statement response used Noavaran Amin statement metadata, but the line-item title column rendered canonical metric codes such as `REVENUE`, `GROSS_PROFIT`, `NET_PROFIT`, `EPS`, and `FINANCE_COSTS` instead of Persian source item titles.

The source item identifier column also fell back to the same metric codes when the persisted source catalog link was missing.

## Expected Behavior

- Financial statement data must come from the configured Noavaran current API source.
- User-facing line-item titles must be Persian.
- Canonical `MetricCode` values may remain in the structured payload for internal traceability, but they must not be used as the primary display title when a Persian title can be inferred.
- Source item identifiers and metric codes must not be displayed in user-facing financial statement tables.

## Root Cause

`EfCoreFinancialStatementTableRepository.GetStatementLineItemsAsync` projected each line item title as:

```csharp
row.Catalog?.TitleFa ?? row.Catalog?.TitleEn ?? row.Item.MetricCode
```

Legacy Noavaran statement rows can have `MetricCode` values without a `SourceItemCatalogId`, especially for rows ingested before the source-item catalog migration. For those rows, both `TitleFa` and `TitleEn` were null, so the repository returned the canonical metric code as the display fallback.

The markdown renderer had a second fallback that used `MetricCode` in the source item identifier column when `SourceItemId` was null.

## Fix

- Added a Noavaran metric-code fallback map for the governed statement item codes already owned by `NadpcoApiStatementItemMaps`.
- The repository now infers Persian titles and source item ids for legacy metric-only rows before building `FinancialStatementTableLineItem`.
- The renderer no longer displays the source item identifier column in user-facing financial statement tables.

## Regression Coverage

- `Repository_UsesPersianFallbackTitlesForLegacyMetricOnlyLineItems`
- `Repository_UsesTranslatableSqlBeforeApplyingLegacyFallbackOrdering`
- `Renderer_DoesNotRenderSourceItemIdentifierColumn`

## Follow-up Runtime Fix

The first implementation used `InferSourceItemId` inside an EF Core `OrderBy`. That passed InMemory tests but failed against relational providers because EF Core cannot translate the helper method to SQL.

The repository now materializes the selected statement line items with `ToListAsync` first, then applies the fallback ordering in memory. A SQLite-backed regression test covers the relational translation boundary.

## Affected Files

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementTableServices.cs`
- `tests/FinancialCopilot.UnitTests/FinancialStatementTable083Tests.cs`
