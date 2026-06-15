# Tasks — Comprehensive Analysis AI Query

**Spec:** `066-comprehensive-analysis-ai-query`
**Depends on:** `065` (schema + sync), `047`/`056` (MAF V2 tool adapter pattern), `009` (citations + confidence), `028` (missing-answer feedback)

---

## Stage 1 — Schema Enhancement (PlainTextSummary)

### TASK-001 · Add `PlainTextSummary` column — EF Migration

- Add `PlainTextSummary` column (`string`, max length 10 000, nullable) to `ComprehensiveAnalysisRow` entity configuration.
- Create EF migration `AddComprehensiveAnalysisPlainTextSummary` (additive — no truncation of existing rows).
- Migration `Up` adds the column nullable so existing rows are not broken before the backfill runs.
- Do NOT add a NOT NULL constraint; the column stays nullable because the backfill endpoint populates it after migration.

---

### TASK-002 · HTML Stripper utility

- Create `IHtmlTextStripper` interface in Application layer:
  ```csharp
  public interface IHtmlTextStripper
  {
      string Strip(string html);
  }
  ```
- Implement `HtmlAgilityPackTextStripper` in Infrastructure using `HtmlAgilityPack` (already a common .NET dep):
  - Remove all HTML tags.
  - Decode HTML entities (`&nbsp;`, `&amp;`, `&lt;`, etc.) to their Unicode equivalents.
  - Collapse multiple whitespace/newline sequences to a single space.
  - Trim the result.
- Register as singleton in DI.
- Unit tests: HTML with inline styles, nested tags, HTML entities, empty string, null-safe input.

---

### TASK-003 · Populate `PlainTextSummary` in the sync upsert path

- In `ComprehensiveAnalysis` upsert repository (spec `065` TASK-007), inject `IHtmlTextStripper`.
- Before calling `SaveChangesAsync`, set `row.PlainTextSummary = _stripper.Strip(row.Summary)`.
- This ensures all new and updated rows written after this spec is deployed have clean text.
- Unit test: upsert with HTML summary → `PlainTextSummary` is stripped, `Summary` retains original HTML.

---

### TASK-004 · Backfill admin endpoint

- Add `POST /api/v1/admin/comprehensive-analysis/backfill-plain-text` (requires `DataAdmin` policy).
- Implementation:
  - Query all `ComprehensiveAnalysisRow` where `PlainTextSummary IS NULL OR PlainTextSummary = ''` in bounded pages (configurable `BatchSize`, default 500).
  - Strip and set `PlainTextSummary` for each row in the batch.
  - `SaveChangesAsync` per batch.
  - Return `{ RowsUpdated: int }` on completion.
- Idempotent — safe to call multiple times.
- Integration test: seed rows with HTML summary and null `PlainTextSummary`, call endpoint, verify all rows have plain text populated.

---

## Stage 2 — Application Layer Contracts

### TASK-005 · `ComprehensiveAnalysis` intent type

- Add `ComprehensiveAnalysis` value to the `AiIntentType` enum in the Application layer.
- Keep enum values in the same file as `Scanner` and `SymbolLookup`.
- Update the `LlmAiIntentDetector` system prompt to describe the new intent:
  ```
  ComprehensiveAnalysis: the user is asking about analysis posts, reports, or
  market commentary. Triggers include: تحلیل, گزارش, رصد معاملات عمده,
  تحلیل تکنیکال, تحلیل بنیادی, قیمت تعادلی, نمودار P/E, نمودار P/S,
  تحلیل جامع, "comprehensive analysis", "market report".
  Does NOT trigger when the user asks for a metric value of a specific symbol
  (use SymbolLookup) or asks for stocks matching a condition (use Scanner).
  ```
- Update the LLM structured-output schema for intent detection to include `ComprehensiveAnalysis`.
- Unit tests for intent classification covering:
  - `«آخرین تحلیل تکنیکال شغدیر»` → `ComprehensiveAnalysis`
  - `«رصد معاملات عمده هفته گذشته»` → `ComprehensiveAnalysis`
  - `«تحلیل بنیادی کرازی»` → `ComprehensiveAnalysis`
  - `«P/E حفاری چقدر است؟»` → `SymbolLookup` (not ComprehensiveAnalysis)
  - `«سهم‌هایی با P/E کمتر از ۶»` → `Scanner` (not ComprehensiveAnalysis)

