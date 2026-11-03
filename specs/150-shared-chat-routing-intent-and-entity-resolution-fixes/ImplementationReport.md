# Feature 150 Implementation Report

Date: 2026-11-01

## Verdict

**Code complete and Feature 150 regression tests pass.** The exact wording checks for the six routing defects and the Feature 148 integration acceptance suite pass. The repository-wide unit and architecture suites still contain known failures outside this feature; they are listed separately below. Tests use scripted model responses and do not establish real-model behavior.

## Changes

- R1: Guarded `periodCount` JSON parsing by JSON value kind so schema-valid `null` remains a null proposal field.
- R2: Excluded financial and period vocabulary from deterministic company/entity extraction. Quarterly net-profit requests now route to an income-statement table, and quarterly company extraction removes the metric words.
- R3: Added Persian and English “less than” / “greater than” phrases to threshold precedence.
- R4: Prevented generic growth/comparison vocabulary from being mistaken for a named product. Exact monthly-sales-growth screening now routes to the screener. Its unspecified comparison period continues to use the existing same-month-prior-year policy.
- R5: Infer income statement for quarterly net-profit trends, resolve the four-quarter window, and extract the company separately from the metric phrase.
- R6: The MAF symbol-lookup path returns the existing provider-unavailable response for known-symbol product production requests in CyclicalWaves mode. The workflow preserves that unsupported outcome instead of asking for the symbol again.
- Updated Feature 148 characterization tests to assert exact-phrase behavior and the updated unsupported-capability response. Added focused Feature 150 regression tests.

## Verification

| Command / scope | Result |
|---|---:|
| `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~Feature150SharedChatRoutingTests|FullyQualifiedName~SymbolLookupToolAdapterTests' --verbosity minimal` | 12 passed, 0 failed |
| `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~Feature148' --verbosity minimal` | 28 passed, 0 failed; exact acceptance wording included |
| Full `FinancialCopilot.UnitTests` project | 2,769 passed, 20 failed, 0 skipped (baseline failures; see below) |
| `dotnet test tests/FinancialCopilot.ArchitectureTests/FinancialCopilot.ArchitectureTests.csproj --no-restore --verbosity minimal` | 14 passed, 1 failed (baseline failure; see below) |

The full unit suite's 20 failures are outside the Feature 150 changes. They include financial source planner/catalog expectations, Feature 141 developer-UAT authorization, Feature 128 semantic-routing expectations, catalog snapshot drift, scanner/metric fixture expectations, and test-data identity assumptions. The one architecture failure is `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`, whose committed `SemanticCapabilityExecutors.cs` already references `FinancialStatementTableIntentRules`. Neither that production file nor the architecture test was changed for Feature 150. No production or repository state was reverted to manufacture a baseline; these are identified by scope and unchanged tracked files.

## Affected Feature 150 Files

- `src/backend/FinancialCopilot.Application/AI/Orchestration/CapabilityInterpretationGovernance.cs`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/DeterministicCapabilityInterpreter.cs`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/FinancialStatementTableIntentRules.cs`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/MonthlyProductTrendIntentRules.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Adapters/SymbolLookupToolAdapter.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/FinancialCopilotAgentWorkflowRunner.cs`
- `tests/FinancialCopilot.UnitTests/Feature150SharedChatRoutingTests.cs`
- `tests/FinancialCopilot.UnitTests/SymbolLookupToolAdapterTests.cs`
- `tests/FinancialCopilot.IntegrationTests/Feature148ChatProviderE2ETests.cs`

## Limits

The 28 integration tests use local fixtures and a scripted AI model. Real LLM, isolated-database full import, scheduled-worker execution, and live Telegram operations belong to Stage 2 and have not been claimed as verified here.
