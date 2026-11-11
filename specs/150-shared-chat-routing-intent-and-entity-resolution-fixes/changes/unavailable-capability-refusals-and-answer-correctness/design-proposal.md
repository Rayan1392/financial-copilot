# Unavailable-capability refusals and answer correctness (BUG-004 to BUG-007)

Status: Gate A: Approved by the owner on 2026-11-11

Gate A decisions (owner, 2026-11-11: "approved with the recommendations you made"):

| Question | Decision |
|---|---|
| OQ-PROPOSED-A1 → OQ-066 | (a) neutral refusal plus data alternatives |
| OQ-PROPOSED-A2 → OQ-067 | (a) the Persian copy in R1–R3 as written |
| OQ-PROPOSED-A3 → OQ-068 | The rule depends on the source that would answer the topic. None of the three topics has an overridable route in `ChatCapabilitySourceMap`, and the only other answer (CyclicalWaves v2 equilibrium range, `FinancialCopilotWorkflowDefinition.cs:382`) runs only when the base provider is CyclicalWaves. So in practice: the rule is on only while `FinancialData:ChatProvider` is `NoavaranAmin`. |
| OQ-PROPOSED-A4 → OQ-069 | (a) only the reported phrases and their direct English forms |
| OQ-PROPOSED-A5 → OQ-070 | (a) `NoData`, keep the context table, deterministic sentence |
| OQ-PROPOSED-A8 → OQ-071 | (a) add ` ریال` to the `LATEST_PRICE` sentence only |
| Scope | R0–R5. R5 (BUG-007) is in this design's scope list; the owner did not remove it. Confirm or remove at Gate B. |

Details and plan: `user-story.md`, `tasks.md` in this folder.

Changes found while re-checking against `develop` `da623ed` (Feature 162 merged); none changes the design:

- `QueryNormalization.Normalize` turns ZWNJ into a space (`QueryNormalization.cs:50`), so «پیوت‌های» yields the word «پیوت». Word matching is enough.
- P/S gauge and industry-relative valuation are retired in both modes (`ChatCapabilityAvailability.RetiredRoutes`); the CyclicalWaves-mode equilibrium answer still exists, so decision OQ-068 stands.
- `SemanticRouting:DefaultMode` is `Shadow` in `docker-compose.yml`; Shadow still runs the semantic frame, so the new capability needs no compose entry. `SemanticRouting__Capabilities__unavailable_topic=Rollback` turns the rule off.
- `RequestedMetricCodes` is set only by the V2 executor (`SemanticCapabilityExecutors.cs:266`, `:800`), so R4 needs no extra V1 guard.

| Field | Value |
|---|---|
| Layers / Area | backend · AI, Conversations, FinancialData |
| Priority (proposed) | P1 |
| Complexity (proposed) | M (BUG-006 and BUG-007 alone: S) |
| Depends On | None for BUG-006 and BUG-007. BUG-004 and BUG-005 share item R0 (§3). |
| Evidence | Feature 155 baseline (`specs/155-admin-unanswered-question-demand-report/design.md` §10): about 19 customers received an unrelated or soft answer (figure supplied by the program lead; not re-measured here, no database in this wave). |
| References | `specs/POLICY-V1-FREEZE.md`, `specs/155-admin-unanswered-question-demand-report/queries.sql`, `specs/156-symbol-snapshot-default-card/design-proposal.md`, `docs/architecture.md`, `src/backend/FinancialCopilot.Application/AI/Orchestration/ChatCapabilityAvailability.cs`, `AiDialogueOutcomePolicy.cs` |
| Why this folder | The defects are in the shared chat routing layer that Feature 150 scopes. The answer-text fixes are in the same turn. One folder; each item below can be approved on its own. |

Questions in this document are invented, same-pattern examples. `فولاد` stands for any ticker.

## 1. Discovery summary

The V2 route for a chat turn is: `ConversationDialogueGate.PrepareAsync` → `HybridCapabilityInterpreter` → frame → `SemanticCapabilityDispatcher` → executor → outcome and billing in `FinancialCopilotWorkflowDefinition` step 3.

**Structural cause of the wrong answers (confirmed in code).** The model picks the capability and nothing checks that the choice fits the question:

- The prompt lists capability codes only, with no descriptions, and says "Choose one primary capability" (`CapabilityInterpretationGovernance.cs:225`). It never says to return none when nothing fits.
- `SemanticArbitrator.Arbitrate` (`:579-591`) keeps the model's candidates and vetoes only disabled ones. The deterministic interpreter's result is not compared.
- `unsupportedParts` from the model is honoured only by `MonthlyActivityTrendCapabilityExecutor` (`SemanticCapabilityExecutors.cs:142`). `ProductRevenueMixCapabilityExecutor` (`:269`) and the others ignore it.
- No capability in the catalog covers technical analysis, valuation, or investment advice. In `NoavaranAmin` mode, which production runs (OQ-008), `comprehensive_analysis` is unavailable too (`ChatCapabilityAvailability.cs:110`).

