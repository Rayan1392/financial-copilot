# Financial Statement Period Analysis Flow and Jalali Period Bug

## Scope

This document traces the implemented flow for the prompt:

`حقوق مالکانه غالبر چقدر است؟`

in feature `081-ai-financial-statement-period-analysis-query`, and identifies the root cause of the bug where the rendered source period shows a numeric value such as `534247` or `387470` instead of a Jalali date.

## Entry Point

The request enters the backend through:

- [AiFacadeController.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs:26)

`POST /api/ai/v1/query` calls the orchestrator service with the raw user message.

## Active Orchestration Mode

Development configuration uses:

- [appsettings.Development.json](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/appsettings.Development.json:30)

```json
"AiOrchestration": {
  "Mode": "MicrosoftAgentFrameworkV2"
}
```

That mode is wired through:

- [MicrosoftAgentFrameworkAiQueryOrchestrationService.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/MicrosoftAgentFrameworkAiQueryOrchestrationService.cs:16)

So `/api/ai/v1/query` executes the V2 workflow, not the legacy V1 imperative orchestrator.

## Intent Detection

In the V2 workflow, the request is checked by:

- [FinancialCopilotWorkflowDefinition.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:383)

The workflow calls:

- [FinancialStatementAnalysisIntentRules.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/AI/Orchestration/FinancialStatementAnalysisIntentRules.cs:87)

For this prompt:

- `حقوق مالکانه` matches balance-sheet metric phrases
- `غالبر` is extracted as the company hint
- no explicit period is requested, so the latest available matching statement is used

The parser builds a `FinancialStatementAnalysisQuery` here:

- [FinancialStatementAnalysisIntentRules.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/AI/Orchestration/FinancialStatementAnalysisIntentRules.cs:106)

For this prompt, the important parsed parts are:

- `MetricFocusCodes` includes `TOTAL_EQUITY`
- `StatementTypeFocus` resolves to `BalanceSheet`
- `IncludeBalanceSheetSummary` becomes `true`
- no explicit audited or variant preference is set

## Use Case Execution

The V2 workflow runs the financial statement analysis use case here:

- [FinancialCopilotWorkflowDefinition.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:386)
- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:351)

The use case performs these steps:

1. Resolve the company from the extracted symbol/company hint.
2. Load persisted normalized financial statements for that company.
3. Parse statement metadata from `WarningsJson`.
4. Select the current and prior statements.
5. Build metric sections.
6. Build source references.
7. Render the Persian answer text.

## Persisted Statement Read Path

Statements are loaded from the ingestion database in:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:17)

The repository:

- reads `FinancialStatements`
- reads `FinancialStatementLineItems`
- reconstructs `FinancialStatementAnalysisStatementSnapshot`
- parses metadata from `WarningsJson`

The metadata parse happens here:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:99)

## Statement Selection

Statement selection happens here:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:177)

Current statement selection filters by:

- statement type
- requested period months if present
- variant preference
- audited preference

Then it orders by:

- `AnnouncementDate` descending
- `PeriodEnd` descending
- `PeriodMonths` descending

at:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:236)

For this query, the selected metric is `TOTAL_EQUITY`, so the returned visible value comes from the selected balance sheet.

## Response Rendering

The rendered Persian text is built in:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:280)

The problematic source line is emitted here:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:324)

```csharp
sb.AppendLine($"{label}{variant} {source.PeriodMonths} ماهه منتهی به {source.JalaliPeriodEnd ?? source.PeriodEndLabel()} ({audited})");
```

The fallback helper is:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:683)

```csharp
internal static string PeriodEndLabel(this FinancialStatementSourceReference source) =>
    source.JalaliPeriodEnd ?? source.ExternalStatementId;
```

This means:

- if `JalaliPeriodEnd` exists, the renderer shows a Jalali date
- if `JalaliPeriodEnd` is null, the renderer shows `ExternalStatementId`

That is the direct reason the UI can show `534247` or `387470`.

## Why Two Numeric Periods Can Appear

The use case may include more than one source reference:

