# Task 6 — Common Evaluation-Period Selection

Implemented a provider-neutral, deterministic common-period policy for the sales-growth scanner.

## Delivered

- Added `SalesGrowthScannerOptions` with the governed defaults from the task specification.
- Added startup validation for the comparison baseline, coverage percentage, and page-size bounds.
- Added validated monthly-period and normalized observation contracts.
- Added `SalesGrowthCommonEvaluationPeriodSelector`, which:
  - considers complete observations only;
  - counts distinct eligible symbols per period;
  - selects the newest period meeting minimum coverage;
  - records target period, numerator, denominator, coverage percentage, mixed-period policy, and policy version;
  - returns explicit `Partial` or `Unavailable` status instead of silently changing period policy.
- Registered options validation and the selector in infrastructure DI.
- Added API configuration under `SalesGrowthScanner`.
- Added unit coverage for newest qualifying period selection, deduplication/completeness, partial and unavailable outcomes, mixed-period disclosure, and invalid options.

Validation command:

```text
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~SalesGrowthCommonEvaluationPeriodSelectorTests|FullyQualifiedName~SalesGrowthScannerPlanTests" --no-restore
```

Result: 24 tests passed.
