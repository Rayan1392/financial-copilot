# BUG-020: technical-analysis question and the English no-analysis prefix

Status: Gate A: Approved by the owner on 2026-11-11
Status: Gate B: Approved by the owner on 2026-11-11 (both gates in one answer, bug-fix path)

Owner answers 2026-11-11: §4.1 = (a) not-found line, then the latest analysis card of X; §4.2 = yes: «تکنیکال X» and «پیوت X» search TahlilApp first and behave like «تحلیل تکنیکال X» while comprehensive analysis is available; the refusal stays only when it is unavailable.

| Field | Value |
|---|---|
| Layers / Area | backend · AI, FinancialData |
| Priority (proposed) | P1 (the English prefix is live since v1.8.0) |
| Complexity (proposed) | S |
| Depends On | 162 (merged), Feature 150 change `unavailable-capability-refusals-and-answer-correctness` (same branch) |
| Request | Owner, 2026-11-11: «تگ تکنیکال به دنبال تحلیل مرتبط در تحلیل اپ بگرده اگر بود همان رفتاری رو بکنه که با پرسش تحلیل جامع فولاد میکنه» |

## 1. Discovery (develop `da623ed` + track A)

| Finding | Evidence |
|---|---|
| «تحلیل تکنیکال X» routes to `comprehensive_analysis`, and the enricher adds topic tag `تحلیل_تکنیکال`. | `SemanticQueryFrameEnrichment.cs:89` |
| The v2 query keeps only analyses carrying that tag. Its own note says most articles carry no tags; titles name the ticker. So a technical question usually finds nothing, even when «تحلیل جامع X» finds the latest analysis. | `CyclicalWavesV2ComprehensiveAnalysisQuery.cs:12-13`, `:55-58` |
| When no analysis is found, the reply starts with the English «Found metric data for 1 symbol(s).»: the workflow joins the lookup's English `AgentSummary` before the Persian card. This also happens for «تحلیل جامع فملی», so it is in production now, not only for technical questions. | `FinancialCopilotWorkflowDefinition.cs:536-538`; agent runner `:124-125`; reproduced by `Bug020_NoAnalysisReply_HasNoEnglishPrefix` (fails for both questions) |
| Real tag data could not be checked: the local read-only role has no access to the `cyclicalwaves` schema. | psql 2026-11-11 |

## 2. Proposed fix

- **F1 (English prefix):** an analysis turn's reply is the Persian card only, found or not; the lookup's English summary is never prepended. Both V2 runners.
- **F2 (technical topic):** for topic `تحلیل_تکنیکال`, an analysis of X is "technical" when it carries the tag **or** its title contains the word «تکنیکال». The latest such analysis is shown exactly as for «تحلیل جامع X» (title, date, preview, link, same charge).
- **F3 (none technical):** owner decision, see §4.
- Other topic tags (`قیمت_تعادلی`, `گزارش_فصلی`, …) unchanged.

## 3. Tests (failing first)

- E2E: «تحلیل تکنیکال فولاد» with a seeded technical-titled analysis → that card; with only a tagged one → that card; «تحلیل جامع فملی» and the none-technical case → no English text.
- Unit: v2 query title rule (whole word «تکنیکال», Arabic letters), tag rule unchanged.
- Feature 159 tests unchanged and green.

## 4. Questions for the owner

1. F3, no technical analysis for X: (a) «برای نماد X تحلیل تکنیکالی در تحلیل‌اپ پیدا نشد.» followed by the latest analysis card of X, as for «تحلیل جامع X»; (b) only the Persian not-found line and the TahlilApp link, free.
2. «تکنیکال X» without «تحلیل» is now refused (Feature 150 change). Should it also search for a technical analysis first (when comprehensive analysis is available), and refuse only when none exists?

## 5. Results (2026-11-11, branch `track/a-routing-answer-correctness`, uncommitted)

- F1 done in both V2 runners: «تحلیل جامع فولاد» and «تحلیل جامع فملی» now start with the Persian card. This was live in production for every analysis reply with context metrics, found or not.
- F2 done: tag or whole title word «تکنیکال» (Persian or Arabic letters) in `CyclicalWavesV2ComprehensiveAnalysisQuery`.
- §4.1 (a) done in `ComprehensiveAnalysisCapabilityExecutor`: one more query without the technical topic; the card starts with «برای نماد X تحلیل تکنیکالی در تحلیل‌اپ پیدا نشد.» (web and Telegram). No analysis at all → the existing free no-analysis card.
- §4.2 done in `ConversationDialogueGate.TryInterpretTechnicalAnalysisAsync` (V2, NoavaranAmin base, comprehensive analysis available, one company, no pending question). Valuation and advice questions are still refused.
- Implementation detail inside scope: the company of a topic question is resolved by exact ticker first, then by the deterministic interpreter's mention without the topic words, because Noavaran rows often keep the symbol in `TseSymbol`/`CompanySymbol` and not `Ticker` (test `TopicQuestion_ForACompanyWithoutTicker_FillsTheSymbolFromItsSymbolColumns`). The same resolution now fills the refusal's follow-ups.
- Tests: new E2E 9 cases, unit 11; changed on purpose: the C3 unit theory no longer lists «تحلیل تکنیکال فولاد» (it now skips the model). Full run: unit 32 = baseline, architecture BUG-003, integration 186 = the same set as `develop` `da623ed`. Two Telegram unit tests failed once in the parallel full run and passed in three isolated and full unit runs (flaky under load).
- Not checked: real tag and title data (local read-only role cannot read `cyclicalwaves`).
