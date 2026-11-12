# AGENTS

read [README](README.md)

`CLAUDE.md` is the primer for every agent (rules to read before coding and the lifecycle summary);
this file adds agent-neutral pointers and does not duplicate it.

## Agent Communication Language

- Reply in the language of the user's message: a Persian message gets a Persian reply, an English message
  an English reply. For a mixed message, use the language of the user's own words (not of pasted or quoted
  material). Persian sample questions, symbols, or UI text inside a request are product input and do not by
  themselves make the message Persian.
- The same applies to progress reports, technical explanations, questions, and task completion summaries
  within that conversation turn.
- Code, identifiers, file and folder names, commit messages, and technical terms in backticks stay English
  in every language.
- Use English for newly created technical specifications, implementation plans, task descriptions, and
  developer documentation unless an existing document explicitly requires another language.
- Preserve Persian text when it represents UI labels, localization resources, business terminology,
  user-facing content (including `CHANGELOG.md` release lines), or quoted source material.
- Do not translate existing Persian documentation unnecessarily.
- An explicit user request for a specific language overrides this rule for that request.

---

## Feature Development Lifecycle

- Follow `specs/FEATURE_DEVELOPMENT_WORKFLOW.md` for every new feature, material change to an existing
  feature, bug fix, and hotfix. `CLAUDE.md` has the step-by-step summary.
- A feature request starts with discovery and a proposed design. Do not automatically create
  `user-story.md` or `tasks.md`, or modify production code.
- Obtain explicit Gate A design approval before finalizing feature specifications and implementation tasks.
  Obtain separate explicit Gate B implementation-plan approval before changing production code, database
  migrations, or application behavior. One approval covers both gates only when the user explicitly
  authorizes both.
- Existing specifications are not evidence of approval. Preserve existing feature files and establish their
  approval status from explicit user approval.
- Bug fixes and urgent hotfixes follow the documented review and implementation authorization rules;
  urgency does not remove the implementation approval gate.
- Do not pick a feature to work on yourself. `specs/implementation-checklist.md` is a historical completion
  ledger, not a work queue. Open decisions live in `specs/open-questions.md`.
- Rules: `.claude/rules/` (Claude Code) and `.codex/rules/` (Codex) hold the same rule files, including
  `spec-driven-workflow.md`. The two folders must stay identical: change both or neither.

---

## Versions and Releases — read before any release, tag, or deploy

[`CHANGELOG.md`](CHANGELOG.md) is the **single source** of Sapio's version numbers and per-version release
notes (Keep a Changelog 1.1.0 + SemVer 2.0.0). The app's version link and `/releases` page are built from it,
and the production deploy refuses a `vX.Y.Z` tag that does not match its newest release.

- Policy and steps: [`docs/release/release-process.md`](docs/release/release-process.md)
- Owner: the [`release-manager`](.claude/agents/release-manager.md) agent. Delegate release, tagging and
  changelog work to it.
- Every user-visible change merged to `develop` adds a Persian, user-facing line under `## [Unreleased]`.
- Never write a version number anywhere else, never move or delete a pushed tag, and never push a `v*` tag
  without the owner's go-ahead: each `v*` push deploys to production.

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

**V1 is frozen for new work.** No new feature, capability, metric, intent, or route may be added to
the V1 path. See [`specs/POLICY-V1-FREEZE.md`](specs/POLICY-V1-FREEZE.md) before touching any V1 code.

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

**Source and answer (Feature 159):** the tool reads the CyclicalWaves v2 analyses (`cyclicalwaves."Analyses"`) in
CyclicalWaves mode, and in NoavaranAmin mode only when `FinancialData:CapabilitySources:comprehensive_analysis` is
`CyclicalWaves`; otherwise it is not offered (C2). An analysis matches a ticker when the ticker is a whole word of its
title; tags do not select analyses for a ticker. The provider `summary` is the whole paid analysis text, so the query
returns only a preview (at most 30% and 300 characters, none under 150; `ComprehensiveAnalysis:Teaser*`). The tool text
is a fixed card for the latest analysis only: `### <title>`, «تاریخ انتشار: <date>», the preview, «متن کامل این تحلیل را
در بخش تحلیل جامع، تحلیل اپ بخوانید» and `[ادامهٔ این تحلیل در تحلیل‌اپ](<ComprehensiveAnalysis:TahlilAppAnalysesUrl>)`.
When nothing matches: «برای نماد X تحلیلی در تحلیل‌اپ پیدا نشد.» plus `[تحلیل‌های جامع تحلیل‌اپ](<url>)`.
Max plan (change `max-plan-full-analysis-text`): a user whose effective plan has the enabled plan capability
`ComprehensiveAnalysis.FullText` (seeded for Max only; port `IComprehensiveAnalysisFullTextEntitlement`, fail closed,
2-second budget) gets the whole text instead of the preview, without the read-more sentence, and the link line
`[تحلیل‌های جامع تحلیل‌اپ](<url>)`; Telegram cuts that card to one message. API clients and the LLM tool path always
get the preview.

**IMPORTANT — topic matching:** Topics are stored by `TagName` (underscore slug), not `TagSlug`. Always use the exact slug strings above.

---

## Symbol Detection Rule

