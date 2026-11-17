# Bug fix proposal — «سهم X را تحلیل کن» / «سهم X را بررسی کن» must take the analysis route (BUG-032)

**Status: Gate A and Gate B approved by the owner on 2026-11-17** («Gate A و B تأیید، پیاده‌سازی کن»), branch
`fix/bug-032-analysis-phrase-route`.

## Feature Folder   `159-capability-source-map-comprehensive-analysis/changes/bug-032-analysis-phrase-route`
## Layers / Area    backend · AI
## Priority         to be set by the owner (proposed: P1, user-visible wrong answer and wrong charge)
## Complexity       S
## Depends On       159 BUG-022 change (done), 159 `analysis-recency-window` (done), 163 (done)
## References       `specs/bugs.md` BUG-032, `specs/159-capability-source-map-comprehensive-analysis/changes/bug-022-analysis-word-route/design-proposal.md`, `specs/159-capability-source-map-comprehensive-analysis/changes/analysis-recency-window/design-proposal.md`, `specs/163-symbol-grounded-fallback-answer/user-story.md`, `specs/open-questions.md` OQ-098, `src/backend/FinancialCopilot.Application/AI/Orchestration/UnavailableTopics/UnavailableTopicCatalog.cs`, `src/backend/FinancialCopilot.Application/AI/Orchestration/ConversationTaskStateContracts.cs` (`TryInterpretTechnicalAnalysisAsync`), `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Adapters/ComprehensiveAnalysisToolAdapter.cs`

## 1. Report (owner, 2026-11-17, production v1.15.0)

«سهم شکبیر را تحلیل کن» → «برای نماد شکبیر تحلیلی در تحلیل‌اپ پیدا نشد.», the «تحلیل‌های جامع تحلیل‌اپ» link, a scanner
metrics table (last price, sales, P/E, P/S, EPS «Missing»), «اطمینان ۵۹٪», 1 credit. Owner: the question should have
reached Feature 163.

## 2. Discovery (from code; production logs were not read)

| # | Finding |
|---|---|
| F1 | BUG-022 added a deterministic gate (`TryInterpretTechnicalAnalysisAsync`) that sends an analysis request straight to `comprehensive_analysis` without the model. It applies only when `UnavailableTopicCatalog.AnalysisRequestCompanyText` leaves text that resolves to exactly one company. |
| F2 | That method removes only «تحلیل», «جامع», «بنیادی», «تکنیکال», «پیوت», «ها», «های», and requires «تحلیل». From «سهم شکبیر را تحلیل کن» the rest is «سهم شکبیر را کن», which does not resolve, so the gate returns null. «سهم X را بررسی کن» has no «تحلیل» and is never considered. `QueryNormalization.Normalize` changes characters only, not words. |
| F3 | The model then chooses the route. The reply matches the BUG-022 symptom exactly (no-analysis sentence + metrics table + confidence line + 1 credit, `AiQuery.Scanner`), so the turn ran on a model-chosen route that also called the analysis tool. |
| F4 | That turn ends `Completed`. Feature 163 replaces only `Unsupported` or `NoData` with `comprehensive_analysis_not_recent` (`SymbolGroundedFallbackEligibility.MayReplace`), so the OQ-098 rule ("no recent analysis → 163") is never applied. |
| F5 | Secondary: `ComprehensiveAnalysisToolResult.NotFound` does not keep the query response, so `RecentWindowDays` is null and the agent-tool card shows the old sentence «… تحلیلی در تحلیل‌اپ پیدا نشد.» instead of the OQ-099 text «… تحلیل تازه‌ای در ۳۰ روز اخیر در تحلیل‌اپ پیدا نشد.». |
| F6 | Not verified: whether `SymbolGroundedFallback:Enabled` is on in the production `.env`. With it off, the corrected route gives the free OQ-099 card, not a 163 answer. |

## 3. Owner decision (2026-11-17)

«بررسی کن» is an analysis request too: «سهم X را بررسی کن» takes the same route as «سهم X را تحلیل کن».

## 4. Proposed fix (no material design change)

This restores the approved behaviour of BUG-022 and of `analysis-recency-window` / OQ-098 for more phrasings. Routing
rules, billing, 163 eligibility and texts stay as approved.

- **D1 Analysis words.** «بررسی» counts as an analysis word, alongside «تحلیل» (the method requires one of them).
- **D2 Filler words.** These whole words are removed before company resolution: «سهم», «نماد», «شرکت», «را», «کن»,
  «بکن», «کنید», «بکنید». The rest must still resolve to exactly one company through `ResolveMentionAsync`. If it
  does not, today's route is kept, so «تحلیل فروش فولاد» and «بررسی صورت سود و زیان فولاد» are unchanged.
