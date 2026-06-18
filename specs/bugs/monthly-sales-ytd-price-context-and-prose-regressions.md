# monthly-sales-ytd-price-context-and-prose-regressions

Date: 2026-06-18
Status: RCA registered before production-code changes
Scope: Microsoft Agent Framework V2 symbol lookup, monthly-sales companions, YTD follow-ups, quote context, deterministic prose

## Observed Behavior

1. `فروش ماهان کچاد چقدر است؟` returns the expected compact monthly-sales table:
   - `MONTHLY_SALES = 90,879,722`
   - `AVG_12M_MONTHLY_SALES = 57,549,287`
   - `MONTHLY_SALES_YTD = 787,016,400`
   - `MONTHLY_SALES_YTD_PREVIOUS_MONTH = 605,344,668`
   - confidence: 94%
2. `متوسط فروش 12 ماهه کچاد چقدر بوده است` incorrectly answers with the latest monthly sales sentence:
   - actual text: `آخرین فروش ماهانه کچاد برابر با 90,879,722 میلیون ریال است.`
   - requested metric: `AVG_12M_MONTHLY_SALES`
   - expected value: `57,549,287 میلیون ریال`
3. `فروش YTD چقدر بوده؟` asks for a symbol even though the previous conversation context was `کچاد`.
4. Follow-up `چادرملو` returns `415,830,370 میلیون ریال`, while the earlier monthly-sales table showed `MONTHLY_SALES_YTD = 787,016,400`.
5. `فروش YTD تا ماه قبل کچاد؟` returns `324,950,648 میلیون ریال`, while the earlier monthly-sales table showed `MONTHLY_SALES_YTD_PREVIOUS_MONTH = 605,344,668`.
6. YTD answers show confidence 0% despite returning numeric values.
7. Direct price questions such as `قیمت سهم کچاد؟` and `آخرین قیمت کچاد چقدر است؟` say no price tool exists, but `P/E کچاد` renders quote columns (`آخرین قیمت`, `تغییر روزانه %`) as Missing.
8. `P/E کچاد` prose says `نسبت نسبت پی به ای`, duplicating `نسبت`.

## Expected Behavior

- Direct monthly-sales companion questions must answer the requested metric, not always the base `MONTHLY_SALES`.
- `متوسط فروش 12 ماهه کچاد چقدر بوده است` must primarily answer `AVG_12M_MONTHLY_SALES = 57,549,287 میلیون ریال`.
- `فروش YTD کچاد؟` must primarily answer `MONTHLY_SALES_YTD = 787,016,400 میلیون ریال` when the current DB values below are present.
- `فروش YTD تا ماه قبل کچاد؟` must primarily answer `MONTHLY_SALES_YTD_PREVIOUS_MONTH = 605,344,668 میلیون ریال` when the current DB values below are present.
- Follow-up metric-only questions must use conversation context when a valid `conversationId` and prior symbol context exist; otherwise ask for only the missing symbol.
- Confidence must be greater than 0 when a numeric metric cell is returned from a supported lookup table.
- Production/sales answers must continue to omit market quote columns.
- Quote columns must remain valid for valuation, price, ratio, screening, and market-statistic questions, but direct price capability and missing quote rendering must be consistent.
- Persian PE prose must not duplicate `نسبت`.

## Affected Queries

- `فروش ماهان کچاد چقدر است؟`
- `متوسط فروش 12 ماهه کچاد چقدر بوده است`
- `فروش YTD چقدر بوده؟`
- `چادرملو`
- `فروش YTD تا ماه قبل کچاد؟`
- `قیمت سهم کچاد؟`
- `آخرین قیمت کچاد چقدر است؟`
- `P/E کچاد`

## Route And Path Trace

### Monthly-sales snapshot query