- selected income statement
- selected balance sheet

This happens here:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:404)

So even when the user asks for a balance-sheet metric like equity, the rendered source block can list both statement references. If both have `JalaliPeriodEnd = null`, both lines fall back to numeric external ids, which explains output like:

- `صورت‌های مالی 9 ماهه منتهی به 534247`
- `ترازنامه 9 ماهه منتهی به 387470`

## Root Cause

The root cause is a metadata shape mismatch between the ingestion producer and the analysis consumer.

### Producer Shape

The NADPCO financial statement normalizer writes evidence JSON here:

- [NadpcoApiFinancialStatementNormalizer.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiFinancialStatementNormalizer.cs:159)

It serializes one object like:

```json
[
  {
    "Code": "NadpcoApiStatementSelection",
    "StatementID": 534247,
    "JalaliFiscalYearEnd": "1404/12/29",
    "JalaliPeriodEnd": "1404/09/30",
    "JalaliAnouncementDate": "1404/11/15 10:30:00",
    "AnouncementDate": "...",
    "IsAudited": false,
    "IsRepresented": false,
    "IsComposing": false
  }
]
```

### Consumer Shape

The financial statement analysis repository expects an array of individual `code/evidence` items:

- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:121)

Expected shape:

```json
[
  { "code": "JalaliPeriodEnd", "evidence": "1404/09/30" },
  { "code": "JalaliFiscalYearEnd", "evidence": "1404/12/29" },
  { "code": "JalaliAnnouncementDate", "evidence": "1404/11/15 10:30:00" }
]
```

Because the consumer explicitly does:

- `TryGetProperty("code", ...)`
- `TryGetProperty("evidence", ...)`

the actual normalized producer payload does not populate:

- `JalaliPeriodEnd`
- `JalaliFiscalYearEnd`
- `JalaliAnnouncementDate`
- `AnnouncementDate`
- `IsAudited`
- `IsRepresented`
- `IsComposing`

As a result, `StatementMetadata.Parse(...)` returns mostly null/default metadata for live NADPCO-ingested statements.

## Why Tests May Not Reveal It

The V2 integration test factory seeds `WarningsJson` in the parser-friendly shape:

- [AiFacadeV2EndpointTests.cs](D:/Source/TahlilApp-AI/tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs:1152)

That test seed uses:

```json
[
  { "code": "JalaliFiscalYearEnd", "evidence": "1404/12/29" },
  { "code": "JalaliPeriodEnd", "evidence": "1404/12/29" },
  { "code": "JalaliAnouncementDate", "evidence": "1405/04/09 09:23:24" }
]
```

So tests can pass while the real ingestion path still stores metadata in a different shape.

## Exact Bug Statement

The visible bug:

- `دوره منتهی به` shows a numeric id instead of a Jalali date

is caused by this chain:

1. NADPCO normalization stores metadata in a single-object evidence shape.
2. Financial statement analysis metadata parsing expects `code/evidence` array entries.
3. `JalaliPeriodEnd` is not recovered from persisted live rows.
4. The renderer falls back to `ExternalStatementId`.
5. The final answer shows a numeric id such as `534247` instead of a Jalali date such as `1404/09/30`.

## Relevant Files

- [AiFacadeController.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs:26)
- [appsettings.Development.json](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/appsettings.Development.json:30)
- [MicrosoftAgentFrameworkAiQueryOrchestrationService.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/MicrosoftAgentFrameworkAiQueryOrchestrationService.cs:16)
- [FinancialCopilotWorkflowDefinition.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:383)
- [FinancialStatementAnalysisIntentRules.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/AI/Orchestration/FinancialStatementAnalysisIntentRules.cs:87)
- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:17)
- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:99)
- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:177)
- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:324)
- [FinancialStatementAnalysisServices.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementAnalysisServices.cs:683)
- [NadpcoApiFinancialStatementNormalizer.cs](D:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiFinancialStatementNormalizer.cs:159)
- [AiFacadeV2EndpointTests.cs](D:/Source/TahlilApp-AI/tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs:1152)