---

### TASK-006 · Query contracts

Create `ComprehensiveAnalysis/Contracts.cs` in the Application layer:

```csharp
// Parser output
public record ComprehensiveAnalysisParseResult(
    ComprehensiveAnalysisParseStatus Status,
    IReadOnlyList<string> SymbolNames,        // raw names from user message
    IReadOnlyList<string> TopicTags,          // matched to allowed 7 category slugs
    DateTimeOffset? FromDate,
    int Limit,
    string? ClarificationPrompt               // populated when Status=ClarificationRequired
);

public enum ComprehensiveAnalysisParseStatus { Parsed, ClarificationRequired }

// Use case input
public record ComprehensiveAnalysisQuery(
    IReadOnlyList<string> SymbolNames,
    IReadOnlyList<string> TopicTags,
    DateTimeOffset? FromDate,
    int Limit
);

// Single analysis item returned to AI
public record ComprehensiveAnalysisSummaryItem(
    long AnalysisId,
    string Title,
    string PersianCreatedAt,
    string AuthorName,
    string PlainTextSummary,           // capped at 2000 chars
    IReadOnlyList<string> TagNames
);

// Use case result
public record ComprehensiveAnalysisQueryResult(
    IReadOnlyList<ComprehensiveAnalysisSummaryItem> Items,
    IReadOnlyList<string> UnresolvedSymbols,
    bool HasResults
);
```

---

### TASK-007 · `IComprehensiveAnalysisQueryParser` interface

```csharp
public interface IComprehensiveAnalysisQueryParser
{
    Task<ComprehensiveAnalysisParseResult> ParseAsync(
        string userMessage,
        CancellationToken cancellationToken);
}
```

- Create `LlmComprehensiveAnalysisQueryParser` in Infrastructure:
  - Single LLM structured-output call.
  - LLM returns: `{ symbolNames: string[], topicTags: string[], fromDateHint: string? }`.
  - Backend validates `topicTags` against the 7 allowed slugs:
    ```
    تحلیل_تکنیکال | قیمت_تعادلی | رصد_معاملات_عمده |
    گزارش_فصلی | گزارش_ماهانه | نمودار_P_S | نمودار_P_E
    ```
    Invalid or unrecognised topic tags are silently dropped (not an error).
  - `fromDateHint` is a relative expression (`yesterday`, `this_week`, `last_week`, ISO date). Backend resolves to `DateTimeOffset`.
  - If the LLM returns empty `symbolNames`, empty `topicTags`, and no `fromDateHint`, return `ClarificationRequired` with the prompt:
    `«لطفاً نماد سهم، نوع تحلیل (مثلاً تحلیل تکنیکال، رصد معاملات)، یا بازه زمانی مورد نظر را مشخص کنید.»`
- Unit tests:
  - Persian symbol extraction
  - TopicTag validation — unknown tag dropped
  - Relative date «این هفته» → correct `DateTimeOffset`
  - Empty LLM output → `ClarificationRequired`
  - Mixed symbol + topic

---

### TASK-008 · `IComprehensiveAnalysisQueryRepository` interface

```csharp
public interface IComprehensiveAnalysisQueryRepository
{
    Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetBySymbolNamesAsync(
        IReadOnlyList<string> symbolNames, int limit, CancellationToken ct);

    Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetByTopicTagsAsync(
        IReadOnlyList<string> topicTagSlugs, int limit, CancellationToken ct);

    Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetByDateRangeAsync(
        DateTimeOffset from, int limit, CancellationToken ct);

    Task<IReadOnlyList<ComprehensiveAnalysisSummaryItem>> GetCombinedAsync(
        IReadOnlyList<string> symbolNames,
        IReadOnlyList<string> topicTagSlugs,
        DateTimeOffset? from,
        int limit,
        CancellationToken ct);
}
```