- Route/workflow mode: `MicrosoftAgentFrameworkV2`
- Intended intent: `SymbolLookup`
- Tool path: deterministic V2 direct metric preflight, equivalent to `lookup_symbol_metrics`
- Symbol: `کچاد`
- ExternalCompanyId: `3`
- Metric set: `MONTHLY_SALES`, `AVG_12M_MONTHLY_SALES`, `MONTHLY_SALES_YTD`, `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
- Repository/service: `SymbolLookupToolAdapter` -> `LlmSymbolLookupParser` -> `EfCoreSymbolMetricLookupService`
- Data source table: `DerivedMetrics`, joined with `Companies`
- Renderer/composer: `SymbolLookupProseBuilder` plus `SymbolLookupTableResult`
- Current behavior is correct for the broad monthly-sales snapshot.

### Average 12-month sales query

- Route/workflow mode: `MicrosoftAgentFrameworkV2`
- Intended intent: `SymbolLookup`
- Symbol: `کچاد`
- ExternalCompanyId: `3`
- Requested metric: `AVG_12M_MONTHLY_SALES`
- Confirmed issue: deterministic prose currently treats any available monthly-sales monetary cell as a monthly-sales snapshot, then `MonthlySalesSentence` reads the `MONTHLY_SALES` cell specifically. This can produce a correct table with incorrect leading prose for companion metrics.
- Relevant code:
  - `src/backend/FinancialCopilot.Application/Scanner/AnswerConsistencyServices.cs:37`
  - `src/backend/FinancialCopilot.Application/Scanner/AnswerConsistencyServices.cs:113`
  - `src/backend/FinancialCopilot.Application/Scanner/AnswerConsistencyServices.cs:146`

### YTD direct and follow-up queries

- Route/workflow mode: `MicrosoftAgentFrameworkV2`
- Intended intent: `SymbolLookup`
- Expected metric codes:
  - `فروش YTD` -> `MONTHLY_SALES_YTD`
  - `فروش YTD تا ماه قبل` -> `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
- Confirmed parser risk:
  - `ShouldForceMonthlySalesSnapshot` treats `فروش YTD` as a sales snapshot trigger and can force the term to `آخرین فروش`.
  - Existing parser coverage encodes `فروش YTD کچاد چقدر است؟` as generic `sales`, not as `MONTHLY_SALES_YTD`.
  - The semantic catalog has Persian aliases for YTD concepts, but not the exact mixed Persian/English user phrases `فروش YTD` and `فروش YTD تا ماه قبل`.
- Relevant code/tests:
  - `src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs:255`
  - `src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs:287`
  - `src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs:40`
  - `src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs:49`
  - `tests/FinancialCopilot.UnitTests/SymbolLookupParserTests.cs:89`

### Conversation context

- Route/workflow mode: `MicrosoftAgentFrameworkV2`
- Confirmed issue: direct metric preflight calls `lookupAdapter.LookupAsync(request.Message, ...)` with the raw current message. It does not pass the enriched message built from conversation memory.
- Consequence: a follow-up such as `فروش YTD چقدر بوده؟` has no symbol in the parser input and can ask for clarification even when `BuildEnrichedMessage` had prior context.
- A second possible cause remains: if the frontend omits `conversationId`, there is no prior conversation context to use.
- Relevant code:
  - `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:183`
  - `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:332`
  - `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:668`

### Price and quote behavior

- Route/workflow mode: `MicrosoftAgentFrameworkV2`
- Confirmed inconsistency:
  - Direct metric preflight does not include price phrases such as `قیمت سهم` or `آخرین قیمت` in `IsDirectMetricLookupRequest`.
  - `LATEST_PRICE` is registered as a source metric, but the catalog has no direct Persian aliases for price lookup in the scanned definition.
  - `EfCoreSymbolMetricLookupService` intentionally adds `LATEST_PRICE` and `DAILY_CHANGE_PCT` as market context for non-monthly metrics, so PE tables can include quote columns even when quote cells are Missing.
- Relevant code:
  - `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:468`
  - `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:586`
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs:101`
  - `src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs:138`

### PE prose duplication

- Confirmed issue: `ValueSentence` always prefixes Persian metric prose with `نسبت`, while `MetricDisplayNameResolver` can resolve `PE_TTM` to the Persian alias `نسبت پی به ای`.
- Result: `نسبت نسبت پی به ای ...`
- Relevant code:
  - `src/backend/FinancialCopilot.Application/Scanner/AnswerConsistencyServices.cs:90`
  - `src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs:257`

## Current DB Diagnostics

Run these diagnostics before implementation and after any fix:

```sql
SELECT "MetricCode", "PeriodType", "PeriodStart", "PeriodEnd", "Value", "Unit", "SourceEvidenceJson"
FROM public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
  AND "MetricCode" IN (
    'MONTHLY_SALES',
    'AVG_12M_MONTHLY_SALES',
    'MONTHLY_SALES_YTD',
    'MONTHLY_SALES_YTD_PREVIOUS_MONTH'
  )