### Findings per bug

Scratch run (Application assembly, outside the repository, no database): the deterministic interpreter and the prose builder were executed with invented input. The model was not called.

| Bug | Symptom | Verdict | Evidence |
|---|---|---|---|
| BUG-004 | `قیمت تعادلی X`, `X در این قیمت خوبه؟` → latest price | **Confirmed in code, deterministic part reproduced** | `قیمت` is in `MetricWords` (`DeterministicCapabilityInterpreter.cs:44`), so the interpreter proposes `symbol_metric_lookup` with metric text `قیمت` (run output, confidence 0.88). The gate takes the metric from the raw message (`ConversationTaskStateContracts.cs:332-336`). `DirectMetricRoutingRegistry` tries every token window (`:416-430`) and the bare alias `قیمت` resolves to `LATEST_PRICE` (`PhaseOneFinancialSemanticCatalog.cs:156`). |
| BUG-004 | `X ارزش خرید داره؟` → latest price | **Partly confirmed.** Only one code path found; not confirmed by a run | No metric word and no candidate in the interpreter (run output). As a first turn, `symbol_metric_lookup` ends in clarification (metric slot Missing). The only path to the price is the carried-over slot: same active capability, so an earlier price turn's metric carries over (`ConversationTaskStateContracts.cs:697-704`, written into the frame at `:461-485`). Needs a real conversation or logs to confirm. |
| BUG-005 | `تکنیکال X` → product revenue mix; `پیوت X` → monthly sales; `ارزش ذاتی X` → monthly sales trend | **Cause confirmed; the specific capability is the model's choice and was not reproduced** | The interpreter returns no candidate for all three (run output), so only the model decides (see structural cause). No code rule maps these words to those capabilities. Production evidence is the Feature 155 baseline. |
| BUG-006 | Price answer labelled `واحد: میلیون ریال` | **Confirmed and reproduced. It is a presentation defect, and broader than reported** | `SemanticLookupMetricPolicy.ContextMetricCodes` always adds `MONTHLY_SALES` (`SemanticCapabilityExecutors.cs:730`), and projection is skipped for price (`:768`). `WithUnitNote` then tests whether the table has any monthly-sales column, not what was asked (`AnswerConsistencyServices.cs:270-276`). Run output: `واحد: میلیون ریال \| آخرین قیمت نماد فولاد برابر است با 3,450.` The same prefix appears on every single-metric V2 lookup except monthly-only ones, for example P/E. Web only: Telegram renders a stock card from cells for one row (`TelegramAssistantResponseRenderer.cs:57,117`). |
| BUG-007 | P/E says no reliable value, Outcome = Answered | **Confirmed. It is an outcome-mapping defect, and the data gap looks wider than "large symbols"** | Rows are built for every resolved company whether or not the metric has a value (`EfCoreSymbolMetricLookupService.cs:194-199`, `:687-688`), and the context columns fill them. The executor maps only `Rows.Count == 0` to NoData (`SemanticCapabilityExecutors.cs:258`), so the unavailable sentence (`AnswerConsistencyServices.cs:93`) is recorded as Answered and charged. **Data:** `PE_TTM` is fed only by `PE_RATIO` passthrough (`ServiceCollectionExtensions.cs:1064-1066`). Only the legacy CyclicalWaves normalizer writes `PE_RATIO` (`CyclicalWavesFinancialStatementNormalizer.cs:146`). Feature 148 C1-M permanently excludes that policy at read time (`LegacyCyclicalWavesDerivedMetricFilter.cs:50`). So the code suggests P/E is empty for every symbol, not only large ones. **Not verified against data:** no database in this wave. |

## 2. Scope

**In scope**

- Explicit refusal plus an alternative for the listed question patterns (BUG-004, BUG-005).
- Correct unit text on lookup answers (BUG-006).
- Correct outcome when the requested metric has no value (BUG-007).

**Out of scope**

- Building valuation, technical analysis, or advice (Features 160 and 161 may later replace the refusals).
- Restoring a P/E data source.
- Billing policy changes (§6 states the existing behaviour that applies).
- `SymbolSnapshot:Enabled`, any provider setting, the V1 path.
- Words not listed in R1 (OQ-PROPOSED-A4).

## 3. Design