- Implement `EfCoreComprehensiveAnalysisQueryRepository` in Infrastructure against `ComprehensiveAnalysisProviderDbContext`.
- All methods:
  - Project onto `ComprehensiveAnalysisSummaryItem` (never return raw EF entities to Application).
  - Cap `PlainTextSummary` at 2 000 characters: `(s.PlainTextSummary ?? "").Substring(0, Math.Min(2000, ...))`.
  - Fall back to stripping `Summary` on the fly (via `IHtmlTextStripper`) when `PlainTextSummary` is null (for rows created before TASK-003 was deployed).
  - Order by `CreatedAt DESC`.
- Symbol name matching: `WHERE t.TagName = @name AND t.TagTypeId = 1` (exact match, case-sensitive — tag names are stored as they come from the API).
- Topic tag matching: `WHERE t.TagSlug = @slug AND t.IsAnalytic = true`.
- `GetCombinedAsync`: applies all non-empty filter groups with AND logic.
- Unit tests with in-memory EF:
  - Symbol filter returns correct rows ordered by date.
  - Topic filter applies `IsAnalytic` guard.
  - Date filter excludes older rows.
  - Combined symbol + topic applies both.
  - Summary capped at 2 000 chars.
  - Fallback strip when `PlainTextSummary` is null.

---

### TASK-009 · `QueryComprehensiveAnalysisUseCase`

```csharp
public class QueryComprehensiveAnalysisUseCase
{
    Task<ComprehensiveAnalysisQueryResult> ExecuteAsync(
        ComprehensiveAnalysisQuery query, CancellationToken ct);
}
```

- Resolves which repository method to call based on which fields are populated.
- Symbol-only query → `GetBySymbolNamesAsync`.
- Topic-only query → `GetByTopicTagsAsync`.
- Date-only query → `GetByDateRangeAsync`.
- Any combination with multiple populated fields → `GetCombinedAsync`.
- Computes `UnresolvedSymbols`: symbol names from the query that returned zero rows in the tag join.
- Does NOT call the LLM — it is a deterministic use case.
- Unit tests: correct dispatch to repository method, unresolved symbol detection, empty result case.

---

## Stage 3 — Orchestration Integration

### TASK-010 · V1 orchestration branch (`AiQueryOrchestrationService`)

- Extend `AiQueryOrchestrationService` with a `ComprehensiveAnalysis` intent branch:
  1. Call `IComprehensiveAnalysisQueryParser.ParseAsync`.
  2. If `ClarificationRequired`, return clarification response (same pattern as Scanner clarification).
  3. Call `QueryComprehensiveAnalysisUseCase.ExecuteAsync`.
  4. Build `ComprehensiveAnalysisResult` and citations (TASK-011).
  5. Feed result to `IExplainableAnswerBuilder` for confidence and follow-up questions.
  6. Collect missing-answer feedback (TASK-012).
  7. Billing finalization.
  8. Persist message.
- V1 branch is the rollback path; it must remain functional when `AiOrchestration:Mode = V1`.

---

### TASK-011 · V2 MAF tool adapter (`ComprehensiveAnalysisToolAdapter`)

- Create `ComprehensiveAnalysisToolAdapter` in Infrastructure / AI orchestration boundary following the same narrow contract as `SymbolLookupToolAdapter`:
  ```csharp
  public record ComprehensiveAnalysisToolInput(
      IReadOnlyList<string> SymbolNames,
      IReadOnlyList<string> TopicTags,
      string? FromDateIso,   // validated ISO 8601, never arbitrary string
      int Limit
  );

  public record ComprehensiveAnalysisToolOutput(
      IReadOnlyList<ComprehensiveAnalysisSummaryItem> Items,
      IReadOnlyList<string> UnresolvedSymbols
  );
  ```
