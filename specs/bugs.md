# Bugs

Defects found. Inspect the affected behavior before proposing a fix; fixes follow `specs/FEATURE_DEVELOPMENT_WORKFLOW.md` ("Bug Fixes and Urgent Hotfixes"). Detailed investigation notes may live in `specs/bugs/`; link them from the Summary.

| ID | Spec | Summary | Steps | Expected | Actual | Status |
|---|---|---|---|---|---|---|
| BUG-001 | Several (075, 088, 128, 141, 143, 150, 152 and others) | 29 backend unit-test methods fail consistently on `develop` (list in `specs/test-baseline.md`). | `dotnet test tests/FinancialCopilot.UnitTests --configuration Release` at `886f05e` | All unit tests pass | 3,065 passed, 32 failed (29 methods), same set in 2 runs (2026-11-09) | Open — fix needs Gate A/B per affected feature |
| BUG-002 | Frontend (several features) | 37 TypeScript errors in 17 files of `src/frontend` (list in `specs/test-baseline.md`). | In `src/frontend`: `npm run typecheck` at `886f05e` | No type errors | 37 errors: 24 in test files, 13 in application files (2026-11-09) | Open — fix needs Gate A/B |
| BUG-003 | 154 / architecture tests | `CleanArchitectureDependencyTests.MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction` fails: `SemanticCapabilityExecutors.cs` uses `FinancialStatementTableIntentRules`. Found during Feature 156; that file was not changed by it. | `dotnet test tests/FinancialCopilot.ArchitectureTests --configuration Release` | Test passes, or its rule is updated | Fails on `var quarterly = FinancialStatementTableIntentRules…` | Open |