ORDER BY "PeriodEnd" DESC, "MetricCode";
```

Current diagnostic snapshot for `ExternalCompanyId = '3'`:

| MetricCode | PeriodStart | PeriodEnd | Raw value | Display in million Rials |
|---|---:|---:|---:|---:|
| `MONTHLY_SALES` | 2026-05-01 | 2026-05-31 | `90,879,722,000,000` | `90,879,722` |
| `AVG_12M_MONTHLY_SALES` | 2026-05-01 | 2026-05-31 | `57,549,286,500,000` | `57,549,287` |
| `MONTHLY_SALES_YTD` | 2026-04-21 | 2026-05-21 | `787,016,400,000,000` | `787,016,400` |
| `MONTHLY_SALES_YTD_PREVIOUS_MONTH` | 2026-04-21 | 2026-05-21 | `605,344,668,000,000` | `605,344,668` |

The suspect returned values `415,830,370,000,000` and `324,950,648,000,000` were not found in `DerivedMetrics` for `ExternalCompanyId = '3'` and were not found as exact `DerivedMetrics.Value` matches across the checked database snapshot. That mismatch is not fully confirmed and needs an API payload/log trace for the observed conversation.

2026-06-18 implementation follow-up:

- The observed conversation was found in `Messages` with `ConversationId = 7a687692-7f60-46d9-a8dd-4da3d2016fe9`.
- The wrong values were present in persisted assistant message content:
  - `چادرملو` -> assistant content `415,830,370 میلیون ریال`
  - `فروش YTD تا ماه قبل کچاد؟` -> assistant content `324,950,648 میلیون ریال`
- The diagnostic query did not expose a structured `AssistantPayloadJson` for those rows, so table cells, backend confidence, and workflow tool trace were not recoverable from persisted message payloads.
- Because the wrong values are not present in current `DerivedMetrics`, no speculative source-path fix was implemented for those specific numbers. The implemented fixes address the confirmed routing/prose/context causes that allowed this class of answer to happen.

Additional diagnostics:

```sql
SELECT "ExternalCompanyId", "Ticker", "TseSymbol", "CompanySymbol", "Name"
FROM public."Companies"
WHERE "Name" LIKE '%چادرملو%'
   OR "Ticker" LIKE '%کچاد%'
   OR "TseSymbol" LIKE '%کچاد%'
ORDER BY "ExternalCompanyId";
```

```sql
SELECT "ExternalCompanyId", "MetricCode", "PeriodStart", "PeriodEnd", "Value", "Unit", "SourceEvidenceJson"
FROM public."DerivedMetrics"
WHERE "Value" IN (415830370000000, 324950648000000)
ORDER BY "ExternalCompanyId", "MetricCode", "PeriodEnd" DESC;
```

```sql
SELECT "ConversationId", "Role", "Content", "AssistantPayloadJson", "CreatedAt"
FROM public."Messages"
WHERE "Content" LIKE '%متوسط فروش 12 ماهه%'
   OR "Content" LIKE '%فروش YTD%'
   OR "Content" LIKE '%چادرملو%'
   OR "Content" LIKE '%آخرین قیمت کچاد%'