- Adapter validates `FromDateIso` (must parse as `DateTimeOffset` or be null).
- Adapter validates `Limit` (1–5, clamp do not throw).
- Adapter calls `QueryComprehensiveAnalysisUseCase`; never calls the repository directly.
- Register in `FinancialCopilotAgentToolRegistry`.
- Add tool definition JSON (function name `query_comprehensive_analysis`, parameter descriptions in Persian and English):
  ```
  symbol_names: list of Persian stock symbol names from the user message
  topic_tags: analysis type slugs — only from allowed list
  from_date_iso: ISO 8601 date string for temporal filtering, null if no date mentioned
  limit: number of results 1-5, default 3
  ```
- Architecture test: `ComprehensiveAnalysisToolAdapter` does not directly reference `DbContext` or EF types.

---

### TASK-012 · Missing-answer feedback wiring

- After `QueryComprehensiveAnalysisUseCase.ExecuteAsync`:
  - If `result.Items` is empty AND at least one `SymbolName` was requested → collect `DataCoverageGap` feedback with `RequestedMetricCode = null`, `Context = JSON({ symbolNames, topicTags })`.
  - If parser returned `ClarificationRequired` → collect `ParserLimitation` feedback.
- Use existing `IMissingAnswerFeedbackCollector` (fire-and-forget, swallows exceptions).
- Does not block the response.

---

## Stage 4 — Response Contracts

### TASK-013 · Extend `AiQueryResponse` with `ComprehensiveAnalysisResult`

- Add to `AiQueryResponse` (Application DTO):
  ```csharp
  public ComprehensiveAnalysisQueryResult? ComprehensiveAnalysisResult { get; init; }
  ```
- Add to `AiQueryHttpResponse` (API DTO):
  ```csharp
  public ComprehensiveAnalysisResultResponse? ComprehensiveAnalysisResult { get; init; }
  ```
- `ComprehensiveAnalysisResultResponse`:
  ```csharp
  public record ComprehensiveAnalysisResultResponse(
      IReadOnlyList<ComprehensiveAnalysisItemResponse> Items,
      IReadOnlyList<string> UnresolvedSymbols
  );

  public record ComprehensiveAnalysisItemResponse(
      long AnalysisId,
      string Title,
      string PersianCreatedAt,
      string AuthorName,
      string Summary,          // PlainTextSummary capped — safe to display in chat
      IReadOnlyList<string> Tags
  );
  ```
- New fields are nullable/additive — no breaking change to existing response consumers.

### TASK-014 · Data Citations for analysis items

- In the `IExplainableAnswerBuilder` flow, when `ComprehensiveAnalysisResult` is present, add one `DataCitation` per returned item:
  ```csharp
  new DataCitation(
      SourceProvider: "CyclicalWaves",
      SourceType: "ComprehensiveAnalysis",
      Reference: item.Title,
      ReportDate: null,
      LastSyncTimestamp: syncedAt,  // from ComprehensiveAnalysis.SyncedAt
      ExternalId: item.AnalysisId.ToString()
  )
  ```
- `SyncedAt` must be accessible from the query result: add it to `ComprehensiveAnalysisSummaryItem`.

### TASK-015 · Confidence score inputs for ComprehensiveAnalysis

- `IConfidenceScoreCalculator` already accepts `ConfidenceFactors`; extend the builder to populate:
  - `InterpretationCertainty`: `1.0` when parser returned `Parsed` with at least one extracted field; `0.5` when only date was extracted.
  - `EvidenceCompleteness`: ratio of resolved symbols to requested symbols (1.0 when no symbols were requested).
  - `SourceFreshness`: based on `MAX(SyncedAt)` of returned items vs. current time. If `SyncedAt` is within 24 h → `1.0`; within 48 h → `0.85`; older → `0.6`.
  - `Warnings`: one warning added when `UnresolvedSymbols` is non-empty.

---

## Stage 5 — Conversation Persistence

### TASK-016 · Persist `ComprehensiveAnalysisResult` in assistant Message

