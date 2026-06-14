# Tasks — ComprehensiveAnalysisSync
**Feature Name:** `ComprehensiveAnalysisSync`
**Epic:** دریافت و ذخیره‌سازی تحلیل‌های جامع بازار سرمایه

---

## Data Model Design

### Entities

#### `ComprehensiveAnalysis` (Aggregate Root)
Stores each analysis post from the CyclicalWaves blog API.

| Column | Type | Notes |
|---|---|---|
| `Id` | `long` | Source API id (from `id` field) — used as PK to support idempotent upsert |
| `Title` | `string(500)` | Persian analysis title |
| `Summary` | `string(MAX)` | HTML content from `summary` field |
| `CreatedAt` | `DateTimeOffset` | UTC — from `created_at` |
| `PersianCreatedAt` | `string(30)` | From `pcreate` (e.g. `1405-03-24 13:26:32`) |
| `AuthorId` | `int` | From `user_id` |
| `AuthorName` | `string(200)` | Resolved from `categories[].name` (the analyst name) |
| `SyncedAt` | `DateTimeOffset` | UTC — last time this row was written |

> **Design decision:** Use the API `id` as the PK. This eliminates the need for a composite unique index on `(Title, CreatedAt)` and makes upsert trivial.

#### `ComprehensiveAnalysisTag`
Normalized tag table (many-to-many join). One row per tag per analysis.
Tags drive the primary query path for AI retrieval (stock symbol lookup, analysis type lookup).

| Column | Type | Notes |
|---|---|---|
| `AnalysisId` | `long` | FK → `ComprehensiveAnalysis.Id` |
| `TagId` | `int` | From `tags[].id` (source system id) |
| `TagName` | `string(200)` | From `tags[].name` (e.g. `شغدیر`, `تحلیل_تکنیکال`) |
| `TagSlug` | `string(200)` | From `tags[].slug` |
| `TagTypeId` | `int` | From `tags[].type_id` — differentiates symbol tags from topic tags |
| `IsAnalytic` | `bool` | From `tags[].analytic` — marks tags used in analysis filtering |

> **Why separate table instead of JSON column:** AI retrieval queries must filter by tag name (symbol code) and tag type efficiently. JSON columns are not indexable without computed columns, which adds complexity. A normalized table allows `WHERE TagName = 'شغدیر' AND TagTypeId = 1` with a plain composite index.

#### `ComprehensiveAnalysisCategory`
The `categories[]` in the API response represents the **analyst/author group**, not a content category. Stored as a lightweight lookup.

| Column | Type | Notes |
|---|---|---|
| `AnalysisId` | `long` | FK → `ComprehensiveAnalysis.Id` |
| `CategoryId` | `int` | From `categories[].id` |
| `CategoryName` | `string(200)` | From `categories[].name` (e.g. `نیما آزادی`) |

#### `ComprehensiveAnalysisSyncLog`
Audit log for each sync job execution.

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` | Auto-increment PK |
| `JobName` | `string(100)` | `"FullSync"` or `"DailySync"` |
| `StartedAt` | `DateTimeOffset` | UTC |
| `FinishedAt` | `DateTimeOffset?` | UTC, null if still running |
| `Status` | `string(20)` | `"Running"`, `"Completed"`, `"Failed"` |
| `PagesTotal` | `int` | Total pages across all tags |
| `ItemsSynced` | `int` | Items upserted in this run |
| `ErrorMessage` | `string(2000)?` | Last error if status is Failed |

### Indexes

```
ComprehensiveAnalysis:
  PK on Id

ComprehensiveAnalysisTag:
  PK on (AnalysisId, TagId)
  INDEX on (TagName, TagTypeId, AnalysisId)     -- AI retrieval: symbol + type filter
  INDEX on (TagName, IsAnalytic, AnalysisId)    -- AI retrieval: topic filter
  INDEX on (TagTypeId, AnalysisId)              -- bulk fetch by type

ComprehensiveAnalysisCategory:
  PK on (AnalysisId, CategoryId)
