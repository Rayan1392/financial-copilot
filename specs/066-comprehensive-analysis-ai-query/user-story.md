# User Story — Comprehensive Analysis AI Query

## Story

As an investor,
I want to ask questions like «آخرین تحلیل تکنیکال شغدیر را بده» or «رصد معاملات عمده این هفته چه بود؟» through the chat interface,
so that I can receive real content from CyclicalWaves comprehensive analysis posts stored in the local database without the AI fabricating market analysis.

## Background

Spec `065-cyclicalwaves-comprehensive-analysis-sync` delivers the sync pipeline that populates `ComprehensiveAnalyses`, `ComprehensiveAnalysisTags`, and `ComprehensiveAnalysisCategories` tables in PostgreSQL. That spec ends at storage. This story closes the gap between stored data and AI-answerable questions by:

1. Adding a `ComprehensiveAnalysis` intent type to the AI facade.
2. Adding a narrow tool adapter + use case that the MAF V2 workflow calls.
3. Enriching the `ComprehensiveAnalysis` entity with a clean `PlainTextSummary` field populated at write time so raw HTML never enters LLM context.
4. Returning structured citations so the user can trace which analysis post was used.

The single public endpoint `POST /api/ai/v1/query` remains the only entry point. No new public endpoints are introduced.

## Acceptance Criteria

### Intent Detection
- `POST /api/ai/v1/query` recognises a new `ComprehensiveAnalysis` intent type alongside the existing `Scanner`, `SymbolLookup`, and `Clarification` intents.
- The intent detector correctly classifies Persian and English questions about analysis posts, technical analysis, fundamental reports, P/E charts, P/S charts, equilibrium price, and trading volume observation.
- The intent detector does NOT classify metric-point questions (Scanner / SymbolLookup territory) as `ComprehensiveAnalysis`.

### Plain-Text Summary
- A `PlainTextSummary` column is added to the `ComprehensiveAnalyses` table via an additive EF migration.
- The sync upsert path (from spec `065`) populates `PlainTextSummary` by stripping all HTML tags and collapsing whitespace from the `Summary` field at write time.
- A backfill admin endpoint `POST /api/v1/admin/comprehensive-analysis/backfill-plain-text` strips and repopulates `PlainTextSummary` for all existing rows that have a null or empty value, so data loaded before this spec is not missing clean text.
- The LLM never receives raw HTML from `Summary`; it always receives `PlainTextSummary`.

### Query Parser
- `IComprehensiveAnalysisQueryParser` extracts from the user message:
  - `SymbolNames`: list of Persian stock symbol names or codes mentioned (`شغدیر`, `کرازی`, etc.) — may be empty.
  - `TopicTags`: list of analysis-type tag names from the allowed 7 categories — may be empty.
  - `FromDate`: optional date parsed from temporal phrases («این هفته», «دیروز», «هفته گذشته», explicit dates).
  - `Limit`: default 3, max 5.
- When neither `SymbolNames` nor `TopicTags` nor `FromDate` can be extracted, the parser returns `ClarificationRequired` with a helpful Persian prompt.
- Parser output is validated server-side before the query executes.

### Query Use Case and Repository
- `IComprehensiveAnalysisQueryRepository` supports three access patterns:
  - By symbol name: `WHERE TagName = @name AND TagTypeId = 1 ORDER BY CreatedAt DESC`
  - By topic tag: `WHERE TagName = @tag AND IsAnalytic = 1 ORDER BY CreatedAt DESC`
  - By date range: `WHERE CreatedAt >= @from ORDER BY CreatedAt DESC`
  - Patterns may be combined (symbol AND topic, symbol AND date, etc.).
- `QueryComprehensiveAnalysisUseCase` is the boundary the tool adapter crosses — it never exposes the repository directly to the LLM.
- Results are truncated: `PlainTextSummary` is capped at 2 000 characters per record before being returned to the AI tool adapter, to prevent context window exhaustion when multiple analyses are returned.
- Unresolved symbol names (no matching tag in `ComprehensiveAnalysisTags`) are returned in a `UnresolvedSymbols` list; the query proceeds for resolved symbols.

