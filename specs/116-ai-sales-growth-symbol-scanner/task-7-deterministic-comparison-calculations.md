# Task 7 — Deterministic Comparison Calculations

Implemented a provider-neutral monthly-sales comparison calculator over a fixed evidence snapshot.

## Reuse and governed gaps

- The existing `MONTHLY_SALES_GROWTH_MOM` and `MONTHLY_SALES_GROWTH_YOY` definitions remain the canonical MoM/YoY semantic policies (`mom-monthly-sales-v1` and `yoy-monthly-sales-v1`). The new calculator uses the same comparison periods and decimal percentage formula while exposing the additional scanner evidence required by Feature 116.
- Existing derived-metric rows are not sufficient for this feature because they do not return both raw observations, a growth multiple, explicit value states, and freshness/source evidence in one provider-neutral result.
- The existing average snapshot calculation has a different window shape. Feature 116 therefore uses a governed `AveragePrevious12Months` calculation policy whose window is exactly the twelve periods before the target period; the current period is excluded.

## Delivered

- Added `ISalesGrowthComparisonCalculator` and `SalesGrowthComparisonCalculator`.
- Returns current value/period, baseline value/period or window, difference, percentage, multiple, value states, source/evidence records, latest observation timestamp/source, and calculation policy versions.
- Missing, negative, duplicate, and non-positive baseline inputs are represented explicitly; they never produce fabricated growth or division-by-zero values.
- Zero current sales remains an observed valid value, while a non-positive baseline is unusable for growth calculation.
- Registered the calculator as a singleton provider-neutral application service.
- Added deterministic unit coverage for MoM, YoY, twelve-month average exclusion, invalid/missing inputs, duplicate periods, evidence, and repeatability.

Validation command:

```text
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~SalesGrowthComparisonCalculatorTests|FullyQualifiedName~SalesGrowthCommonEvaluationPeriodSelectorTests|FullyQualifiedName~SalesGrowthScannerPlanTests" --no-restore
```

Result: 32 tests passed.