```

### AI Retrieval Query Patterns

| User Question Type | SQL Pattern |
|---|---|
| آخرین تحلیل بنیادی سهم X | `JOIN Tags WHERE TagName='X' AND TagTypeId=1` + `ORDER BY CreatedAt DESC LIMIT 1` |
| تحلیل‌های سهم X | `JOIN Tags WHERE TagName='X' AND TagTypeId=1 ORDER BY CreatedAt DESC` |
| تحلیل دلاری شاخص کل | `JOIN Tags WHERE TagName='تحلیل_دلاری' AND IsAnalytic=1` |
| تحلیل تکنیکال سهم X | `JOIN Tags t1 ON TagName='X' JOIN Tags t2 ON TagName='تحلیل_تکنیکال'` |

---

## Phase 1 — Infrastructure & Domain Setup

### TASK-001 · Domain Entities
- [ ] Create `ComprehensiveAnalysis` entity (sealed class, Guid-free — uses `long Id` from API)
- [ ] Create `ComprehensiveAnalysisTag` value object / child entity
- [ ] Create `ComprehensiveAnalysisCategory` value object / child entity
- [ ] Create `ComprehensiveAnalysisSyncLog` entity
- [ ] All entities validated at construction (no public setters on invariant fields)
- **Related US:** US-008

---

### TASK-002 · Persistence Models & EF Core Configuration
- [ ] Create `ComprehensiveAnalysisProviderDbContext` (follow existing pattern: `ApplyConfigurationsFromAssembly`)
- [ ] Create persistence row types:
  - `ComprehensiveAnalysisRow`
  - `ComprehensiveAnalysisTagRow`
  - `ComprehensiveAnalysisCategoryRow`
  - `ComprehensiveAnalysisSyncLogRow`
- [ ] Create `IEntityTypeConfiguration<T>` implementations for each row type:
  - Column types, max lengths, nullable flags
  - PK on `ComprehensiveAnalysisRow.Id` (not auto-generated — sourced from API)
  - Composite PK `(AnalysisId, TagId)` on tags
  - Composite PK `(AnalysisId, CategoryId)` on categories
  - Indexes as specified in data model section above
- [ ] Create and apply EF Core migration: `AddComprehensiveAnalysis`
- **Related US:** US-008

---

### TASK-003 · Configuration
- [ ] Add `CyclicalWavesApiOptions` settings class:
  ```csharp
  public class CyclicalWavesApiOptions
  {
      public string BaseUrl { get; set; }        // https://back1.cyclicalwaves.com/api
      public string UserName { get; set; }
      public string Password { get; set; }
      public int PageSize { get; set; }          // default: 10
      public int RequestDelayMs { get; set; }    // default: 300
      public string DailySyncCron { get; set; }  // default: "0 6 * * *"
  }
  ```
- [ ] Register in `appsettings.json` / user-secrets / environment variables
- [ ] Bind via `services.Configure<CyclicalWavesApiOptions>(config.GetSection("CyclicalWavesApi"))`
- **Related US:** US-001, US-003

---

## Phase 2 — API Client

### TASK-004 · Auth Service
- [ ] Create `ICyclicalWavesAuthService` interface + implementation
- [ ] Implement `POST /api/auth/login` → returns `TokenResponse`
- [ ] Cache token in-memory with expiry (`expires_in` - 60s buffer)
- [ ] Auto re-authenticate on 401 and retry once
- [ ] Unit tests: successful login, expired token refresh, invalid credentials throws
- **Related US:** US-001

---

### TASK-005 · API DTOs
- [ ] Create response DTOs matching the full API contract:
  ```csharp
  public record TokenResponse(string TokenType, int ExpiresIn,
      string AccessToken, string RefreshToken);

  public record AnalysisItemDto(
      long Id,
      string Title,
      string Summary,
      int UserId,
      string CreatedAt,       // ISO 8601 UTC
      string Pcreate,         // Persian datetime string
      List<CategoryDto> Categories,
      List<TagDto> Tags);

  public record CategoryDto(int Id, string Name);

  public record TagDto(
      int Id,
      string Name,
      string Slug,
      int TypeId,
      int Analytic);           // 0 or 1 from API

  public record PagedResponse<T>(List<T> Data, PageLinks Links, PageMeta Meta);
  public record PageLinks(string First, string Last, string? Prev, string? Next);
  public record PageMeta(int CurrentPage, int From, int LastPage,
      int PerPage, int To, int Total);
  ```
- **Related US:** US-007, US-008

---

### TASK-006 · Comprehensive Analysis API Client
- [ ] Create `ICyclicalWavesAnalysisClient` interface + implementation
- [ ] Implement `GET /api/blog/getComprehensiveAnalysis` with:
  - `page`, `paginate` query params
  - `filter[from_date]`, `filter[to_date]` (format: `YYYY-MM-DD`)
- [ ] Handle 401 → delegate to auth service, retry once
- [ ] Handle 500 → throw `ApiException` for retry policy upstream
- [ ] Register `HttpClient` with Polly retry policy (3 retries, exponential back-off)
- [ ] Unit tests: successful response deserialization, 401 triggers re-auth
- **Related US:** US-004, US-005, US-007

---

## Phase 3 — Sync Logic (Data Ingestion)

### TASK-007 · Upsert Repository
- [ ] Create `IComprehensiveAnalysisRepository` interface + EF Core implementation
- [ ] Method: `UpsertAsync(IEnumerable<ComprehensiveAnalysis> items)`
  - Match on `Id` (API source id); update if exists, insert if not
  - Delete and re-insert child `Tags` and `Categories` rows on update (simpler than diff)
- [ ] Method: `LogSyncRunAsync(ComprehensiveAnalysisSyncLog log)`
- [ ] Method: `UpdateSyncLogAsync(int logId, int itemsSynced, string status, string? error)`
- [ ] Unit tests with in-memory EF provider
- **Related US:** US-008

---

### TASK-008 · Full Sync Service
- [ ] Create `IComprehensiveAnalysisFullSyncService` + implementation
- [ ] Algorithm:
  1. Authenticate (US-001)
  2. Insert `SyncLog` row with `Status=Running`
  3. Fetch page 1 → read `meta.last_page`
  4. Fetch pages 2..N sequentially with `RequestDelayMs` delay between calls
  5. Map each `AnalysisItemDto` → domain entity (including all tags and categories)
  6. Upsert batch of items
  7. Update `SyncLog` with final counts and `Status=Completed`
- [ ] No date filter applied (fetch all historical data)
- [ ] On page-level error: log, skip that page, continue
- [ ] Integration test: mock HTTP, verify all pages fetched and upserted with correct tag rows
- **Related US:** US-002, US-007

---

### TASK-009 · Daily Incremental Sync Service
- [ ] Create `IComprehensiveAnalysisDailySyncService` + implementation
- [ ] Algorithm:
  1. Authenticate (re-use cached token)
  2. Set `filter[from_date]` = yesterday, `filter[to_date]` = today
  3. Fetch all pages and upsert (same page loop as full sync)
  4. Log sync run result
- [ ] Idempotent — safe to run multiple times per day
- [ ] Integration test: mock HTTP for incremental date range, verify upsert called with correct date params
- **Related US:** US-003, US-005

---

## Phase 4 — AI Retrieval Layer

### TASK-010 · Analysis Query Repository (Read Side)
- [ ] Create `IComprehensiveAnalysisQueryRepository` interface (separate from write repository — different responsibility)
- [ ] Method: `GetLatestBySymbolAsync(string symbolName, int limit = 5)`
  - Filter: `Tags WHERE TagName = symbolName AND TagTypeId = 1`
  - Order: `CreatedAt DESC`
  - Returns: list of `ComprehensiveAnalysisSummary` (Id, Title, Summary, CreatedAt, Tags)
- [ ] Method: `GetBySymbolAndTopicAsync(string symbolName, string topicTagName, int limit = 5)`
  - Filter: analysis has a tag matching symbolName (TypeId=1) AND a tag matching topicTagName (IsAnalytic=1)
  - Order: `CreatedAt DESC`
- [ ] Method: `SearchByTagNamesAsync(IReadOnlyList<string> tagNames, int limit = 10)`
  - Filter: analysis has at least one tag in tagNames
  - Order: `CreatedAt DESC`
  - Used when AI extracts multiple intent tags from the user question
- [ ] Method: `GetByIdAsync(long id)`
- [ ] All methods return domain-oriented result objects, not persistence rows
- **Related US:** US-010 (new)

---

### TASK-011 · Analysis Query Use Case
- [ ] Create `QueryComprehensiveAnalysisUseCase` (application service)
- [ ] Input: `ComprehensiveAnalysisQuery` (plain record: `SymbolName?`, `TopicTags[]?`, `Limit`)
- [ ] Delegates to `IComprehensiveAnalysisQueryRepository` based on which fields are populated
- [ ] Returns `ComprehensiveAnalysisQueryResult` (list of summaries with full `Summary` HTML)
- [ ] This use case is the boundary AI tool calls cross — AI never touches the repository directly
- **Related US:** US-010 (new)

---

### TASK-012 · AI Tool Definition for Comprehensive Analysis
- [ ] Define an AI tool (function definition) that the LLM can call:
  ```json
  {
    "name": "query_comprehensive_analysis",
    "description": "Retrieve comprehensive stock analysis posts. Use when the user asks about fundamental analysis, technical analysis, P/E, P/S, equilibrium price, suspicious volumes, or investment suitability for a stock symbol.",
    "parameters": {
      "symbol_name": "string? — Persian stock symbol code (e.g. شغدیر, کرازی, غگلپا)",
      "topic_tags": "string[]? — analysis type tags (e.g. تحلیل_تکنیکال, قیمت_تعادلی, حجم_مشکوک)",
      "limit": "int — max results, default 3"
    }
  }
  ```
- [ ] Wire tool call handler to `QueryComprehensiveAnalysisUseCase`
- [ ] Tool response format: array of `{ title, persianDate, summary (HTML stripped to plain text), tags }`
- [ ] Strip HTML from `Summary` before passing to LLM context (the HTML is not useful to the model)
- **Related US:** US-010 (new)

---

## Phase 5 — Job Scheduling

### TASK-013 · Hangfire Recurring Job Registration
- [ ] Install `Hangfire.Core`, `Hangfire.AspNetCore`, and storage package
- [ ] Register `IComprehensiveAnalysisDailySyncService` as Hangfire recurring job
  ```csharp
  RecurringJob.AddOrUpdate<IComprehensiveAnalysisDailySyncService>(
      "comprehensive-analysis-daily-sync",
      svc => svc.RunAsync(CancellationToken.None),
      options.DailySyncCron);   // "0 6 * * *"
  ```
- [ ] Enable Hangfire dashboard (protected by auth policy)
- **Related US:** US-003

---

### TASK-014 · Manual Full Sync Trigger
- [ ] Admin API endpoint: `POST /api/admin/comprehensive-analysis/full-sync` (authorized)
- [ ] Enqueues `IComprehensiveAnalysisFullSyncService.RunAsync()` as a Hangfire background job
- [ ] Returns `202 Accepted` with the `SyncLog.Id` so caller can poll status
- [ ] Add `GET /api/admin/comprehensive-analysis/sync-log/{id}` to check run status
- **Related US:** US-002

---

## Phase 6 — Observability

### TASK-015 · Structured Logging
- [ ] Add structured log enrichment for sync job runs (JobName, RunId, Page, TotalPages, ItemsSynced)
- [ ] Log at `Information` level: job started, each page fetched, job completed with summary
- [ ] Log at `Error` level: 500 after retries exhausted, unhandled exceptions
- **Related US:** US-009

---

### TASK-016 · Health Check
- [ ] Add `IHealthCheck` for CyclicalWaves API connectivity (calls auth endpoint)
- [ ] Register at `/health/cyclicalwaves`
- **Related US:** US-009

---

## Phase 7 — Testing & Review

### TASK-017 · Integration Tests — Sync Path
- [ ] Use `WireMock.Net` to simulate CyclicalWaves API
- [ ] Test full sync: all pages fetched, DB populated with correct tag rows
- [ ] Test daily sync: only yesterday–today range queried, correct `filter[from_date]` sent
- [ ] Test token expiry: 401 causes re-auth and retry
- [ ] Test page-level failure: error logged, remaining pages continue

---

### TASK-018 · Integration Tests — AI Retrieval Path
- [ ] Seed test DB with known analysis records and tags
- [ ] Test `GetLatestBySymbolAsync("شغدیر")` returns correct records ordered by date
- [ ] Test `GetBySymbolAndTopicAsync("شغدیر", "تحلیل_تکنیکال")` applies both tag filters
- [ ] Test `SearchByTagNamesAsync(["قیمت_تعادلی"])` returns all matching records
- [ ] Test AI tool call end-to-end: input question → extracted params → use case → result

---

### TASK-019 · Code Review & Merge
- [ ] PR review against feature branch `feature/comprehensive-analysis-sync`
- [ ] Ensure no hardcoded credentials
- [ ] Ensure Persian strings are handled correctly (UTF-8, URL encoding)
- [ ] Ensure HTML is stripped before content reaches LLM context

---

## Dependency Order

```
TASK-003 → TASK-001 → TASK-002
TASK-003 → TASK-004
TASK-004 → TASK-005 → TASK-006
TASK-006 → TASK-007 → TASK-008 → TASK-013
TASK-006 → TASK-007 → TASK-009 → TASK-013
TASK-008 → TASK-014
TASK-002 → TASK-010 → TASK-011 → TASK-012
TASK-008 + TASK-009 → TASK-015 → TASK-016
TASK-007 + TASK-008 + TASK-009 → TASK-017
TASK-010 + TASK-011 + TASK-012 → TASK-018
TASK-017 + TASK-018 → TASK-019
```

---

## New User Story Required

> **US-010 — بازیابی تحلیل‌ها توسط هوش مصنوعی**
>
> **As an** AI assistant,
> **I want to** query stored comprehensive analyses by stock symbol name and/or analysis type tag,
> **So that** I can answer user questions like "آخرین تحلیل بنیادی سهم کرازی چیست؟" with real content from the database.
>
> **Acceptance Criteria:**
> - AI can retrieve the latest N analyses for a given stock symbol (matched via tag name, `TagTypeId=1`)
> - AI can filter further by analysis type (matched via topic tag name, `IsAnalytic=1`)
> - AI receives plain-text content (HTML stripped from `Summary`)
> - Results ordered by `CreatedAt DESC`
> - Response includes: title, Persian date, plain-text summary, list of tag names
> - The query boundary is a defined use case — AI tool calls never bypass it