ORDER BY "CreatedAt" DESC
LIMIT 50;
```

## Root-Cause Investigation Checklist

- [x] Confirm V2 direct metric preflight is active for direct metric phrases.
- [x] Confirm production/sales table still omits quote columns for the successful monthly-sales snapshot.
- [x] Confirm current DB values for KCHAD monthly-sales companion metrics.
- [x] Confirm `AVG_12M_MONTHLY_SALES` can be present in the table while prose still selects `MONTHLY_SALES`.
- [x] Confirm `فروش YTD` parser coverage currently resolves through generic `sales`.
- [x] Confirm V2 direct metric preflight bypasses enriched conversation context.
- [x] Confirm PE prose duplication source.
- [ ] Capture the exact API response for the observed YTD wrong values, including payload, `conversationId`, confidence payload, table cells, and `textAnswer`.
- [ ] Confirm whether the frontend converts null backend confidence to 0%.
- [ ] Confirm whether direct YTD wrong values came from an LLM-generated text answer, an older DB snapshot, `MonthlyReports`, `MonthlyReportLineItems`, Noavaran API path, or a missing/incorrect symbol context.
- [ ] Confirm whether direct price should be supported through `lookup_symbol_metrics` with `LATEST_PRICE`, or whether the API should return a clearer unsupported message until quote data exists.

## Implementation Status - 2026-06-18

Fixed in this pass:

- Priority 1 direct metric accuracy:
  - Exact companion phrases are preserved before generic monthly-sales snapshot forcing.
  - Added aliases for `فروش YTD`, `فروش YTD تا ماه قبل`, and `متوسط فروش 12/۱۲ ماهه`.
  - Companion-only monthly-sales prose now states the requested metric value instead of falling back to latest `MONTHLY_SALES`.
- Priority 2 conversation context preservation:
  - V2 direct metric preflight parses the enriched conversation message while keeping the current user message as the lookup query text for layout decisions.
  - Added API-level same-conversation follow-up coverage for `فروش YTD چقدر بوده؟`.
- Priority 4 PE prose duplication:
  - Persian deterministic prose no longer prepends `نسبت` when the resolved display name already starts with `نسبت`.

Not fixed in this pass:

- Priority 3 wrong-value source-specific fix: not implemented because the persisted structured payload/tool trace for the original wrong turns was unavailable and the suspect raw values do not exist in the current `DerivedMetrics` snapshot.
- Priority 5 price lookup: not implemented by design. Recommendation: handle it as a separate product/architecture decision. Direct price lookup should either be formally supported by adding `LATEST_PRICE` aliases and routing through `lookup_symbol_metrics`, or the UI/API should consistently explain that direct price is unsupported while valuation tables may still include quote context when available. Do not hide quote columns globally; keep them for valuation/ratio/screening contexts, and consider hiding only Missing quote columns after a separate product decision.

## Likely Files And Classes

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
  - direct metric preflight
  - enriched conversation message handling
  - direct metric phrase list
  - confidence calculation handoff
- `src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs`
  - exact metric alias preservation
  - YTD and 12-month average disambiguation
  - sales snapshot forcing
- `src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs`
  - Persian/English aliases for `AVG_12M_MONTHLY_SALES`, `MONTHLY_SALES_YTD`, `MONTHLY_SALES_YTD_PREVIOUS_MONTH`, `LATEST_PRICE`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs`
  - snapshot expansion
  - latest row selection
  - market context inclusion
  - quote cell fallback
- `src/backend/FinancialCopilot.Application/Scanner/AnswerConsistencyServices.cs`
  - deterministic monthly-sales prose
  - generic Persian `ValueSentence`
  - confidence/prose consistency interaction
- `src/frontend` chat rendering layer
  - only if backend payload confidence is null while the UI displays 0%

## Acceptance Criteria

- `فروش ماهان کچاد چقدر است؟` still returns the compact production/sales table and confidence > 0.
- `متوسط فروش 12 ماهه کچاد چقدر بوده است` resolves to `AVG_12M_MONTHLY_SALES` and the leading sentence uses `57,549,287 میلیون ریال`.
- `فروش YTD کچاد؟` resolves to `MONTHLY_SALES_YTD` and returns `787,016,400 میلیون ریال` from the current DB snapshot.
- `فروش YTD تا ماه قبل کچاد؟` resolves to `MONTHLY_SALES_YTD_PREVIOUS_MONTH` and returns `605,344,668 میلیون ریال` from the current DB snapshot.
- A follow-up `فروش YTD چقدر بوده؟` reuses prior `کچاد` context when the request has a valid prior `conversationId`; otherwise it asks only for the missing symbol.
- YTD and 12-month-average direct answers include confidence > 0 when a numeric value is returned.
- Production/sales answers do not include `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `آخرین قیمت`, or `درصد تغییر آخرین قیمت`.
- Direct price questions have a consistent behavior: either supported by `LATEST_PRICE` lookup or explicitly documented as unsupported without contradicting PE quote-context rendering.
- `P/E کچاد` prose does not contain `نسبت نسبت پی به ای`.
- No new parallel retrieval pipeline is introduced.

## Regression Tests To Add

- Parser/unit tests:
  - `متوسط فروش 12 ماهه کچاد چقدر بوده است` -> `AVG_12M_MONTHLY_SALES`
  - `فروش YTD کچاد چقدر است؟` -> `MONTHLY_SALES_YTD`
  - `فروش YTD تا ماه قبل کچاد؟` -> `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
  - `فروش ماهانه کچاد؟` still -> `MONTHLY_SALES`
