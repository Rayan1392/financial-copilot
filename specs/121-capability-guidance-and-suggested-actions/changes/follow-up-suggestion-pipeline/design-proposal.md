# Change to Feature 121 — one follow-up suggestion pipeline for every answer

Status: Gate A and Gate B: Approved by the owner on 2026-11-11

Approval record: «Gate A و B تأیید با پیشنهادات تو» (2026-11-11): the design and its test plan, with the recommended
answers — question 1 (b) single-symbol provider included; question 2 yes; question 3 yes (fallback runner included).

## Request (owner, 2026-11-11)

«در انتهای پرسش های ترازنامه، صورت وضعیت x کادرهای پیشنهاد وجود ندارد. این بخش پیشنهادات مرتبط با همان پرسش، در
انتهای هر پاسخ را بصورت Design Pattern پیاده سازی کن که همه پاسخ ها از آن پایپلاین عبور کنند و شامل کادرهای پیشنهادی
مرتبط باشند. این تغییر نباید طوری پیاده سازی شود که پاسخ هایی که در حال حاضر کادر پیشنهاد دارند تغییری در
پیشنهادات شان داده بشه.»

## Discovery

- «ترازنامه X» and «صورت وضعیت X» (PD-27 alias) are the Feature 154 balance-sheet report:
  `financial_statement_table` with `FinancialStatementTableResult.BalanceSheetReport`.
- Suggestions are chosen in two places:
  1. `FinancialCopilotWorkflowDefinition` (main V2 workflow, ~line 1463): an if/else chain of 8 result-specific
     services, in this order — Symbol Snapshot, assistant help, product revenue mix (075), monthly sales products (137),
     product trend (138), monthly sales quality ranking, quarterly report (144), statement analysis. First match wins.
  2. Otherwise `MessagePersistenceFunction` calls `CapabilityGuidanceService.Suggest`, which **returns nothing for an
     `Answered` outcome** (`CapabilityGuidanceContracts.cs:81`); it only helps unanswered, partial, clarification or
     unavailable outcomes.
- So every answered result outside the 8 gets no suggestions: the balance-sheet report, and also other tables
  (non-quarterly statements), metric lookups (P/E, price), a found comprehensive analysis with metrics, disclosures,
  P/S gauge, value search, relative valuation, screening.
- The fallback runner `FinancialCopilotAgentWorkflowRunner` (semantic path, "2-fallback") never calls the 8 services; it
  only gets the guidance service.
- The existing services check each suggestion by running its text through the deterministic interpreter, so a
  suggestion always routes to the capability it names. Maximum 4 actions (guidance).

## Proposed design — Chain of Responsibility over result-specific providers

1. `IFollowUpSuggestionProvider` (Application): `Order`, `CanHandle(FollowUpSuggestionContext)`,
   `BuildAsync(context)`. The context holds the request, the outcome, the reply language and the typed results.
2. `FollowUpSuggestionPipeline`: asks the providers in `Order`; the **first** that can handle the answer returns the
   actions; if none can, the existing `CapabilityGuidanceService.Suggest` runs exactly as today.
3. **No change for answers that have suggestions today:** the 8 existing services become 8 thin provider adapters with
   the same conditions and the same order; their code and texts are not touched. Characterization tests are written
   first, against today's code, for each of the 8 and for the guidance outcomes, and must stay green after the change.
4. New providers, placed after the 8 (so they can never pre-empt one):
   - **Balance-sheet report** (successful Feature 154 report): «گزارش فصلی X», «روند فروش ماهانه X را نشان بده», and
     «ترازنامه تلفیقی X» when `ConsolidatedAlternativeAvailable` (question 2).
   - **Single-symbol answer** (question 1): any other answered or partial answer about one symbol gets the related
     capabilities it did not just run, from a fixed list («گزارش فصلی X», «روند فروش ماهانه X را نشان بده», «ترازنامه
     X», «تحلیل جامع X» when comprehensive analysis is available).
   Every text is checked through the interpreter as the existing services do; a text that would not route to its
   capability is dropped. At most 4.
5. Both V2 paths call the same pipeline: the main workflow replaces its if/else chain, and the fallback runner calls it
   instead of only the guidance service (question 3).
6. V1 is frozen and unchanged. Telegram shows the same `SuggestedActions` (no renderer change).

### Alternatives

- Add one more `else if` for the balance sheet. Rejected: the owner asked for one pipeline every answer passes through,
  and the chain already has 8 branches in one method.
- Change `CapabilityGuidanceService` to suggest on `Answered`. Rejected: it would also change unanswered-outcome
  suggestions that work today.

## Questions for the owner

1. Scope: (a) balance-sheet report only, or (b) also the single-symbol provider so every answered one-symbol question
   gets related suggestions? Questions about many symbols (screening, rankings) stay without suggestions for now.
   Recommendation: (b), matching «همه پاسخ ها … شامل کادرهای پیشنهادی مرتبط باشند».
2. Balance-sheet suggestions: «گزارش فصلی X», «روند فروش ماهانه X را نشان بده», and «ترازنامه تلفیقی X» only when a
   consolidated report exists. Recommendation: yes.
3. The fallback runner path also passes through the pipeline (today it shows no suggestion after an answer).
   Recommendation: yes.

## Results (2026-11-11, branch `track/a-routing-answer-correctness`)

- Implemented: `FollowUps/FollowUpSuggestionPipeline.cs` (context, result, provider port, pipeline),
  `ExistingFollowUpSuggestionProviders.cs` (8 adapters, conditions copied verbatim, orders 10–80),
  `RelatedCapabilityFollowUpProviders.cs` (balance sheet 90, single symbol 100, `RelatedFollowUpActionFactory`).
  `FinancialCopilotWorkflowDefinition` replaces its if/else chain with the pipeline; `FinancialCopilotAgentWorkflowRunner`
  passes the pipeline result to persistence.
- Deviations inside the approved intent: (1) the new providers apply to **Answered only**, not «answered or partial»:
  a partial answer already gets guidance follow-ups today, so handling it would change existing suggestions;
  (2) the new factory does not require exactly one entity mention, because the deterministic interpreter lists untyped
  words («ترازنامه», «تلفیقی») as mentions; it requires the capability, no missing slot and the symbol among the mentions.
- Before/after evidence: a probe of 24 questions on the pre-pipeline commit ce1ff2e and on this code — no existing
  suggestion changed (greeting, quarterly report, NoData, unavailable topic, clarification, unsupported); new boxes on the
  balance sheet and on answered single-symbol questions (P/E, monthly sales, found comprehensive analysis, income table).
- Tests: `FollowUpSuggestionPipeline121Tests` (16 unit), `FollowUpSuggestionPipeline121EndpointTests` (7 endpoint, with
  the pre-pipeline ids of the quarterly follow-ups pinned); related suites green.

## Tests (plan)

- Characterization first: the 8 existing providers' actions (ids, texts, order) and the guidance outcomes, captured on
  today's code.
- Unit: pipeline order and first-match; balance-sheet provider (with and without the consolidated alternative, failed
  report → none); single-symbol provider excludes the capability just run, respects availability, max 4.
- Integration: «ترازنامه فولاد» and «صورت وضعیت فولاد» return suggestions; a quarterly report and a Symbol Snapshot
  return exactly today's suggestions.