- Extend `AssistantMessageContentRow` (or equivalent structured payload) to include `ComprehensiveAnalysisResultJson` (nullable serialized JSON), following the same pattern as `ScannerResultJson` and `SymbolLookupResultJson`.
- EF migration `AddComprehensiveAnalysisMessagePayload` (additive nullable column).
- On conversation reload (`GET /api/ai/v1/conversations/{id}/messages`), deserialize and include in `AssistantMessageContentResponse`.
- Integration test: send analysis query → reload conversation message → `ComprehensiveAnalysisResult` is present with correct items.

---

## Stage 6 — Tests

### TASK-017 · Unit tests

All unit tests use fake/in-memory providers — no network calls.

- `ComprehensiveAnalysisIntentDetectorTests` (5 cases):
  - Persian analysis question → `ComprehensiveAnalysis`
  - Topic-only question → `ComprehensiveAnalysis`
  - P/E metric question → `SymbolLookup` (negative test)
  - Screener question → `Scanner` (negative test)
  - Ambiguous → `Clarification`

- `LlmComprehensiveAnalysisQueryParserTests` (7 cases):
  - Symbol extraction
  - Topic tag validation (unknown tag dropped)
  - Relative date «این هفته»
  - Combined symbol + topic
  - Empty → `ClarificationRequired`
  - `fromDateHint` ISO string parsing
  - Allowed topic slugs list boundary

- `EfCoreComprehensiveAnalysisQueryRepositoryTests` (8 cases):
  - Symbol filter returns correct rows DESC
  - Topic filter applies `IsAnalytic` guard
  - Date filter excludes older rows
  - Combined: symbol + topic
  - Summary capped at 2 000 chars
  - Null `PlainTextSummary` falls back to strip
  - Zero results returns empty list (no exception)
  - Limit respected

- `QueryComprehensiveAnalysisUseCaseTests` (4 cases):
  - Symbol-only dispatches to `GetBySymbolNamesAsync`
  - Topic-only dispatches to `GetByTopicTagsAsync`
  - Combined dispatches to `GetCombinedAsync`
  - `UnresolvedSymbols` populated for symbol with zero results

- `ComprehensiveAnalysisToolAdapterTests` (3 cases):
  - Valid input → use case called with correct query
  - Invalid `FromDateIso` → validation failure before use case is called
  - Limit clamped to 5

- `HtmlTextStripperTests` (5 cases):
  - HTML tags removed
  - Entities decoded
  - Whitespace collapsed
  - Empty string safe
  - Null-safe

### TASK-018 · Integration tests

All integration tests use the in-memory `ComprehensiveAnalysisProviderDbContext` seeded with known rows.

- `ComprehensiveAnalysisEndpointTests` (6 cases):
  1. Query with known symbol → `ComprehensiveAnalysisResult.Items` non-empty, `AnalysisId` correct.
  2. Query with unknown symbol → `UnresolvedSymbols` contains the name, no exception.
  3. Query with topic tag → items filtered by `IsAnalytic`.
  4. Billing ledger entry created after successful analysis query.
  5. Message reload → `ComprehensiveAnalysisResult` present in reloaded message.
  6. Unauthenticated request → 401.

- `ComprehensiveAnalysisBackfillEndpointTests` (2 cases):
  1. Backfill endpoint populates `PlainTextSummary` for rows with null value.
  2. Non-admin user → 403.

### TASK-019 · Architecture tests

- `ComprehensiveAnalysisToolAdapter` must not directly reference `DbContext` or any EF namespace.
- `QueryComprehensiveAnalysisUseCase` must not reference Infrastructure assembly types.
- `IComprehensiveAnalysisQueryRepository` interface must live in Application, not Infrastructure.

---

## Dependency Order

```
TASK-001 (migration)
  → TASK-002 (HTML stripper)
  → TASK-003 (sync upsert wiring)
  → TASK-004 (backfill endpoint)

TASK-005 (intent type + detector update)
  → TASK-006 (query contracts)
  → TASK-007 (query parser)
  → TASK-008 (query repository)
  → TASK-009 (use case)
  → TASK-010 (V1 orchestration branch)
  → TASK-011 (V2 tool adapter)
  → TASK-012 (feedback wiring)

TASK-013 (response contracts)
  → TASK-014 (citations)
  → TASK-015 (confidence inputs)
  → TASK-016 (message persistence)

TASK-002 + TASK-008 + TASK-009 + TASK-010 + TASK-011
  → TASK-017 (unit tests)
  → TASK-018 (integration tests)
  → TASK-019 (architecture tests)
```

