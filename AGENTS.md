# AGENTS

read [README](README.md)

## Communication Rule

Always respond in **English**, even when the user message is in Persian.
Persian text in user queries or examples is product input — not a language instruction.

---

## System Overview

Financial Copilot is an AI-powered capital market assistant for the **Iranian stock market (Tehran Stock Exchange)**.
It answers natural language questions about stocks by routing through three specialized tools backed by persisted, normalized financial data.

Stack: .NET 10 · C# · PostgreSQL · EF Core · Clean Architecture · Microsoft Agent Framework V2

---

## AI Orchestration Architecture

All user queries enter via `POST /api/ai/v1/query` and flow through this pipeline:

```
User message
    │
    ├─ V1 path: LlmAiIntentDetector → intent branch → use case → response
    └─ V2 path: MAF Workflow (7 steps) → agent tool-calling loop → response
```

**Active mode:** `MicrosoftAgentFrameworkV2` (set in `appsettings.Development.json`)

In V2, the agent LLM reads the system prompt, decides which tools to call (and in what combination), calls them, then synthesizes a final answer. The system prompt is the primary lever for improving response quality.

---

## Available Tools

### 1. `screen_stocks`
**When:** User wants to filter or rank stocks by financial metric conditions.
**Examples:** "سهام با P/E زیر ۵", "find stocks with revenue growth over 50%"
**Input:** natural language query string
**Returns:** ranked table of matching symbols with metric values

