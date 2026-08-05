# Task 9 — Structured Result Table

Implemented the Feature 116 structured result table on the existing Feature 008 scanner contract.

## Delivered

- Added governed sales-growth default columns:
  - symbol;
  - company;
  - latest monthly sales;
  - baseline sales column selected from previous month, same month previous year, or average previous 12 months;
  - growth percent;
  - sales multiple only when multiple semantics or an explicit sales-multiple display request requires it.
- Suppressed raw `MONTHLY_SALES_GROWTH_MOM`/`MONTHLY_SALES_GROWTH_YOY` display columns for governed Feature 116 plans in favor of the canonical growth-percent column.
- Preserved other explicit scanner conditions as columns while preventing automatic price, daily-change, valuation, market-cap, score, and debug columns.
- Added row metadata for current/baseline period or window, unit/scale, evidence observations, freshness/source, threshold/operator, origin, policy versions, and deterministic match reason.
- Added table metadata for target common period, coverage numerator/denominator/percentage, selection status/reason, policy versions, and mixed-period status.
- Extended API response contracts and mapping for the new metadata.
- Extended execution facts with eligible/evaluated counts and exclusion-by-reason counts.
- Kept missing values nullable and unavailable rather than converting absent data to zero.
- Added unit coverage for default columns, dynamic baseline columns, multiple semantics, explicit multiple requests, and forbidden automatic columns.

Validation:

```text
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~SalesGrowthStructuredResultTableTests|FullyQualifiedName~SalesGrowth" --no-restore
dotnet build src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --configuration Release --no-restore
```

Result: 62 tests passed; API build succeeded with zero warnings and errors.