---

## Notes on spec `065` Changes Required

The following changes must be made to spec `065` artifacts as part of this delivery (they are amendments, not rewrites):

1. **TASK-007 in spec `065`** (`IComprehensiveAnalysisRepository.UpsertAsync`): inject `IHtmlTextStripper` and set `PlainTextSummary` during upsert. This is a one-line change to the existing method — do not re-implement the upsert.
2. **`ComprehensiveAnalysisSummaryItem`** read model from spec `065` TASK-010: add `SyncedAt` field so the confidence calculator can evaluate freshness.
3. No other spec `065` tasks need modification. The sync pipeline, token auth, Hangfire job, and `ComprehensiveAnalysisSyncLog` remain unchanged.


# تکمیلی
# Comprehensive Analysis Retrieval Rules

## Primary Rule

When the user asks about a stock, company, ticker, or symbol, your FIRST responsibility is to search the ComprehensiveAnalyses dataset before asking any follow-up questions.

## Symbol Detection

If the user's question explicitly contains a stock symbol or company name such as:

* شغدیر
* فملی
* کگل
* فارس
* اخابر

DO NOT ask clarifying questions such as:

* "لطفاً نام شاخص یا متریک مالی موردنظرتان را مشخص کنید."
* "منظور شما تحلیل بنیادی است یا تکنیکال؟"
* "کدام جنبه سهم را بررسی کنم؟"

The symbol itself is sufficient to begin retrieval.

## Data Source

Search the table:

```sql
SELECT
    "Id",
    "Title",
    "Summary",
    "CreatedAt",
    "PersianCreatedAt",
    "AuthorId",
    "AuthorName",
    "SyncedAt",
    "PlainTextSummary"
FROM public."ComprehensiveAnalyses"
WHERE "PlainTextSummary" LIKE '%{Symbol}%'
ORDER BY "CreatedAt" DESC;
```

## Freshness Rule

Only consider analyses created within the last 30 days.

Use the newest matching record.

If multiple records exist, always select the most recent one.

## No Analysis Found

If no matching analysis exists in the last 30 days, respond exactly in this style:

"تحلیل جدیدی از نماد شغدیر در ۳۰ روز گذشته یافت نشد."

Do not generate your own stock analysis.
Do not speculate.
Do not use market knowledge to fill the gap.

## Analysis Presentation Rule

When a matching record is found:

1. Present the latest analysis.
2. Preserve the author's wording.
3. Do not rewrite the conclusions.
4. Do not add extra financial interpretation.
5. Do not add your own technical analysis.
6. Do not invent support/resistance levels.
7. Do not generate new valuation estimates.
8. Do not expand the content with AI-generated commentary.

You may only:

* organize the text
* add section headers
* improve readability

The actual statements, numbers, valuation estimates, P/E, P/S, dividend expectations and conclusions must remain exactly as provided in the stored analysis.

## Expected Output Example

User:
شغدیر را بررسی کن

Assistant:

آخرین تحلیل یافت‌شده برای شغدیر:

تاریخ: {PersianCreatedAt}

الف) بررسی حجم و نمودار امروز
...
(متن تحلیل عیناً از PlainTextSummary)

ب) بررسی ارزش ذاتی سهم
...
(متن تحلیل عیناً)

ج) بررسی P/E سهم
...
(متن تحلیل عیناً)

د) بررسی P/S سهم
...
(متن تحلیل عیناً)

نتیجه‌گیری:
...
(متن تحلیل عیناً)

منبع: ComprehensiveAnalyses
نویسنده: {AuthorName}

## Priority

For stock-specific questions:

ComprehensiveAnalyses (last 30 days)
→ then other internal financial sources
→ then AI reasoning

Never perform AI reasoning first when a recent ComprehensiveAnalysis exists.