Items can be approved independently, except that R1–R3 need R0.

### R0 — Refusal mechanism (shared by BUG-004 and BUG-005)

A deterministic rule in the gate, before the model is called, the same placement Feature 156 uses.

```text
ConversationDialogueGate.PrepareAsync
  symbol snapshot (Feature 156, flag)  →  unavailable-topic rule  →  hybrid interpreter
                                               │ match
                                               ▼
  gate-routed capability "unavailable_topic"   (GateRoutedOnly, as symbol_snapshot; never offered to the model)
                                               ▼
  UnavailableTopicCapabilityExecutor  →  Unsupported + topic reason code (no data read)
                                               ▼
  AiDialogueOutcomePolicy → message from ChatCapabilityAvailability.MessageForReasonCode
  CapabilityGuidanceService → "روند فروش {symbol}", "گزارش فصلی {symbol}" (existing SymbolFollowUps)
```

| Part | Location | Notes |
|---|---|---|
| `UnavailableTopicCatalog` (table: topic, phrases, reason code) and match rule | new, `Application/AI/Orchestration/UnavailableTopics/` | Normalized with `QueryNormalization`. One row per topic. No frameworks. |
| `UnavailableTopicCapabilityExecutor` | same folder | Returns `CapabilityExecutionStatus.Unsupported` and the row's reason code. Humble. |
| Capability entry and route | `ConversationalCapabilityContracts.cs` | `GateRoutedOnly = true`, optional `symbol` slot, one data-requirement arm. |
| Reason codes and text | `ChatCapabilityAvailability.cs`, **constants and `MessageForReasonCode` only** | No change to `Evaluate` or the route sets. |
| Detection | `ConversationDialogueGate` | New optional constructor arguments, default `null` = rule off, so existing gate tests keep their meaning. |

Decisions:

- **Why the rule runs before the model.** It is testable without a model, costs no tokens, and gives the same answer every time.
- **Why a gate-routed capability, not a new disposition.** The frame path already gives correct billing, outcome persistence, `SemanticCapabilityCode` for the Feature 155 report, and symbol-specific follow-ups, in both V2 runners with no new branch.
- **Feature 141 fallback stays off these turns.** A reason code known to `MessageForReasonCode` sets `policyBlocked` (`FinancialCopilotWorkflowDefinition.cs:459`), and the fallback requires `!policyBlocked` (`:463`).
- **The company is optional.** The slot is filled only when entity resolution returns `Resolved`. Ambiguous or not-found results leave it empty, because the refusal must not first ask "which symbol?". Without a symbol the buttons show the catalog example.
- **V1 stays frozen.** The gate also serves V1 (`AiQueryOrchestrationService.cs:86`), so the rule checks `Feature154RoutingScope.IsActive` as Feature 154 did. Assumption: production runs `MicrosoftAgentFrameworkV2`; not verifiable in this wave.
- **Provider.** The rule fires only in `NoavaranAmin` mode. In `CyclicalWaves` mode gauges and analysis posts can answer these questions (`FinancialCopilotWorkflowDefinition.cs:351-385`, `SemanticQueryFrameEnrichment.cs:89-90`). See OQ-PROPOSED-A3.
- **Easy to replace.** When Feature 160 or 161 ships, its topic row and message are deleted and the question reaches the new capability. Nothing else changes.

### R1 — Technical-analysis topic (part of BUG-005)

Phrases: token `تکنیکال`, token `پیوت`, `technical analysis`, `pivot`. Reason code `technical_analysis_unavailable`.

Proposed text (OQ-PROPOSED-A2): `تحلیل تکنیکال هنوز در دستیار فعال نشده است. می‌توانم روند فروش ماهانه یا گزارش فصلی همین نماد را نشان بدهم.`

### R2 — Valuation topic (BUG-004 `قیمت تعادلی`, BUG-005 `ارزش ذاتی`)

Phrases: `قیمت تعادلی`, `ارزش ذاتی`, `equilibrium price`, `intrinsic value`. Reason code `valuation_unavailable`.

Proposed text: `قیمت تعادلی و ارزش ذاتی هنوز در دستیار محاسبه نمی‌شوند. می‌توانم آخرین قیمت، روند فروش ماهانه یا گزارش فصلی همین نماد را نشان بدهم.`

### R3 — Investment-advice topic (BUG-004 `ارزش خرید`, `در این قیمت خوبه`)

Phrases: `ارزش خرید`; `این قیمت` together with `خوبه` or `مناسب`. Reason code `investment_advice_unavailable`.