### 2. `lookup_symbol_metrics`
**When:** User asks for specific metric values for one or more named symbols — no threshold, no filter.
**Examples:** "P/E فولاد چقدر است؟", "فروش ماهانه شغدیر را نشان بده"
**Input:** natural language query string referencing symbol(s) and metric(s)
**Always fetch:** `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `MONTHLY_SALES`, `PE_TTM`, `PS_TTM`, `EPS`
**Also fetch when relevant:** `MONTHLY_SALES_GROWTH_YOY`, `MONTHLY_PRODUCTION_QUANTITY`
**Returns:** structured metric table per symbol

### 3. `query_comprehensive_analysis`
**When:** User asks about analysis posts, technical analysis, equilibrium price, suspicious volumes, or investment suitability.
**Examples:** "تحلیل تکنیکال شغدیر", "رصد معاملات عمده کرازی", "قیمت تعادلی فملی چقدر است؟"
**Parameters:**
- `symbolNames`: Persian stock tickers e.g. `["شغدیر", "کرازی"]`
- `topicTags`: slugs from allowed list only — `تحلیل_تکنیکال`, `قیمت_تعادلی`, `رصد_معاملات_عمده`, `گزارش_فصلی`, `گزارش_ماهانه`, `نمودار_P_S`, `نمودار_P_E`
- `fromDateIso`: ISO 8601 date string (optional)
- `limit`: 1–5 (default 3)
**Returns:** narrative analysis items from CyclicalWaves with title, author, date, plain-text summary, and tags

**IMPORTANT — topic matching:** Topics are stored by `TagName` (underscore slug), not `TagSlug`. Always use the exact slug strings above.

---

## Tool Combination Rules

| User intent | Tools to call |
|---|---|
| Filter/rank stocks by condition | `screen_stocks` only |
| Ask metric value for named symbol(s) | `lookup_symbol_metrics` only |
| Ask about analysis/reports for a symbol | `query_comprehensive_analysis` only |
| **Comprehensive stock analysis** ("تحلیل شغدیر", "بررسی سهم X", "وضعیت X چطور است") | **Both** `lookup_symbol_metrics` + `query_comprehensive_analysis` in parallel |

When calling both tools, combine results into one unified answer:
- Live data (price, change %, monthly sales, P/E, P/S) from `lookup_symbol_metrics`
- Expert narrative and reports from `query_comprehensive_analysis`

---

## Intent Detection (V1 path)

`LlmAiIntentDetector` classifies each message before routing:

| Intent | Trigger |
|---|---|
| `Scanner` | Condition + threshold on many stocks (e.g. "P/E زیر ۱۰") |
| `SymbolLookup` | Named symbol(s) + metric value request, no threshold |
| `ComprehensiveAnalysis` | تحلیل · بررسی · بررسی کن · وضعیت · ارزیابی · نظرت چیه · چطوره · گزارش · رصد معاملات · تحلیل تکنیکال · تحلیل بنیادی · قیمت تعادلی · نمودار P/E · نمودار P/S · تحلیل جامع · analyze · review |
| `Clarification` | Message too vague AND no symbol mentioned |
| `Unknown` | None of the above |

Key disambiguation:
- **Scanner** = operator + threshold, filtering many stocks
- **SymbolLookup** = specific metric value for named symbol(s), no threshold
- **ComprehensiveAnalysis** = any general/analytical question about a named stock, OR published analysis content/reports
- Rule: when a symbol is named alongside words like بررسی, وضعیت, ارزیابی → always ComprehensiveAnalysis, never Clarification

---

## Query Parser for ComprehensiveAnalysis (V1 path)

`LlmComprehensiveAnalysisQueryParser` extracts structured parameters from the user message:

```json
{
  "symbolNames": ["شغدیر"],
  "topicTags": ["تحلیل_تکنیکال"],
  "fromDateHint": "this_month",
  "limit": 3
}
```

`fromDateHint` accepted values: `yesterday` · `this_week` · `last_week` · `this_month` · `last_month` · ISO date string · `null`

Returns `ClarificationRequired` when all three filters (symbolNames, topicTags, fromDate) are empty.

---

## Response Shape

```
AiQueryResponse {
    Intent:                     Scanner | SymbolLookup | ComprehensiveAnalysis | ...
    ScannerTable:               structured metric table (Scanner intent)
    SymbolLookupTable:          structured metric table (SymbolLookup or combined)
    ComprehensiveAnalysisResult:narrative items (ComprehensiveAnalysis or combined)
    ExplainableAnswer:          filter chips, evidence, citations (Scanner)
    ConfidenceScore:            0–1 score with factors
    TextAnswer:                 plain text (Unknown intent fallback)
    ClarificationRequired:      bool
    ClarificationMessage:       string
}
```

---

## Sample Questions and Expected Behavior

### Q1 — Comprehensive stock analysis (combined tools)
**User:** `سهام شغدیر را تحلیل کن`
**Expected:**
- Intent: `ComprehensiveAnalysis`
- Tools called: `lookup_symbol_metrics` (LATEST_PRICE, DAILY_CHANGE_PCT, MONTHLY_SALES, PE_TTM, PS_TTM, EPS) + `query_comprehensive_analysis` (symbolNames=["شغدیر"], limit=3)
- Response: live price + metrics block, followed by narrative summaries from analysis posts
- Must NOT return "تحلیل جامعی پیدا نشد" if شغدیر has tagged entries in the database

### Q2 — Technical analysis for a symbol
**User:** `تحلیل تکنیکال شغدیر`
**Expected:**
- Intent: `ComprehensiveAnalysis`
- Tool: `query_comprehensive_analysis` (symbolNames=["شغدیر"], topicTags=["تحلیل_تکنیکال"])
- Response: analysis posts tagged تحلیل_تکنیکال for شغدیر
- **Critical:** topicTags filter must match on `TagName` column, not `TagSlug`

### Q3 — Metric value lookup
**User:** `P/E فولاد چقدر است؟`
**Expected:**
- Intent: `SymbolLookup`
- Tool: `lookup_symbol_metrics` with PE_TTM for فولاد
- Response: structured metric table with current P/E value and freshness

### Q4 — Scanner screening
**User:** `سهام با P/E زیر ۵ و رشد سود بالای ۵۰ درصد`
**Expected:**
- Intent: `Scanner`
- Tool: `screen_stocks`
- Response: ranked list of matching symbols with conditions explained

### Q5 — Suspicious volume radar
**User:** `رصد معاملات عمده کرازی`
**Expected:**
- Intent: `ComprehensiveAnalysis`
- Tool: `query_comprehensive_analysis` (symbolNames=["کرازی"], topicTags=["رصد_معاملات_عمده"])
- Response: radar/surveillance posts for کرازی

### Q6 — Recent monthly sales report
**User:** `آخرین گزارش فروش ماهانه شپدیس`
**Expected:**
- Intent: `ComprehensiveAnalysis`
- Tool: `query_comprehensive_analysis` (symbolNames=["شپدیس"], topicTags=["گزارش_ماهانه"])
- Response: most recent monthly sales report posts

### Q7 — Equilibrium price
**User:** `قیمت تعادلی فملی چقدر است؟`
**Expected:**
- Intent: `ComprehensiveAnalysis`
- Tool: `query_comprehensive_analysis` (symbolNames=["فملی"], topicTags=["قیمت_تعادلی"])
- Response: equilibrium price analysis posts for فملی

### Q8 — Vague analysis request without symbol
**User:** `تحلیل بده`
**Expected:**
- Intent: `Clarification`
- Response: ask user to specify symbol name, analysis type, or time range

### Q9 — "بررسی" (review/check) a stock
**User:** `سهم شغدیر را بررسی کن`
**Expected:**
- Intent: `ComprehensiveAnalysis` (NOT Clarification — "بررسی" + symbol name = analysis request)
- Tools called: `lookup_symbol_metrics` + `query_comprehensive_analysis` in parallel
- Response: live metrics + faithful presentation of analysis post content
- **FAITHFULNESS check:** if PlainTextSummary contains "P/E فعلی 5.4" or "ارزش ذاتی 3753 تومان", those exact figures must appear in the response
- Must NOT return generic market commentary when specific numbers exist in the database

---

## Date Window Policy — ComprehensiveAnalysis

**Default:** when the user does not specify a date or time range, limit results to the **last 3 months** (90 days back from `now`).

This is enforced in `ComprehensiveAnalysisQueryUseCase.ExecuteAsync`:
- If `request.FromDate` is `null` → set `effectiveFrom = now.AddMonths(-3)`
- If `request.FromDate` is set by the parser (user said "این ماه", "هفته گذشته", ISO date, etc.) → use that value as-is
- The 3-month window applies to **all** query paths: symbol-only, topic-only, and combined

**Rationale:** analysis posts older than 3 months are rarely actionable for users. Enforcing the window keeps responses fresh and prevents the LLM from being given stale data as if it were current.

If the user explicitly asks for older records (e.g. "تحلیل سال گذشته"), the parser will set `fromDateHint` to a past ISO date, which overrides the default window.

---

## Known Data Constraints

- `ComprehensiveAnalyses` table: analysis posts synced from CyclicalWaves via daily + full-sync jobs
- Symbol tags have `TagTypeId = 1`; topic/analytic tags have `IsAnalytic = true`
- Topic slugs (e.g. `تحلیل_تکنیکال`) are stored in `TagName`, NOT `TagSlug` — filter accordingly
- `PlainTextSummary` column: HTML-stripped version of `Summary`; capped at 2000 chars when returned to LLM
- `SyncedAt`: timestamp of last sync — used for data freshness assessment
- Metric codes are case-sensitive: use `PE_TTM`, `PS_TTM`, `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `MONTHLY_SALES`, `EPS`
- **Default date window:** 3 months back from now — enforced in `ComprehensiveAnalysisQueryUseCase`, not in the repository or parser

