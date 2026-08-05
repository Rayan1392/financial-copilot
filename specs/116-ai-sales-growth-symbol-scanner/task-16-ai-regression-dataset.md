# Task 16 — AI Regression Dataset

Implemented the versioned `feature-116-sales-growth-regression` dataset in the shared AI evaluation framework.

The 12 golden/adversarial utterances cover:

- omitted `لیست` discovery wording and colloquial `کدوما فروششون بهتر شده؟`;
- mixed Persian/English threshold phrasing and punctuation/spacing variants;
- ambiguous baseline-only wording and single-symbol lookup counterexamples;
- trend, product-mix, and net-profit counterexamples;
- SQL/prompt-injection wording, with routing expectations remaining provider-neutral;
- previous-month and average-12-month multiple semantics.

Each case records expected intent, routing target, clarification behavior, and governed sales-growth baseline/threshold parameters where applicable. Tests use a fake AI execution service for the ambiguous fallback path.

Verification:

```text
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --filter "FullyQualifiedName~SalesGrowth" --no-restore
Passed: 85, Failed: 0
```