- Prose/unit tests:
  - table with `MONTHLY_SALES` and `AVG_12M_MONTHLY_SALES`, requested metric average -> sentence uses average value
  - table with YTD requested -> sentence uses YTD value
  - PE Persian display name already starts with `نسبت` -> no duplicate prefix
- API/integration tests:
  - `متوسط فروش 12 ماهه کچاد چقدر بوده است` returns `AVG_12M_MONTHLY_SALES`, concise prose, table, confidence > 0
  - `فروش YTD کچاد؟` returns `MONTHLY_SALES_YTD`, no fallback, confidence > 0
  - `فروش YTD تا ماه قبل کچاد؟` returns `MONTHLY_SALES_YTD_PREVIOUS_MONTH`, no fallback, confidence > 0
  - successful monthly production/sales answers omit quote columns
  - direct price query behavior matches the decided product rule
- Conversation/API tests:
  - first turn `فروش ماهان کچاد چقدر است؟`, second turn same `conversationId` with `فروش YTD چقدر بوده؟` resolves symbol from context
  - same second turn without usable `conversationId` asks only for symbol
- Payload/UI tests if needed:
  - backend numeric metric confidence is not serialized as null
  - frontend does not display null confidence as 0% for valid structured metric answers

## Regression Coverage Added - 2026-06-18

- `SymbolLookupParserTests.Parser_ExplicitMonthlySalesCompanionQuestion_PreservesRequestedMetric`
  - covers `AVG_12M_MONTHLY_SALES`
  - covers `MONTHLY_SALES_YTD`
  - covers `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
- `AnswerConsistencyTests.SymbolLookup_MonthlySalesCompanionOnly_UsesRequestedMetricInProse`
  - proves companion-only monthly-sales answers state the requested metric value.
- `AnswerConsistencyTests.SymbolLookup_PersianPeDisplayName_DoesNotDuplicateRatioPrefix`
  - proves `نسبت نسبت پی به ای` is not generated.
- `V2MonthlySalesRoutingEndpointTests.V2AiQuery_DirectMonthlySalesCompanionMetric_UsesRequestedMetricInProse`
  - proves direct companion questions return concise prose, table value, confidence > 0, and no quote columns.
- `V2MonthlySalesRoutingEndpointTests.V2AiQuery_DirectYtdFollowup_UsesPreviousConversationSymbol`
  - proves same-conversation follow-up `فروش YTD چقدر بوده؟` resolves `کچاد` from enriched context.

## Proposed Minimal Fix Plan

1. Preserve exact requested metric intent through the V2 direct lookup path and table/prose context.
2. Tighten monthly-sales snapshot forcing so explicit companion metrics (`AVG_12M_MONTHLY_SALES`, `MONTHLY_SALES_YTD`, `MONTHLY_SALES_YTD_PREVIOUS_MONTH`) are not collapsed to `MONTHLY_SALES`.
3. Add exact mixed Persian/English aliases for `فروش YTD`, `فروش YTD تا ماه قبل`, and the observed average-sales phrase.
4. Update deterministic prose to select the requested primary metric first; keep the compact monthly-sales table as companion context.
5. Use enriched conversation context, or a deterministic prior-symbol extraction from memory, for direct metric preflight when the current message lacks a symbol.
6. Decide and document price lookup behavior, then align direct price routing and quote-column rendering.
7. Fix Persian generic prose prefixing for metric display names that already include `نسبت`.
8. Add the regression coverage listed above before changing production behavior.
