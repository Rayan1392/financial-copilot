# company-name-resolution-pe-lookup-regressions

Date: 2026-06-18
Status: RCA Reopened + Parser fallback fixed in current working tree

## Title

Company-name resolution regression for PE lookup and empty table/confidence handling.

## Observed Behavior

Reproduction conversation:

1. Query: `نسبت پی به ای چادرملو؟`
   - Response says `نسبت P/E چادرملو در داده‌ای که من الان در دسترس دارم پیدا نشد...`
   - Table columns render: `نماد | شرکت | آخرین قیمت | تغییر روزانه % | PE_TTM`
   - Table has no rows.
   - Confidence: `10%`.

2. Query: `نسبت پی به ای کچاد؟`
   - Response says `نسبت پی به ای نماد کچاد برابر است با 9.73.`
   - Table row renders: `کچاد | معدنی و صنعتی چادرملو | — | Missing | 9.73`
   - Confidence: `95%`.

## Expected Behavior

- Company names must resolve through the existing `Companies` table just like symbols.
- `چادرملو` must resolve to `کچاد` when `Companies.Name = معدنی و صنعتی چادرملو` and `Companies.Ticker/TseSymbol/CompanySymbol = کچاد`.
- `نسبت پی به ای چادرملو؟` must execute the normal `SymbolLookup` path and return the same `PE_TTM = 9.73` result as `نسبت پی به ای کچاد؟`.
- Empty tables must not be rendered. Either return a populated table or return no table.
- Missing-data prose must not be shown when the entity resolves and the requested metric exists.
- Confidence must reflect successful resolution and metric availability.

## Affected Queries

- `نسبت پی به ای چادرملو؟`
- `نسبت P/E چادرملو؟`
- `P/E چادرملو`
- `نسبت پی به ای کچاد؟`
- `نسبت پی به ای گل گهر؟`
- `نسبت پی به ای پالایش نفت اصفهان؟`

## Route Trace

Expected route:

```text
POST /api/ai/v1/query
  -> MicrosoftAgentFrameworkV2
  -> direct metric preflight for PE/P-E question
  -> SymbolLookupToolAdapter
  -> LlmSymbolLookupParser extracts symbol/company phrase + PE_TTM
  -> EfCoreSymbolMetricLookupService resolves symbol/company via Companies
  -> DerivedMetrics lookup for PE_TTM by ExternalCompanyId
  -> SymbolLookupTableResult with populated row
  -> AnswerConsistencyServices / SymbolLookupProseBuilder grounded prose
  -> ConfidenceScoringService high confidence for populated metric
```

Observed route to confirm:

```text
company-name query
  -> PE direct metric path was attempted
  -> parser likely extracted "چادرملو" as symbolName
  -> lookup service did not match partial Companies.Name
  -> empty SymbolLookupTableResult was created
  -> missing-data prose and low confidence were emitted
```

## Root-Cause Investigation Checklist

### A. Company Name Resolution

- [ ] Confirm parser output for `نسبت پی به ای چادرملو؟`.
- [ ] Confirm whether parser returns `symbolName = چادرملو` and `metricTerm = نسبت پی به ای`.
- [ ] Trace symbol/company resolution in `EfCoreSymbolMetricLookupService`.
- [ ] Verify Companies lookup checks `Ticker`, `TseSymbol`, `CompanySymbol`, and exact `Name`.
- [ ] Verify whether partial company-name matching against `Companies.Name` is missing.
- [ ] Verify row exists with `Name = معدنی و صنعتی چادرملو` and ticker/symbol `کچاد`.
- [ ] Confirm resolved `ExternalCompanyId = 3` for `چادرملو`.

### B. Empty Table Rendering

- [ ] Trace `SymbolLookupTableResult` creation for unresolved entities.
- [ ] Confirm endpoint payload includes a table object with columns and zero rows.
- [ ] Confirm frontend renders table whenever `symbolLookupTable` exists, even with zero rows.
- [ ] Decide whether backend should suppress empty lookup tables, frontend should not render zero-row tables, or both.

### C. Text/Table Contradiction

- [ ] Determine whether lookup failed due to symbol resolution or missing metric data.
- [ ] Confirm whether prose generation receives the same table data as renderer.
- [ ] Ensure missing-data prose is only used when a resolved entity lacks a requested metric.
- [ ] Ensure resolution failures produce clarification/no-table behavior instead of data-missing claims.

### D. Confidence Bug

- [ ] Identify confidence source for failed `چادرملو` lookup.
- [ ] Confirm low confidence comes from empty table / missing data warnings.
- [ ] Verify successful company-name lookup produces the same confidence class as symbol lookup.
- [ ] Verify failed lookup does not display misleading structured confidence.