Proposed text needs an owner decision on the advice boundary (OQ-PROPOSED-A1): `توصیهٔ خرید یا فروش ارائه نمی‌کنم. می‌توانم آخرین قیمت، روند فروش ماهانه یا گزارش فصلی همین نماد را نشان بدهم تا خودتان تصمیم بگیرید.`

Negative cases that must keep today's route: `قیمت فولاد`, `آخرین قیمت فولاد`, `نسبت قیمت به سود فولاد`, `ارزش بازار فولاد`, `تحلیل بنیادی فولاد`, `چارت فروش فولاد`.

### R4 — BUG-006 unit label

In `SymbolLookupProseBuilder.WithUnitNote`, when `RequestedMetricCodes` is set, show the note only if a requested metric is a monthly-sales monetary metric. Without requested codes behaviour is unchanged. Scope-guarded so V1 output does not change.

Add ` ریال` to the sentence for `LATEST_PRICE` only, matching the Feature 156 card (`SymbolSnapshotTextComposer.cs:31`). See OQ-PROPOSED-A8. Independent of R0.

### R5 — BUG-007 outcome

In `SymbolMetricLookupCapabilityExecutor`, when no row has a value for any requested metric, return `NoData` and keep the table as payload. If any requested value exists, the result stays `Executed`. Scope-guarded for V1. Independent of R0.

Text: for a NoData lookup the workflow summary uses the deterministic sentence (`برای نماد … مقدار قابل اتکایی … موجود نیست`) instead of the English `AgentSummary`, which `ComposeSystemMessage` would discard for the generic message. The same applies in `FinancialCopilotAgentWorkflowRunner`.

Telegram skips `TextAnswer` for a one-row lookup (`TelegramAssistantResponseRenderer.cs:64`), so it must print the sentence above the card when the outcome is NoData. See OQ-PROPOSED-A5.

## 4. Fit with outcome policy and availability

| Concern | Behaviour |
|---|---|
| `AiDialogueOutcomePolicy` | No change needed. `ComposePersian` and `ComposeEnglish` already use `MessageForReasonCode` for Unsupported (`:199`, `:223`), and NoData already prefers a compatible detail (`:196-198`). |
| `ChatCapabilityAvailability` | Remains the single owner of refusal text. Only constants and the switch grow. |
| Existing default refusal text (`:202`) | Still offers "تحلیل‌های ثبت‌شده", which `NoavaranAmin` cannot provide. Out of scope; reported for the copy review. |

## 5. Interplay with Feature 156

- The snapshot check stays first, so a bare ticker still wins when the flag is on. The topic rule needs a listed phrase, so it matches a plain ticker only if a ticker equals a listed word. The snapshot check would win in that case when the flag is on. The collision has not been checked against the eligible-symbol list (no database in this wave).
- `SymbolSnapshot:Enabled` is not read or changed. No follow-up button depends on it.
- R4 and R5 do not touch `SymbolSnapshotUseCase`.

## 6. Billing (existing policy, no change proposed)

| Turn | Completion status | Charge |
|---|---|---|
| Refusal (`Unsupported`) | `ValidationFailed` (`SemanticCapabilityExecutionContracts.cs:289`) | Zero (`DefaultPricingPolicy.cs:34`) |
| BUG-007 (`NoData`) | `NoData` (`:287`) | Zero (`DefaultPricingPolicy.cs:36`) |

**Effect the owner should know.** These turns are charged today, because they complete as Answered. After the fix they are free and make no model call for the refusal path. The policy is the existing "no answer, no charge" rule (OQ-006 records the same principle); only the classification of these turns changes.

## 7. Effect on the Feature 155 classifier (`queries.sql`, v1)

No classifier change is needed for these items. Themes depend on the question text and do not move.

| Turn | Bucket before | Bucket after | Gap? |
|---|---|---|---|
| `قیمت تعادلی X` (T03), `ارزش ذاتی X` (T03) | `misrouted` (outcome 0, `misroute_hint`) | `unsupported` (outcome 5) | Yes → yes |
| `X ارزش خرید داره؟`, `X در این قیمت خوبه؟` (T04) | `misrouted` | `unsupported` | Yes → yes |
| `تکنیکال X`, `پیوت X` (T02) | `misrouted` | `unsupported` | Yes → yes |
| P/E unavailable (T03) | `soft_no_answer` (`مقدار قابل اتکایی` in the first 400 characters) | `no_data` (outcome 4) | Yes → yes |
| Price answer (BUG-006) | `answered` | `answered` | No change |

Expected on Q1 to Q3: the gap rate does **not** fall. These turns are still unmet demand. The `hidden` column of Q3 falls and `unsupported` and `no_data` rise by the same amount. Q4 shows the new reason codes and capability `unavailable_topic`.

