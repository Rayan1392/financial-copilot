# User Story: Clear refusal for unavailable topics and correct lookup answers

## Feature Folder

`150-shared-chat-routing-intent-and-entity-resolution-fixes/changes/unavailable-capability-refusals-and-answer-correctness`

## Layers / Area

Layers: backend
Area: AI, Conversations, FinancialData

## Priority

P1

## Complexity

M

## Depends On

None

## References

- `specs/150-shared-chat-routing-intent-and-entity-resolution-fixes/changes/unavailable-capability-refusals-and-answer-correctness/design-proposal.md` (Gate A approved 2026-11-11)
- `specs/155-admin-unanswered-question-demand-report/design.md` §10 and `queries.sql` (evidence and classifier)
- Bugs BUG-004, BUG-005, BUG-006, BUG-007 (registered on the `track/lead-unanswered-coverage` branch, `specs/bugs.md`; not yet on `develop`)
- `specs/POLICY-V1-FREEZE.md`, `specs/156-symbol-snapshot-default-card/` (gate-routed capability pattern), Feature 121 change (assistant help, same pattern)
- `docs/architecture.md` (V2 chat route), `docs/domain-glossary.md`
- Code: `ConversationTaskStateContracts.cs` (`ConversationDialogueGate`), `ConversationalCapabilityContracts.cs`, `ChatCapabilityAvailability.cs`, `ChatCapabilitySourceMap.cs`, `AiDialogueOutcomePolicy.cs`, `CapabilityGuidanceContracts.cs`, `SemanticCapabilityExecutors.cs`, `Scanner/AnswerConsistencyServices.cs`, `FinancialCopilotWorkflowDefinition.cs`, `FinancialCopilotAgentWorkflowRunner.cs`, `TelegramAssistantResponseRenderer.cs`
- No database model is touched.

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user who asks about technical analysis, equilibrium price, intrinsic value, or whether to buy a stock,
I want a clear answer that the assistant cannot do this yet, with what it can show me for the same symbol,
so that I am not misled by an unrelated table and am not charged for a non-answer.

As a user who asks for a stock price or a single metric,
I want the right unit and an honest "no value" outcome,
so that I can trust the number and am not charged when there is no value.

## Business Context

The Feature 155 baseline found customers who asked `قیمت تعادلی فپنتا`, `فاذر ارزش خرید داره؟`, `تکنیکال کگهر`, `پیوت های کاما را نشان بده`, `ارزش ذاتی رمپنا چقدر است؟` and received product mix, monthly sales, or the latest price, recorded as Answered and charged. Price answers on the web start with `واحد: میلیون ریال`, which is wrong for a per-share price. The P/E answer says there is no reliable value but is still Answered and charged.

## Scope

- R0: deterministic unavailable-topic rule in the dialogue gate, gate-routed capability `unavailable_topic`, executor returning Unsupported with a topic reason code.
- R1: technical analysis topic (`تکنیکال`, `پیوت`, `technical analysis`, `pivot`).
- R2: valuation topic (`قیمت تعادلی`, `ارزش ذاتی`, `equilibrium price`, `intrinsic value`).
- R3: investment-advice topic (`ارزش خرید`; `این قیمت` together with `خوبه` or `مناسب`).
- R4: unit note on lookup answers only for requested monthly-sales metrics; ` ریال` on the latest-price sentence.
- R5: a single-metric lookup with no value for any requested metric is NoData, with the deterministic Persian sentence, on web and Telegram.

## Out Of Scope

- Building valuation, technical analysis, or advice (Features 160 and 161).
- A P/E data source. Prompt changes (backlog). Billing policy changes.
- `SymbolSnapshot:Enabled`, provider settings, the V1 path, CyclicalWaves-mode behaviour.
- Words beyond the listed phrases (OQ-069). The default refusal text that still offers "تحلیل‌های ثبت‌شده" (copy review, backlog).

## Functional Requirements

