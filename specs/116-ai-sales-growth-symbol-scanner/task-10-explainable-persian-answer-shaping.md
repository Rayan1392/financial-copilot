# Task 10 — Explainable Persian Answer Shaping

Implemented deterministic Feature 116 answer framing from validated scanner plan and result facts.

## Delivered

- Added a governed sales-growth filter chip that exposes comparison semantics, operator, threshold, origin, and inferred-default reason.
- Added sales-growth metric evidence with calculation policy version, actual growth value, formatting, period type, and observation timestamp.
- Added evidence-backed data citations for monthly-sales observations, source names, periods, and timestamps.
- Added deterministic Persian explanation text covering:
  - interpreted comparison and threshold;
  - target period;
  - inferred default-baseline disclosure;
  - common-period coverage and selection status;
  - mixed-period and freshness/data warnings;
  - empty results and unavailable/partial ranking status.
- Sales-growth plans do not invoke the optional LLM explanation generator, so validated values, periods, thresholds, and comparison semantics cannot be changed by prose generation.
- Generic scanner plans retain their existing optional explanation-generator behavior.
- No investment-advice language is introduced.

Validation:

```text
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~SalesGrowth|FullyQualifiedName~ExplainableAnswerBuilderTests" --no-restore
dotnet build src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --configuration Release --no-restore
```

Result: 72 tests passed; API build succeeded with zero warnings and errors.