---

## Loop Engineering Checklist

When evaluating a response against a sample question, verify:

- [ ] Correct intent detected
- [ ] "بررسی", "وضعیت", "ارزیابی" + symbol name → `ComprehensiveAnalysis` (NOT `Clarification`)
- [ ] Correct tool(s) called (single or combined as required)
- [ ] For combined calls: both `SymbolLookupTable` and `ComprehensiveAnalysisResult` present in response
- [ ] No false "not found" when data exists within the last 3 months
- [ ] Default 3-month window applied when user does not specify a date
- [ ] Topic tag filter uses `TagName` (not `TagSlug`)
- [ ] Symbol filter uses `TagTypeId = 1` and `TagName`
- [ ] Response language matches user message language (Persian → Persian)
- [ ] Confidence score present when SymbolLookup or Scanner result returned
- [ ] `PlainTextSummary` not empty (backfill endpoint called if needed)
- [ ] **FAITHFULNESS:** specific numbers from `PlainTextSummary` (ارزش ذاتی, P/E, P/S, قیمت تعادلی, سود, تقسیم سود) appear verbatim in the AI response — not paraphrased or generalized
- [ ] Analysis title and date cited in response so user can identify the source
- [ ] AI conclusions from source text (e.g. "سوپر مفت", "ارزنده") relayed directly, not softened