1. In V2 (`Feature154RoutingScope.IsActive`) and while `FinancialData:ChatProvider` is `NoavaranAmin`, a message whose normalized words contain a listed phrase is routed to `unavailable_topic` before the hybrid interpreter, so the model is not called for interpretation.
2. Order in the gate: Symbol Snapshot (Feature 156) → unavailable topic → assistant help → hybrid interpreter. The rule does not fire when the conversation has a pending question, for insight-explanation requests, or when the rollout mode for `unavailable_topic` is Legacy or Rollback.
3. Phrase matching is on whole words of `QueryNormalization.Normalize(message)` (ZWNJ becomes a space). A multi-word phrase must appear as consecutive words. R3's pair rule needs both `این قیمت` and one of `خوبه`/`مناسب`.
4. The symbol slot is filled only when entity resolution returns one resolved company; otherwise it stays empty and no "which symbol?" question is asked.
5. `UnavailableTopicCapabilityExecutor` reads no data and returns `Unsupported` with the topic's reason code: `technical_analysis_unavailable`, `valuation_unavailable`, `investment_advice_unavailable`.
6. `ChatCapabilityAvailability.MessageForReasonCode` returns the approved Persian text and an English text for each code; `Evaluate` and the route sets do not change.
7. Follow-up buttons are the existing symbol follow-ups (`روند فروش {symbol}`, `گزارش فصلی {symbol}`); without a symbol they show the catalog example.
8. The refusal turn is not charged and the Feature 141 fallback does not run on it.
9. R4: when `RequestedMetricCodes` is set, the unit note `واحد: میلیون ریال` appears only if a requested metric is a monthly-sales monetary metric. Without requested codes behaviour is unchanged. The `LATEST_PRICE` value sentence ends the number with ` ریال`.
10. R5: when no row has a value for any requested metric, `SymbolMetricLookupCapabilityExecutor` returns `NoData` and keeps the table as payload; if any requested value exists the result stays `Executed`.
11. R5 text: the web reply for that NoData turn is the deterministic sentence `برای نماد … مقدار قابل اتکایی برای … در داده‌های فعلی موجود نیست.` (no unit note). Telegram prints the same sentence above the single-symbol card when the outcome is NoData. Both V2 runners behave the same.

## Data / Schema

None. No migration.

## API (if any)

No contract change. `AiQueryResponse.Outcome`, `OutcomeReasonCode` and `SemanticCapabilityCode` carry new values (`Unsupported` with the three reason codes, `unavailable_topic`; `NoData` for R5).

## AI Query Path (if any)

V2 only. Capability `unavailable_topic` (route `unavailable_topic`, `GateRoutedOnly`, optional `symbol` slot, data requirement `unavailable_topic_copy`). Outcome Unsupported, billing completion `ValidationFailed` (zero charge). R5: `symbol_metric_lookup` Outcome NoData, billing `NoData` (zero charge).

## UI (if any)

No screen change. Web and Telegram show the refusal text and the follow-up buttons through the existing paths. Telegram NoData lookup: sentence above the card.

## Non-Functional Requirements

- No new I/O on the refusal path; the match is a bounded word scan on the existing 4000-character limit.
- Customer text is not logged by new code. Existing outcome, reason-code, and semantic-event records carry the new codes.
- Kill switch: `SemanticRouting__Capabilities__unavailable_topic=Rollback`.

## Acceptance Criteria

- [ ] AC-1: `تکنیکال کگهر`, `پیوت های کاما را نشان بده` and `پیوت‌های کاما` give the technical-analysis refusal, Outcome Unsupported, reason `technical_analysis_unavailable`, zero charge, and no interpretation model call, even when a stub model would pick `product_revenue_mix`.
- [ ] AC-2: `قیمت تعادلی فپنتا` and `ارزش ذاتی رمپنا چقدر است؟` give the valuation refusal (`valuation_unavailable`), not the latest price.
- [ ] AC-3: `فاذر ارزش خرید داره؟` and `فولاد در این قیمت خوبه؟` give the advice refusal (`investment_advice_unavailable`).
- [ ] AC-4: With a resolved symbol the follow-ups are `روند فروش {symbol}` and `گزارش فصلی {symbol}`; with an unknown or ambiguous name the refusal still appears, with example follow-ups and no clarification question.
- [ ] AC-5: These still keep today's route: `قیمت فولاد`, `آخرین قیمت فولاد`, `نسبت قیمت به سود فولاد`, `ارزش بازار فولاد`, `تحلیل بنیادی فولاد`, `چارت فروش فولاد`, a bare ticker with Symbol Snapshot on, an answer to a pending question.
- [ ] AC-6: The rule does not fire in V1 scope, in CyclicalWaves mode, or with rollout mode Rollback for `unavailable_topic`.
- [ ] AC-7: The Feature 141 fallback does not run on a refusal turn.
- [ ] AC-8: Each new reason code has Persian and English text (pinned by a test).
- [ ] AC-9: `قیمت فولاد` on the web does not start with `واحد: میلیون ریال` and states the price in ریال; a requested monthly-sales lookup keeps the note; a table without requested codes keeps today's text.
- [ ] AC-10: P/E with no value: Outcome NoData, zero charge, table kept, Persian deterministic sentence on web; Telegram shows the sentence above the card. One requested value present → Executed and charged as today.
- [ ] AC-11: Build and tests green with no failure outside `specs/test-baseline.md` (BUG-001, BUG-003 known).
- [ ] AC-12: Live check on local Development with the disposable PostgreSQL: one question per topic, one price, one P/E.

## Assumptions

- Production runs `MicrosoftAgentFrameworkV2` and `NoavaranAmin` (OQ-008); not re-verified here.
- No eligible ticker equals a listed word (`تکنیکال`, `پیوت`). Not checked against data; if one does, Symbol Snapshot wins for the bare word when its flag is on.
- R5 is in scope as the design listed it; the owner can remove it at Gate B.
- The Feature 155 gap rate does not fall; turns move from `misrouted`/`soft_no_answer` to `unsupported`/`no_data` (design §7).