## 8. Reliability and security

- No new I/O in the refusal path. One fewer model call; the rule is a bounded string match on the existing 4000-character interpretation limit.
- Failure of entity resolution leaves the refusal intact (slot empty).
- Telemetry: existing outcome, reason-code and semantic-event records carry the new codes. No new persistence, migration, API contract, or integration.
- Customer text is not logged by the new code.

## 9. Testing (a failing regression test comes first for each bug)

All with invented questions; existing test harnesses are named so the plan can reuse them.

| Bug | Regression test written first | Must fail today because |
|---|---|---|
| BUG-004, BUG-005 | Gate test in the style of `ComprehensiveAnalysisSemanticRoutingRegressionTests`: a stub proposal provider returns the wrong capability (`product_revenue_mix`); assert frame capability `unavailable_topic`, the reason code, and zero provider calls. Theory table for each phrase and each negative case in §3. | Rule does not exist |
| BUG-004, BUG-005 | Dispatcher and policy: `Unsupported`, Persian text, no Feature 141 fallback, follow-ups carry the symbol. V1 scope and `CyclicalWaves` mode: route unchanged. | Same |
| BUG-006 | `AnswerConsistencyTests`: table with requested `LATEST_PRICE` and a `MONTHLY_SALES` context column does not start with the note and contains `ریال`; requested `MONTHLY_SALES` keeps the note. | Reproduced by the scratch run |
| BUG-007 | `SemanticRouteExecutorTests`: row with a Missing requested cell → `NoData` with table; one value present → `Executed`. Billing: zero charge. Telegram renderer prints the sentence. | Executor returns `Executed` |

Also: the architecture tests stay green apart from BUG-003, and each reason code in the catalog has Persian and English text (pinned by a test). Live Development check on the disposable PostgreSQL with one question per topic. Green means no new failure against `specs/test-baseline.md`.

## 10. Alternatives

| Option | Verdict |
|---|---|
| Change the prompt so the model returns no capability | Not rejected as an addition, but it cannot be the fix alone: not deterministic, not unit-testable, still costs a call. Candidate for `specs/backlog.md`. |
| Remove the `قیمت` alias | Rejected: breaks ordinary price questions. |
| New disposition branch instead of a capability | Rejected: two runner branches, no symbol follow-ups. |
| Remove `MONTHLY_SALES` from context metrics (BUG-006) | Rejected: changes the visible table. The defect is in the text. |

## 11. Risks

| Risk | Mitigation |
|---|---|
| False refusals from a broad phrase | Narrow phrase list, negative test table. |
| Refusal replaces an answer that happened to be useful | The listed questions received a value unrelated to what was asked. |
| Executor change alters other single-metric lookups to NoData | Limited to "no requested value in any row", tested. |
| Merge contention on the catalog, the gate, and step 3 | Coordinate with Tracks B and C at planning. |
| P/E wider than reported | Decide with owner data once a read-only connection exists. |

## 12. Open questions (proposed; not yet in `specs/open-questions.md`)

| ID | Question | Options | Recommendation |
|---|---|---|---|
| OQ-PROPOSED-A1 | Advice boundary for `ارزش خرید` and `در این قیمت خوبه` (Feature 155 lane T04 is a policy decision) | (a) neutral refusal plus data alternatives; (b) refusal with a legal disclaimer; (c) defer R3 until an advice policy exists | (a) |
| OQ-PROPOSED-A2 | Persian refusal copy in R1–R3 | (a) as proposed; (b) owner edits | (a), with owner review of wording |
| OQ-PROPOSED-A3 | Do refusals apply in `CyclicalWaves` mode? | (a) `NoavaranAmin` only; (b) both modes | (a): that mode has gauges and analysis posts |
| OQ-PROPOSED-A4 | Phrase list | (a) only the reported phrases and direct English forms; (b) also `چارت`, `مقاومت`, `حمایت`, `هدف قیمتی`, `پیش‌بینی` | (a) now; `چارت` collides with the sales-chart route, and broader lists should follow measured false-positive data |
| OQ-PROPOSED-A5 | P/E with no value | (a) `NoData`, keep the context table, deterministic sentence; (b) `Unsupported` because no source exists; (c) `PartialAnswer` | (a) |
| OQ-PROPOSED-A8 | Add `ریال` to the price sentence | (a) yes, `LATEST_PRICE` only; (b) only remove the wrong label | (a) |

Greeting questions A6, A7 and A9 are in the greeting proposal.

## 13. Next step

Gate A review by the owner, per item. No `user-story.md` or `tasks.md` exists until Gate A.