### E. Quote Context Bug

- [ ] Confirm whether `LATEST_PRICE` and `DAILY_CHANGE_PCT` are actually unavailable for `کچاد`.
- [ ] Confirm why PE lookup includes quote columns by default.
- [ ] Determine whether quote columns should remain for valuation metrics when unavailable.
- [ ] Check whether quote-column behavior is already tracked by another bug or product decision.

### F. Missing Data Message Bug

- [ ] Confirm current message says PE data is missing when the actual failure is entity resolution.
- [ ] Decide correct product behavior for unresolved company names: resolve via Companies, or ask clarification if ambiguous.
- [ ] Ensure the final message never claims `PE_TTM` is missing when `ExternalCompanyId = 3` has `PE_TTM = 9.73`.

## Affected Classes/Files

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Adapters/SymbolLookupToolAdapter.cs`
- `src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs`
- `src/backend/FinancialCopilot.Application/Scanner/AnswerConsistencyServices.cs`
- `src/backend/FinancialCopilot.Application/Scanner/SymbolLookupProseBuilder.cs`
- `src/frontend/src/lib/chat.functions.ts`
- `src/frontend/src/components/app/message-list.tsx`
- `src/frontend/src/components/scanner/scanner-result-table.tsx`
- `tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs`
- `tests/FinancialCopilot.IntegrationTests/SymbolLookupEndpointTests.cs`
- `tests/FinancialCopilot.UnitTests/SymbolLookupParserTests.cs`

## Acceptance Criteria

- `نسبت پی به ای چادرملو؟` resolves to `کچاد`.
- `نسبت پی به ای چادرملو؟` returns `PE_TTM = 9.73` when current DB/test fixture contains that value.
- `نسبت پی به ای چادرملو؟` returns the same symbol/company row as `نسبت پی به ای کچاد؟`.
- Successful company-name lookup confidence is greater than zero and comparable to symbol lookup.
- Failed lookup responses do not render empty tables.
- Failed lookup responses do not claim metric data is missing when the failure is unresolved/ambiguous entity resolution.
- Existing symbol lookup, monthly-sales lookup, production/sales column rules, CyclicalWaves routes, and Noavaran routes remain unchanged.

## Regression Tests

- PE lookup by symbol:
  - Query: `نسبت پی به ای کچاد؟`
  - Expected: `SymbolLookup`, row for `کچاد`, `PE_TTM = 9.73`, confidence > 0.
- PE lookup by company name:
  - Query: `نسبت پی به ای چادرملو؟`
  - Expected: resolves to `کچاد`, same `PE_TTM = 9.73`, confidence > 0.
- Company name resolution:
  - Match against `Companies.Name`.
  - Match against `CompanySymbol`.
  - Match against `TseSymbol`.
- Empty table rendering:
  - Failed lookup response must not include/render a zero-row table.
- Confidence:
  - Successful company-name lookup confidence > 0.
  - Successful symbol lookup confidence > 0.
  - Failed lookup must not be displayed as a misleading populated confidence result.
- Quote columns:
  - Verify current intended PE behavior for quote columns remains unchanged unless product decision changes.

## Implementation Plan

1. Add failing regression tests for PE lookup by `چادرملو` and existing lookup by `کچاد`.
2. Add a failed-lookup regression proving zero-row tables are omitted from API/frontend rendering.
3. Inspect parser output and `EfCoreSymbolMetricLookupService` resolution rules.
4. Extend existing Companies-based resolution to match company-name substrings safely, without adding a new lookup pipeline.
5. Prefer exact symbol fields first, then exact normalized company name, then unambiguous normalized company-name contains match.
6. Return normal `SymbolLookup` table/prose/confidence when company-name resolution succeeds.
7. Suppress empty lookup tables at the response/frontend boundary.
8. Validate focused backend tests, frontend table rendering tests, and build.

## Confirmed RCA

- Parser/tool routing was not the primary failure. The PE lookup path was attempted and the parsed entity text could reach the symbol metric lookup service.
- The entity resolver was the primary failure. `CompanyResolverService.ResolveBySymbolAsync` checked `Companies.Name` only by exact normalized equality, so `چادرملو` did not match the full company name `معدنی و صنعتی چادرملو`.
- When resolution failed, `EfCoreSymbolMetricLookupService.BuildEmptyResult` returned a `SymbolLookupTableResult` with columns and zero rows.
- `AiFacadeController.MapSymbolLookupTable` mapped that zero-row table into the endpoint payload, and the frontend table component rendered it because it only checked whether a table object existed.
- Low confidence came from the missing-data/empty lookup path, not from a successful PE lookup. Once `چادرملو` resolves to `کچاد`, the normal pre-calculated `PE_TTM` confidence path returns high confidence.
- Quote columns are currently intentional for PE/valuation lookups through market-context expansion. Missing quote values are separate from the company-name resolution bug and were not removed in this fix.

## RCA Update After Live-Path Check

The first fix attempt changed the shared `CompanyResolverService`, but the live AI behavior still proved that query-time PE questions could bypass the deterministic direct metric preflight. The missing query-time piece was in `FinancialCopilotWorkflowDefinition.ContainsDirectMetricTerm`: it recognized English `P/E`/`pe`, but not the Persian PE phrase family:

- `نسبت پی به ای`
- `پی به ای`
- `نسبت قیمت به سود`
- `قیمت به سود`

Because of that, `نسبت پی به ای چادرملو؟` could still depend on the outer LLM tool-selection path instead of deterministically calling `lookup_symbol_metrics`. The final fix therefore covers both layers:

1. V2 direct metric preflight recognizes Persian PE aliases and calls `lookup_symbol_metrics` directly.
2. The query-time lookup service uses `CompanyResolverService`.
3. `CompanyResolverService` first checks exact symbol fields (`Ticker`, `TseSymbol`, `CompanySymbol`, identifier fields), then falls back to unambiguous `Companies.Name` contains matching.
4. On a unique name match, the resolved row supplies `ExternalCompanyId` for metrics and `TseSymbol`/company row display for the table.

## Fix Notes

- `CompanyResolverService` now keeps existing exact symbol/identifier precedence and adds a final unambiguous company-name fragment fallback.
- `ResolvedCompany` now exposes `TseSymbol` and `CompanySymbol` so resolver tests can assert the query-time resolved tradable symbol.
- Ambiguous company-name fragments return `null` rather than guessing.
- `FinancialCopilotWorkflowDefinition` now treats Persian PE aliases as direct metric lookup terms.
- The API omits zero-row symbol lookup tables from responses and suppresses confidence for those empty symbol lookup payloads.
- The frontend also normalizes/hides zero-row tables defensively.
- V2 PE endpoint tests now prove both `نسبت پی به ای چادرملو؟` and `نسبت پی به ای کچاد؟` route through deterministic preflight, do not invoke outer LLM tool-selection, resolve to `کچاد`, and return `PE_TTM = 9.73`.

## RCA Update After 2026-06-18 User Retest

The user retested `نسبت پی به ای چادرملو؟` and still received the tool-summary style response:

```text
Found metric data for 0 symbol(s). 1 unresolved.
```

That showed one more weak point remained: even when V2 preflight recognizes the PE intent and calls `lookup_symbol_metrics`, `LlmSymbolLookupParser` still depended on the LLM structured parser to extract a usable `(symbolName, metricTerm)` pair. If that parser returned clarification/no pairs, the company resolver never had a chance to match `چادرملو` against `Companies.Name`.

Additional fix:

1. `LlmSymbolLookupParser` now has a narrow deterministic fallback for direct PE/P-E lookup phrases.
2. When the LLM parser returns clarification/no pairs, or only unresolvable metric pairs, the fallback extracts the company phrase from the original user message and returns `(چادرملو, PE_TTM)` for `نسبت پی به ای چادرملو؟`.
3. The fallback handles Persian PE aliases, `P/E`, and `price-to-earnings`, but deliberately does not implement broad symbol parsing for unrelated metrics.
4. New regression coverage forces the V2 fake parser to return clarification for `چادرملو`; the endpoint still resolves to `کچاد` and returns `PE_TTM = 9.73`.

## Validation Results

- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~CompanyResolverServiceTests" -m:1 --no-restore`
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~PeSymbolLookupRegressionTests|FullyQualifiedName~AiQuery_UnknownSymbol_DoesNotReturnEmptyLookupTable" -m:1 --no-restore`
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~V2SymbolLookupEndpointTests" -m:1 --no-restore`
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~V2MonthlySalesRoutingEndpointTests" -m:1 --no-restore`
- `npm test -- message-list.test.tsx`
- `dotnet build src/backend/FinancialCopilot.sln --configuration Release -m:1 --no-restore`
- `npm run build`

## Additional Validation After Parser Fallback

- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~SymbolLookupParserTests" -m:1 --no-restore`
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~V2AiQuery_PeLookupByCompanyName_WhenParserMissesPair_UsesDeterministicFallback|FullyQualifiedName~V2AiQuery_PeLookupByCompanyOrSymbol_RoutesThroughDirectPreflight" -m:1 --no-restore`
