# wrong-ytd-values-and-confidence-investigation

Date: 2026-06-18
Status: Fixed in current working tree; focused regressions added
Scope: Wrong YTD numeric answers and confidence displayed as 0%

## Observed Problem

In conversation `7a687692-7f60-46d9-a8dd-4da3d2016fe9`, two YTD answers returned values that do not match the current structured monthly-sales metrics for KCHAD:

| Query / turn | Rendered text value | Current expected value |
|---|---:|---:|
| `چادرملو` after `فروش YTD چقدر بوده؟` | `415,830,370 میلیون ریال` | `MONTHLY_SALES_YTD = 787,016,400 میلیون ریال` |
| `فروش YTD تا ماه قبل کچاد؟` | `324,950,648 میلیون ریال` | `MONTHLY_SALES_YTD_PREVIOUS_MONTH = 605,344,668 میلیون ریال` |

The UI also showed confidence as `0%` for those numeric answers.

## Current Structured Metric Values

Current `DerivedMetrics` rows for `ExternalCompanyId = '3'`:

| MetricCode | PeriodStart | PeriodEnd | Raw value | Display in million Rials |
|---|---:|---:|---:|---:|
| `MONTHLY_SALES_YTD` | 2026-04-21 | 2026-05-21 | `787,016,400,000,000` | `787,016,400` |
| `MONTHLY_SALES_YTD_PREVIOUS_MONTH` | 2026-04-21 | 2026-05-21 | `605,344,668,000,000` | `605,344,668` |

The suspect raw values were searched at both raw-rial and displayed-million scales:

- `415,830,370,000,000`
- `324,950,648,000,000`
- `415,830,370`
- `324,950,648`

They were not found in:

- `DerivedMetrics.Value`
- `MonthlyReportLineItems.SalesAmount` for KCHAD monthly reports

## Captured Persisted Conversation Evidence

### Turn: `فروش YTD چقدر بوده؟`

Assistant response:

`لطفاً نماد را مشخص کنید. اگر منظورتان همان **کچاد** است، بفرمایید تا فروش **YTD** آن را اعلام کنم.`

Persisted assistant payload:

- `Version`: `2`
- `Intent`: `4` (`DetectedIntent.Unknown`)
- `ClarificationRequired`: `false`
- `TextAnswer`: present
- `SymbolLookupTable`: `null`
- `ComprehensiveAnalysisResult`: `null`
- `ConfidenceScore`: `null`

### Turn: `چادرملو`

Assistant response:

`فروش **YTD** نماد **کچاد (چادرملو)** برابر است با: **415,830,370 میلیون ریال**`

Persisted assistant payload:

- `Version`: `2`
- `Intent`: `4` (`DetectedIntent.Unknown`)
- `ClarificationRequired`: `false`
- `TextAnswer`: present
- `SymbolLookupTable`: `null`
- `ComprehensiveAnalysisResult`: `null`
- `ConfidenceScore`: `null`

### Turn: `فروش YTD تا ماه قبل کچاد؟`

Assistant response:

`فروش **YTD تا ماه قبل** نماد **کچاد (چادرملو)** برابر است با: **324,950,648 میلیون ریال**`

Persisted assistant payload:

- `Version`: `2`
- `Intent`: `4` (`DetectedIntent.Unknown`)
- `ClarificationRequired`: `false`
- `TextAnswer`: present
- `SymbolLookupTable`: `null`
- `ComprehensiveAnalysisResult`: `null`
- `ConfidenceScore`: `null`

## Captured Comparison With Correct Structured Turns

Correct monthly-sales turns in the same conversation persisted as structured symbol lookup responses:

- `Intent`: `SymbolLookup`
- `SymbolLookupTable`: present
- `ConfidenceScore.Score`: `0.94`
- columns:
  - `SYMBOL`
  - `COMPANY_NAME`
  - `MONTHLY_SALES`
  - `AVG_12M_MONTHLY_SALES`
  - `MONTHLY_SALES_YTD`
  - `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
- cells:
  - `MONTHLY_SALES = 90,879,722`
  - `AVG_12M_MONTHLY_SALES = 57,549,287`
  - `MONTHLY_SALES_YTD = 787,016,400`
  - `MONTHLY_SALES_YTD_PREVIOUS_MONTH = 605,344,668`

This confirms the correct values were available to the structured lookup path in the same conversation.

## Root-Cause Findings

### Confirmed: wrong YTD values were not table-backed

The wrong YTD answers were persisted as `TextAnswer` under `DetectedIntent.Unknown`, with no `SymbolLookupTable`. Therefore the values `415,830,370` and `324,950,648` were not rendered from the structured symbol lookup table.

### Confirmed: backend confidence was null, not 0

For the wrong YTD answers, `ConfidenceScore` in the backend assistant payload was `null`. The displayed `0%` is therefore consistent with a frontend/display layer converting missing confidence to zero, or with client-side fallback formatting. It was not a backend `ConfidenceScore.Score = 0` in the persisted payload.

### Confirmed: current structured data does not contain the suspect values

The suspect values were not found in current `DerivedMetrics` or KCHAD `MonthlyReportLineItems` at the searched scales. This rules out the current structured lookup table as the source.

### Likely: LLM-generated prose path / Unknown fallback

Because the wrong turns had:

- `Intent = Unknown`
- `TextAnswer` populated
- `SymbolLookupTable = null`
- `ConfidenceScore = null`

the most likely source is LLM-generated prose from the V2 agent path without a successful tool-backed symbol lookup result.

### Tool-call trace status

Tool calls are not persisted in `AssistantPayloadJson`. For these historical turns, the persisted payload only proves that no scanner table, symbol lookup table, comprehensive-analysis result, or confidence object reached final persistence. Exact tool-call decisions for those historical turns require runtime logs/traces from the original execution window; they are not recoverable from the current persisted message payload.

## Confidence 0% Investigation

Backend evidence:

- Wrong YTD turns: `ConfidenceScore = null`
- Correct monthly-sales structured turns: `ConfidenceScore.Score = 0.94`
- PE structured turn: `ConfidenceScore.Score = 0.95`

Working conclusion:

- The backend did not compute a zero confidence score for the wrong YTD answers.
- It emitted no confidence score because the route ended as `Unknown` text answer.
- The `0%` display should be investigated in the frontend or response-rendering layer as a null-to-zero presentation fallback.

## Diagnostic SQL

```sql
SELECT "Role", left("Content", 120),
       CASE WHEN "AssistantPayloadJson" IS NULL
            THEN 'NULL'
            ELSE length("AssistantPayloadJson")::text
       END AS payload_length,
       "CreatedAt"