If the user's message contains **any stock symbol or company name** (شغدیر, فملی, کگل, فارس, اخابر, …):
- Immediately call `query_comprehensive_analysis` for that symbol — do NOT ask clarifying questions.
- Do NOT say "لطفاً نام متریک موردنظرتان را مشخص کنید" or "منظورتان تکنیکال است یا بنیادی؟"
- The symbol alone is sufficient to begin retrieval.

---

## Intent Classification Rules

Classify intent BEFORE choosing tools:

| Intent | Triggers | Tools |
|---|---|---|
| **Analysis** | تحلیل · بررسی · بررسی کن · ارزنده است؟ · نظر · وضعیت · ارزیابی · چطوره · آخرین تحلیل · review · analyze · opinion | `query_comprehensive_analysis` + `lookup_symbol_metrics` in parallel |
| **Financial Metric** | P/E · P/S · EPS · فروش · درآمد · سود خالص · حاشیه سود · ارزش بازار · تولید ماهانه · ROE · ROA · نسبت جاری | `lookup_symbol_metrics` **only** — never `query_comprehensive_analysis` |
| **Screening** | condition + threshold across many stocks ("P/E زیر ۵") | `screen_stocks` only |

**Critical disambiguation:**
- "P/E شغدیر" → Financial Metric → `lookup_symbol_metrics` only
- "شغدیر را بررسی کن" → Analysis → both tools
- "تحلیل شغدیر" → Analysis → both tools
- "شغدیر ارزنده است؟" → Analysis → both tools
- Never use `query_comprehensive_analysis` as the primary source for financial metric queries

---

## Tool Combination Rules

**For Analysis intent:**
1. `query_comprehensive_analysis` result → present verbatim (see Faithfulness Rule)
2. `lookup_symbol_metrics` result → present as live metrics block
3. Only fall back to AI reasoning if `query_comprehensive_analysis` returns zero results

**For Financial Metric intent:**
- `lookup_symbol_metrics` only — return the value directly, do NOT summarize analyst reports

**Symbol Detection:** if a symbol is present in the message, do NOT ask clarifying questions — immediately classify intent and call the appropriate tool(s).

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
- Intent: `SymbolLookup` (Financial Metric — not Analysis)
- Tool: `lookup_symbol_metrics` **only** — do NOT call `query_comprehensive_analysis`
- Response: structured metric table with current P/E value and freshness
- Must NOT return analyst report summaries

### Q3b — Metric lookup with symbol only pattern
**User:** `P/E شغدیر`
**Expected:**
- Intent: `SymbolLookup` (Financial Metric)
- Tool: `lookup_symbol_metrics` only
- Response: PE_TTM value for شغدیر directly from financial data
- Must NOT call `query_comprehensive_analysis` even though شغدیر is mentioned

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

**Default:** when the user does not specify a date or time range, limit results to the **last 30 days** from `now`.

This is enforced in `ComprehensiveAnalysisQueryUseCase.ExecuteAsync`:
- If `request.FromDate` is `null` → set `effectiveFrom = now - 30 days`
- If `request.FromDate` is set by the parser (user said "این ماه", "هفته گذشته", ISO date, etc.) → use that value as-is
- The 30-day window applies to **all** query paths: symbol-only, topic-only, and combined

**Rationale:** only analyses from the last 30 days are considered current and actionable.

If no results exist within 30 days, respond: "تحلیل جدیدی از نماد {symbol} در ۳۰ روز گذشته یافت نشد."
Do NOT generate your own analysis or speculate.

If the user explicitly asks for older records (e.g. "تحلیل سال گذشته"), the parser sets `fromDateHint` to a past ISO date, which overrides the 30-day default.

---

## Faithfulness Rule — ComprehensiveAnalysis Response

When `query_comprehensive_analysis` returns results, the AI **MUST**:

1. Present the author's statements, numbers, and conclusions **exactly as written** in `PlainTextSummary`
2. **NOT** paraphrase, generalize, or soften any numeric fact (ارزش ذاتی, P/E, P/S, قیمت تعادلی, سود, تقسیم سود, EPS)
3. **NOT** rewrite conclusions — if source says "سوپر مفت" or "ارزنده", use those exact words
4. **NOT** add its own technical analysis, support/resistance levels, or valuation estimates
5. **NOT** expand content with AI-generated commentary
6. Only **MAY**: add section headers, improve readability, translate section titles

**Required output format:**
```
آخرین تحلیل یافت‌شده برای {symbol}:
تاریخ: {PersianCreatedAt}

[sections from PlainTextSummary verbatim]

منبع: ComprehensiveAnalyses | نویسنده: {AuthorName}
```

**If no analysis found:**
```
تحلیل جدیدی از نماد {symbol} در ۳۰ روز گذشته یافت نشد.
```
(Do not generate analysis. Do not speculate. Do not use market knowledge to fill the gap.)

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

- [ ] Correct intent detected: Analysis vs Financial Metric vs Screening
- [ ] "بررسی", "وضعیت", "ارزیابی" + symbol name → Analysis intent → both tools (NOT `Clarification`)
- [ ] "P/E X", "EPS X", "فروش X", "ROE X" → Financial Metric intent → `lookup_symbol_metrics` only (NOT `query_comprehensive_analysis`)
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
