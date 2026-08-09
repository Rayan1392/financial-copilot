# Task 15 — Integration Tests

Implemented with `SalesGrowthScannerIntegrationTests` and `SalesGrowthScannerApiFactory`.

Coverage uses the production EF scanner execution service against a deterministic in-memory ingestion database. No external market-data provider is called.

- Persian-language scanner plan with positive growth and deterministic row ordering.
- YoY `>30%`, previous-month minimum `20%`, previous-year multiple `>=2x`, and average-12-month `>=1.5x` cases.
- Missing baseline periods, zero baseline, fewer-than-twelve observations, no matches, and common-period coverage below policy.
- Composition with a `PE_TTM` filter and exact financial-cell assertions.
- Existing integration coverage also verifies provider-neutral AI execution, billing usage persistence/idempotency, cache behavior, and conversation reload parity; Telegram/web renderers consume the same structured result contract.

Verification:

```text
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --filter FullyQualifiedName~SalesGrowthScannerIntegrationTests --no-restore
Passed: 7, Failed: 0
```
