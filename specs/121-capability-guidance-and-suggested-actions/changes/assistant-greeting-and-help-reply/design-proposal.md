# Assistant greeting and "what can I ask?" reply

Status: Gate A: Approved by the owner on 2026-11-10

| Field | Value |
|---|---|
| Layers / Area | backend · AI, Conversations, Billing (status mapping only, if approved) |
| Priority (proposed) | P1 |
| Complexity (proposed) | S |
| Classification (workflow, "Existing Features and Changes") | **Scope change** to Feature 121: new behaviour and acceptance criteria for a message type that today ends in a refusal. Needs Gate A, then Gate B. |
| Depends On | None. Independent of `specs/150-shared-chat-routing-intent-and-entity-resolution-fixes/changes/unavailable-capability-refusals-and-answer-correctness/`; both add a rule at the same place in the gate. |
| Evidence | Feature 155 theme T15 (`گفتگوی عمومی / معرفی دستیار`): 6 customers (figure from the program lead; not re-measured here). |
| References | `specs/121-capability-guidance-and-suggested-actions/user-story.md` (help menus; starter prompts come from enabled capabilities), `specs/POLICY-V1-FREEZE.md`, `specs/155-admin-unanswered-question-demand-report/queries.sql` |

## 1. Current behaviour

Questions below are invented examples. Confirmed by reading the code and by a scratch run of the deterministic interpreter (no model call).

- `سلام` and `چیکار میکنی` produce no capability candidate (run output). The model then decides.
- If the model returns no capability, the turn is `Unsupported` (`CapabilityInterpretationGovernance.cs:415-419`), the frame is null, and the workflow replies with the generic refusal `این پرسش را نمی‌توانم به‌صورت قابل اتکا پاسخ بدهم…` (`FinancialCopilotWorkflowDefinition.cs:570-605`, `AiDialogueOutcomePolicy.cs:202`). A greeting gets a refusal.
- If the model picks a capability, `سلام` can be treated as a company mention (the interpreter lists it as an entity), giving an unrelated or not-found reply. Which of the two happens per message was not reproduced, because the model was not called.
- Telegram has a fixed `/start` and `/help` text (`TelegramAiAssistantAdapter.cs:182`), reached only by the command, not by typing a greeting.
- `GET capabilities/guidance` returns starter prompts for the web (`AiFacadeController.cs:36`), but no chat turn uses them.

## 2. Scope

**In scope:** a message that is *only* a greeting or *only* a help question gets a short introduction and a few sample questions, on web and Telegram.

**Added at Gate A (owner, 2026-11-10):** the whole-message phrase sets must cover three patterns, shown here with invented examples: a greeting (`سلام`), a request to introduce the assistant and say what it does (`خودت را معرفی کن و بگو چه کارهایی بلدی؟`), and a follow-up asking what can be asked (`خب چی می‌تونم بپرسم؟`). Customer wording stays out of the repository, so the phrase list and tests use same-pattern text.