FROM public."Messages"
WHERE "ConversationId" = '7a687692-7f60-46d9-a8dd-4da3d2016fe9'
ORDER BY "CreatedAt", "Role";
```

```sql
SELECT "ExternalCompanyId", "MetricCode", "PeriodStart", "PeriodEnd", "Value"
FROM public."DerivedMetrics"
WHERE "Value" IN (
  415830370000000,
  324950648000000,
  415830370,
  324950648
)
ORDER BY "ExternalCompanyId", "PeriodEnd" DESC;
```

```sql
SELECT mr."ProviderName", mr."ExternalCompanyId", mr."ExternalReportId",
       mr."PeriodStart", mr."PeriodEnd", mr."OutputType",
       li."ProductCode", li."SalesAmount"
FROM public."MonthlyReports" mr
JOIN public."MonthlyReportLineItems" li ON li."MonthlyReportId" = mr."Id"
WHERE mr."ExternalCompanyId" = '3'
  AND li."SalesAmount" IN (
    415830370000000,
    324950648000000,
    415830370,
    324950648
  )
ORDER BY mr."PeriodEnd" DESC;
```

```sql
SELECT "Content", "AssistantPayloadJson", "CreatedAt"
FROM public."Messages"
WHERE "ConversationId" = '7a687692-7f60-46d9-a8dd-4da3d2016fe9'
  AND "Role" = 'Assistant'
  AND (
    "Content" LIKE '%415,830,370%'
    OR "Content" LIKE '%324,950,648%'
    OR "Content" LIKE '%نماد را مشخص%'
  )
ORDER BY "CreatedAt";
```

## Temporary Diagnostic Logging

No temporary production logging was added in this task. The persisted assistant payloads were sufficient to confirm:

- the wrong answers were `Unknown` text answers,
- the structured symbol lookup table was absent,
- backend confidence was null,
- the values were not present in current structured data.

If this reproduces again, add temporary request-scoped logging around V2 step 3 and step 4 for:

- original message and enriched message,
- selected direct-preflight flag,
- LLM tool calls,
- resulting `ScannerResult`, `LookupResult`, `ComprehensiveAnalysisResult`,
- detected intent,
- `ConfidenceScore` null/non-null and score,
- final `TextAnswer`.

## Proposed Next Investigation Steps

No fixes should be implemented from this bug until a live reproduction or trace confirms the current source.

Recommended next steps:

1. Re-run the exact conversation against current code and capture raw `POST /api/ai/v1/query` responses for each turn.
2. Confirm whether the frontend still displays null confidence as `0%`.
3. If wrong values recur, enable temporary V2 workflow logging for tool calls and result computation.
4. If wrong values do not recur, classify the historical wrong values as pre-fix `Unknown` fallback/LLM prose generated without structured data.

## Acceptance Criteria For Closing This Investigation

- Exact API payload is captured for any new reproduction.
- If wrong values recur, the source is identified as one of:
  - old DB snapshot,
  - wrong symbol resolution,
  - conversation-context loss,
  - `MonthlyReports`,
  - `MonthlyReportLineItems`,
  - Noavaran path,
  - LLM-generated text,
  - another source.
- Frontend confidence display behavior for `ConfidenceScore = null` is confirmed.
- No code fix is merged under this bug without a confirmed current source.

## Fix Implementation Notes

Implemented on 2026-06-18 after the current code reproduced the routing failure with:

1. `فروش YTD چقدر بوده؟`
2. `چادرملو`

The second turn incorrectly returned `Intent = Unknown` before the fix because the current user message was only a company name, so the deterministic direct-metric preflight did not run.

Backend fix:

- `FinancialCopilotWorkflowDefinition` now detects short symbol/company follow-ups when the enriched recent conversation contains a pending direct metric term.
- Those follow-ups are routed through the existing `lookup_symbol_metrics` adapter and parser.
- The fix preserves the existing direct metric path and does not add a new retrieval pipeline.
- The structured lookup path now returns `MONTHLY_SALES_YTD = 787,016,400` for the reported follow-up shape instead of allowing LLM-only `Unknown` prose.

Frontend fix:

- Chat confidence is now optional in the frontend model.
- Missing backend `ConfidenceScore` is no longer converted to `0`.
- The confidence badge is hidden when no backend confidence score exists, while usage credits still render.

Regression tests added:

- API-level V2 regression for metric-first/company-name-second YTD follow-up.
- Frontend message-list regression proving missing confidence does not display as `0%`.

Validation:

- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~V2AiQuery_PendingYtdMetricThenCompanyName_UsesStructuredLookup" -m:1 --no-restore`
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~V2MonthlySalesRoutingEndpointTests" -m:1 --no-restore`
- `npm test -- message-list.test.tsx`