- **D3 Valuation and advice stay out.** The existing rule is unchanged: a message that matches a valuation or advice
  topic («ارزش ذاتی سهم X را بررسی کن») is not a plain analysis request.
- **D4 Agent-tool card.** `ComprehensiveAnalysisToolResult.NotFound` keeps the window (`RecentWindowDays`) so the card
  shows the OQ-099 sentence on every route.

### Alternatives

- Give the deterministic interpreter a veto in `SemanticArbitrator`. Rejected again (as in BUG-022): it changes
  arbitration for every capability.
- Let 163 also replace a `Completed` turn whose analysis tool found nothing. Rejected: it widens 163 eligibility (a
  design change) and keeps the route dependent on the model.

### Risks

- A company whose name contains a filler word as a whole word («شرکت …»). The rest still has to resolve to one
  company; if removal breaks resolution, the message keeps today's route (no wrong answer, only no improvement).
- «بررسی» is more general than «تحلیل». The gate fires only when nothing but filler words and one company remain, so
  «بررسی فروش X» or «بررسی بازار» are not affected.

## 5. Implementation plan (for Gate B)

- [x] T1 `UnavailableTopicCatalog.AnalysisRequestCompanyText`: «بررسی» as a trigger word, the D2 filler words; update
  the XML doc.
- [x] T2 `ComprehensiveAnalysisToolResult.NotFound`: carry `RecentWindowDays` (D4). Implemented as a separate
  `RecentWindowDays` on the tool result, so `QueryResponse` stays null when nothing is found (the workflow passes it
  to the presenters).
- [x] T3 Unit tests (`Feature150UnavailableTopicTests`): «سهم شکبیر را تحلیل کن», «سهم شکبیر را بررسی کن», «نماد فولاد را تحلیل کنید», «بررسی فولاد» route
  to `comprehensive_analysis` without a model call while the harness model proposes another capability;
  «تحلیل فروش فولاد», «بررسی صورت سود و زیان فولاد» and «ارزش ذاتی سهم فولاد را بررسی کن» keep today's route;
  BUG-020 / BUG-022 suites unchanged. Mutation check: with the old rule 7 cases fail. Note: two gate cases also pass
  with the old rule, because the unit fake resolver accepts any rest text; the `AnalysisRequestCompanyText` cases and
  the endpoint test below are the ones that discriminate.
- [~] T4 Endpoint test: «سهم X را تحلیل کن» with only an old analysis → 163 answer (2 credits) with the flag on; free
  OQ-099 card with the flag off. Agent-tool card test for the D4 sentence.
  Done: `Feature150UnavailableTopicMappedAnalysisE2ETests.AnalysisQuestion_KeepsTheTahlilAppCard` with
  «سهم فولاد را تحلیل کن» / «سهم فولاد را بررسی کن» while the scripted model proposes `symbol_metric_lookup` — the
  production symptom (both fail with the old rule, pass with the fix); `Bug032AnalysisToolCardTests` for D4; the
  existing `AnalysisRecencyWindow159EndpointTests` already cover «سهم وبملت را بررسی کن» → 163 answer (2 credits) and
  the free OQ-099 card. Not done: one endpoint test that combines a model proposing another capability with an
  old-only analysis and the 163 answer.
- [x] T5 Full suite compared with `develop`; no failure outside `specs/test-baseline.md`. `CHANGELOG.md` `[Unreleased]`
  `Fixed` line; BUG-032 row updated.
  Result 2026-11-17 against `develop` `e0c465e` (same machine): unit 32 failed / 3741 (develop 33 / 3724), architecture
  0 / 19, integration 179 failed / 1127 (develop 179 / 1125); no failure only on this branch except
  `FoundationEndpointTests.Health_ReturnsHealthyResponseAndCorrelationId`, which passed twice when re-run alone
  (flaky). The integration host needed test-only overrides `Billing__TahlilAppTopUps__Enabled=false` and
  `Authentication__Email__Enabled=false`: without them it does not start on develop either (option validation for
  top-up signing keys and SMTP).
- [ ] T6 (owner, read-only, optional) Confirm `SymbolGroundedFallback:Enabled` in the production `.env` (F6).

## 6. Acceptance criteria

- «سهم X را تحلیل کن» and «سهم X را بررسی کن» give the same answer as «تحلیل X» for the same company.
- With no TahlilApp analysis in the last 30 days and the fallback on, these questions get the 163 answer (2 credits).
- With the fallback off or not answering, the card shows «برای نماد X تحلیل تازه‌ای در ۳۰ روز اخیر در تحلیل‌اپ پیدا
  نشد.», free of charge, on every route.
- Messages with more than filler words and one company keep their current route.
