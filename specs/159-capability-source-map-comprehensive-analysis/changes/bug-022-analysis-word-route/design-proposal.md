# Bug fix proposal — «تحلیل X» must take the same route as «تحلیل جامع X» (BUG-022)

Status: Gate A and Gate B: Approved by the owner on 2026-11-11

Approval record: «Gate A و B تأیید» (2026-11-11) for this design and its test plan. The production log check
(question 1) was not requested and is not done.

## Report (owner, 2026-11-11, production v1.9.0)

- «تحلیل اخابر» → «برای نماد اخابر تحلیلی در تحلیل‌اپ پیدا نشد.», the link line, a context-metrics table,
  «اطمینان ۵۶٪», 1 credit.
- «تحلیل جامع اخابر» → the TahlilApp card of the latest اخابر analysis (title «اخابر», 1405/08/12).
- Owner: «پرسش اول هم باید همان مسیر پرسش دوم را میرفت». This is also Gate B condition C1 of the Feature 150 change
  («تحلیل فولاد», «تحلیل بنیادی فولاد», «تحلیل جامع فولاد» keep the CyclicalWaves analysis route).

## Discovery

- In the test host (scripted model with no proposal, so the deterministic interpreter decides) both questions route to
  `comprehensive_analysis` and both show the اخابر card (probe 2026-11-11, analysis titled «اخابر», company with the
  symbol only in `TseSymbol`/`CompanySymbol`). The data and the query are not the cause.
- With a real model, `HybridCapabilityInterpreter` sends every message to the model, and `SemanticArbitrator.Arbitrate`
  (`CapabilityInterpretationGovernance.cs:579`) **always takes the model's capability**; the deterministic
  `comprehensive_analysis` candidate has no veto. So the route of «تحلیل X» is whatever the model proposes.
- The first reply carries a confidence line and was charged, unlike the second; a `comprehensive_analysis` answer with
  no analysis on the semantic route is free (FR7) and shows no confidence line. So the first question ran on another
  route (most likely a model-proposed capability or the agent tool path with model-chosen arguments). **Which route
  exactly is not confirmed**: that needs the production logs or the conversation record of that question.
- The gate already forces the route for technical questions (BUG-020, `TryInterpretTechnicalAnalysisAsync`): when the
  message is only topic words plus one company that resolves, it builds the `comprehensive_analysis` interpretation
  without the model.

## Proposed fix

1. Widen that gate rule from technical words to every analysis request: a message that, after removing the analysis
   words («تحلیل», «تحلیل جامع», «تحلیل بنیادی», and the existing technical words «تکنیکال», «پیوت»), is exactly one
   company that resolves, goes straight to `comprehensive_analysis`. Technical words keep the technical topic (BUG-020);
   the others ask for the latest analysis with no topic.
2. Same guards as today: MAF V2, NoavaranAmin base provider, comprehensive analysis available, no pending question,
   rollout allows the semantic route (kill switch `SemanticRouting__Capabilities__comprehensive_analysis`).
3. Anything else in the message («تحلیل فروش فولاد», «تحلیل P/E فولاد», «تحلیل صنعت فولاد») keeps today's routing.

### Alternatives

- Give the deterministic interpreter a veto in `SemanticArbitrator` for strong matches. Rejected for now: it changes
  arbitration for every capability, a much larger change than this bug.
- Add «تحلیل X» examples to the model prompt. Rejected: the route would still depend on the model.

## Tests (plan)

- Unit: the gate routes «تحلیل اخابر», «تحلیل جامع اخابر», «تحلیل بنیادی اخابر» to `comprehensive_analysis` without a
  model call; «تحلیل فروش فولاد» is not routed by the gate; technical words still add the technical topic.
- Integration: a scripted model that proposes a **different** capability for «تحلیل فولاد» — the answer is still the
  analysis card (proves the model can no longer change the route); BUG-020 and Feature 150 suites unchanged.

## Results (2026-11-11, branch `track/a-routing-answer-correctness`, uncommitted)

- Implemented in `ConversationDialogueGate.TryInterpretTechnicalAnalysisAsync` and
  `UnavailableTopicCatalog.AnalysisRequestCompanyText` / `BuildAnalysisRequestInterpretation`. The rest of the message
  must resolve through `ResolveMentionAsync` (whole-text identity, not extraction). One addition inside the approved
  intent: a message that also matches a valuation or advice topic («تحلیل ارزش ذاتی X») is not a plain analysis request,
  so C3 / OQ-072 keep its route without depending on the resolver.
- Tests: unit — the gate routes «تحلیل فولاد», «تحلیل جامع فولاد», «تحلیل بنیادی فولاد», «تحلیل اخابر» with no model call
  while the harness model proposes `product_revenue_mix`; a rest that is not a company and an unavailable comprehensive
  analysis keep today's route; the company-text rule. Integration — the scripted model now proposes
  `symbol_metric_lookup` for «تحلیل فولاد»; the answer is still the analysis card. With the new rule disabled the same
  test fails with `symbol_metric_lookup` (mutation check), which reproduces the production behaviour.
- Not done: the production log check (not requested); a live run.

## Question for the owner

1. Should I first confirm the exact route of the production question from the server logs (read-only)? It does not
   change the fix, which removes the model from this decision either way.
