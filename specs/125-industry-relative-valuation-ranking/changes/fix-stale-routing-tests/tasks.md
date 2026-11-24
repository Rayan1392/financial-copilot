# Fix BUG-047

Status: Gate B: Approved by the owner on 2026-11-24

- [x] Update the six `IndustryComparisonRoutingEndpointTests` cases to the current comparison answer (shared request helper `IndustryComparisonQuery`).
- [x] Add `IndustryComparisonRetiredEndpointTests` with `IndustryComparisonRetiredApiFactory` (availability with `cyclicalWavesV2RelativeValuationEnabled: false`).
- [x] Run `dotnet test tests/FinancialCopilot.IntegrationTests --filter "FullyQualifiedName~IndustryComparison"`: 10 passed, 0 failed (6 routing cases, the retired case, the follow-up endpoint tests). Mark BUG-047 Fixed.