### AI Tool Adapter and Orchestration
- A `ComprehensiveAnalysisToolAdapter` is added to the MAF V2 workflow tool registry alongside the existing `ScannerPlanToolAdapter` and `SymbolLookupToolAdapter`.
- The tool adapter accepts a narrow typed input DTO derived from the parser output and calls `QueryComprehensiveAnalysisUseCase`.
- The tool adapter must not expose `DbContext`, arbitrary SQL, or unvalidated LLM-originated strings to the repository.
- The V1 orchestration path (`AiQueryOrchestrationService`) is also extended with a `ComprehensiveAnalysis` intent branch for rollback compatibility.
- Billing reservation occurs before the tool adapter executes; finalization follows the same pattern as `SymbolLookup`.

### Response and Citations
- The `AiQueryResponse` is extended with a nullable `ComprehensiveAnalysisResult` that carries:
  - List of matched analysis items: `{ Id, Title, PersianCreatedAt, AuthorName, PlainTextSummary (capped), TagNames }`.
  - `UnresolvedSymbols` list when symbol extraction yielded names not found in tags.
  - `DataCitations`: one citation per returned analysis item with `SourceProvider = "CyclicalWaves"`, `SourceType = "ComprehensiveAnalysis"`, `Reference = Title`, `PersianDate`, `AnalysisId`.
- The LLM generates a Persian natural-language answer using the tool result content; it does not invent analysis content not present in the tool result.
- The Confidence Score is calculated by the existing `IConfidenceScoreCalculator` from parser certainty, result completeness, and data freshness (based on `SyncedAt` vs current time).
- Suggested follow-up questions are derived from the returned tags and symbols.
- The assistant Message is persisted with the structured `ComprehensiveAnalysisResult` so the conversation can be reloaded.

### Missing Answer Feedback
- When the query returns no results (no matching tag for the requested symbol), a `DataCoverageGap` feedback entry is recorded via the existing `IMissingAnswerFeedbackCollector`.
- When the parser cannot extract any queryable parameters, a `ParserLimitation` feedback entry is recorded.

### Tests
- Unit tests: intent classification (Persian/English), parser extraction (symbol, topic, date, combined, empty → clarification), query repository (symbol filter, topic filter, date filter, combined, summary capping, unresolved symbols).
- Integration tests: end-to-end `POST /api/ai/v1/query` with seeded analysis rows returns `ComprehensiveAnalysisResult`, citation includes `AnalysisId`, billing ledger entry is created, message reload returns the structured result, question with unknown symbol returns `UnresolvedSymbols`.
- Architecture tests: `ComprehensiveAnalysisToolAdapter` does not reference `DbContext` directly; `QueryComprehensiveAnalysisUseCase` does not reference Infrastructure assemblies.

## Out of Scope

- Exposing `ComprehensiveAnalysisResult` as a separate public endpoint — all queries go through `POST /api/ai/v1/query`.
- Full semantic / vector search (RAG with embeddings) — deterministic tag-based retrieval is sufficient for structured data.
- Returning the full un-capped `Summary` to the LLM.
- Frontend rendering changes beyond what is needed to display `ComprehensiveAnalysisResult` alongside the existing `ScannerTableResult` and `SymbolLookupTable` shapes (frontend spec is separate).
- Paginated analysis browsing UI — out of scope; the AI answer returns up to 5 items.

## Coherence Rules

- This spec depends on `065` for the persistence schema and sync pipeline; it must not duplicate the upsert or sync logic.
- This spec depends on `047`/`056` for the MAF V2 workflow tool registration pattern; it follows the same narrow tool-adapter contract.
- This spec depends on `009` for `IConfidenceScoreCalculator` and `DataCitation` contracts; it must not introduce a parallel citation or confidence path.
- This spec depends on `028` for `IMissingAnswerFeedbackCollector`; feedback collection follows the existing fire-and-forget pattern.
- The `ComprehensiveAnalysisToolAdapter` is an Infrastructure concern; `QueryComprehensiveAnalysisUseCase` and `IComprehensiveAnalysisQueryRepository` are Application concerns.
- LLM must not receive raw HTML, full un-capped summaries, or direct database access.