**Out of scope:** greetings combined with a question (`سلام فولاد چطوره` keeps today's route), small talk in general, LLM-written replies, changes to `/start`, any flag.

## 3. Design

Same placement and pattern as Feature 156.

```text
ConversationDialogueGate.PrepareAsync
  symbol snapshot → assistant-help rule → (other deterministic rules) → hybrid interpreter
                         │ whole-message match, no pending clarification
                         ▼
  gate-routed capability "assistant_help"   (GateRoutedOnly; no data read; no model call)
                         ▼
  AssistantHelpCapabilityExecutor → Executed, reason "assistant_help", fixed text payload
```

| Part | Location | Notes |
|---|---|---|
| `AssistantHelpIntentRules` | new, `Application/AI/Orchestration/AssistantHelp/` | After `QueryNormalization`; strips trailing punctuation; **whole-message** match against two short phrase sets (greeting; help). |
| `AssistantHelpCapabilityExecutor` | same folder | Returns the composed text as the existing `string` payload (`FinancialCopilotWorkflowDefinition.cs:540`, `FinancialCopilotAgentWorkflowRunner.cs:116`). |
| Capability entry | `ConversationalCapabilityContracts.cs` | `GateRoutedOnly = true`, no slots. |
| Sample questions | Application, one list | Rendered in the text and as suggested actions (step 3 deterministic-actions block, `FinancialCopilotWorkflowDefinition.cs:1414-1434`). The guidance service returns nothing for Answered (`CapabilityGuidanceContracts.cs:81`), so a deterministic list is needed. |

Decisions:

- Skipped when the conversation has a pending clarification, so a clarification reply is never stolen.
- V2 only: the rule checks `Feature154RoutingScope.IsActive` because the gate also serves V1.
- Each sample must be answerable now. A provider availability check drops any sample whose route is unavailable, and a test round-trips every sample through the router (Feature 121 starter-prompt governance). Catalog examples are not reused as they stand: they include P/E and analysis questions that do not currently answer.
- Proposed text (OQ-PROPOSED-A7): `سلام! من ساپیو، دستیار هوشمند بازار هستم و می‌توانم فروش ماهانه، گزارش فصلی، ترازنامه و ترکیب فروش شرکت‌های بورسی را نشان بدهم. برای نمونه بپرسید:` followed by the sample list.

## 4. Billing

The turn is Answered, so today's mapping gives `Completed`, and a completed turn with no model call is charged `MinimumCredits` (OQ-006). Charging for a greeting would be a regression, since the refusal it replaces is free.

**Proposal (OQ-PROPOSED-A6):** a new completion status `InformationalReply` added explicitly to `ZeroChargeStatuses` in `DefaultPricingPolicy.cs`, and pinned by the existing pricing test. This is a visible billing-table change for the owner to approve, not a silent one. If the owner prefers the minimum credit, no billing code changes.

## 5. Feature 155 classifier effect

| Turn | Bucket today | Bucket after |
|---|---|---|
| `سلام`, `چیکار میکنی` | `unsupported` or `misrouted` | **`misrouted`**, unless the classifier changes |

`misroute_hint` matches `^(سلام|hi|hello)\y` and `چیکار می ?کنی`, and the turn is now outcome 0, so a good reply would still count as a gap.

Proposed classifier v2 rule (Feature 155 owner, Track D; bump the version per the file header): `outcome IN (0,1) AND reason = 'assistant_help'` → `answered`, before the misroute test. Without it the gap rate for T15 does not improve (OQ-PROPOSED-A9).

## 6. Interplay with Feature 156

The snapshot check runs first. Whether `سلام` or a help phrase equals a ticker has not been checked against the full eligible list (no database in this wave). OQ-011 shows that some common words are tickers, so the precedence is deliberate and follows the OQ-011 assumption: a whole-message ticker match wins. `SymbolSnapshot:Enabled` is untouched.

## 7. Testing

Regression test first (invented questions):

- Gate: `سلام`, `سلام!`, `درود`, `hello`, `چی می‌تونم بپرسم؟` → capability `assistant_help`; stub proposal provider is not called.
- Negative: `سلام فولاد چطوره`, `فولاد`, `قیمت فولاد`; a greeting during a pending clarification keeps the existing flow.
- Executor: Answered, reason code, Persian and English text, samples present.
- Sample round-trip through the router; availability filter in `CyclicalWaves` mode.
- V1 scope: route unchanged.
- Billing: zero charge if A6 is accepted. Telegram renders text and actions.
- Green means no new failure against `specs/test-baseline.md`.

## 8. Alternatives

| Option | Verdict |
|---|---|
| Improve only the generic refusal text | Still a refusal for a greeting. |
| Let the model write the reply | Costs tokens, not deterministic. |
| Treat as Unsupported with friendlier text | Keeps it free with no billing change, but mislabels a good reply as a gap. Fallback if A6 is refused and minimum charge is also unwanted. |

## 9. Risks

| Risk | Mitigation |
|---|---|
| A short real question matches a phrase | Whole-message match, negative tests. |
| Sample list drifts from what works | Round-trip test. |
| Three sources of sample text (this list, `/start`, starter prompts) | Keep one list for this reply; aligning `/start` and starter prompts is a follow-up for `specs/backlog.md`. |

## 10. Files expected at implementation

New: `Application/AI/Orchestration/AssistantHelp/AssistantHelpIntentRules.cs`, `AssistantHelpCapabilityExecutor.cs`, unit tests.

Changed: `ConversationalCapabilityContracts.cs`, `ConversationTaskStateContracts.cs`, `FinancialCopilotWorkflowDefinition.cs`, `ServiceCollectionExtensions.cs`, and, if A6 is accepted, `SemanticCapabilityExecutionContracts.cs` and `DefaultPricingPolicy.cs`.

## 11. Open questions (proposed; not yet in `specs/open-questions.md`)

| ID | Question | Options | Recommendation |
|---|---|---|---|
| OQ-PROPOSED-A6 | Is a greeting or help turn charged? | (a) free, new explicit zero-charge status; (b) minimum credit | (a): the refusal it replaces is free |
| OQ-PROPOSED-A7 | Reply text and sample questions | (a) the text in §3 with four samples: monthly sales trend, quarterly report, balance sheet, product mix (the OQ-007 set); (b) another set | (a) |
| OQ-PROPOSED-A9 | Classifier v2 rule for help turns (Feature 155) | (a) count as `answered`; (b) leave classifier v1 and accept T15 as a gap | (a) |

## 12. Next step

Gate A review by the owner. No `user-story.md` or `tasks.md` exists until Gate A.
