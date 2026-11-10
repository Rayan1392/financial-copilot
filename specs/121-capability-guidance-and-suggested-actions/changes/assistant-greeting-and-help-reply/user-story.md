# User Story: Assistant greeting and "what can I ask?" reply

## Feature Folder

`121-capability-guidance-and-suggested-actions/changes/assistant-greeting-and-help-reply`

## Layers / Area

Layers: backend
Area: AI, Conversations, Billing

## Priority

P1

## Complexity

S

## Depends On

`121-capability-guidance-and-suggested-actions` (parent feature; its user story records it as implemented). No dependency on another open change.

## References

- `specs/121-capability-guidance-and-suggested-actions/changes/assistant-greeting-and-help-reply/design-proposal.md` (approved at Gate A by the owner on 2026-11-10)
- `specs/121-capability-guidance-and-suggested-actions/user-story.md` (suggestion rules; starter-prompt governance)
- `specs/POLICY-V1-FREEZE.md`
- `specs/156-symbol-snapshot-default-card/design-proposal.md` (same gate pattern)
- `specs/155-admin-unanswered-question-demand-report/queries.sql` (classifier v1; not changed here)
- `specs/test-baseline.md`
- `docs/domain-glossary.md` terms used: none new. "Capability", "suggested action" keep their existing meaning.
- `docs/architecture.md`: dependency direction only.
- Data model: none touched. `CompletionStatus` on the billing ledger is an existing 64-character text column (`BillingEntityConfigurations.cs:74`).
- Owner decisions: OQ-017 (free through a new explicit zero-charge status), OQ-018 (the §3 text with four samples), OQ-011 (assumption kept), OQ-030 (open; see Assumptions).

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a customer who opens the chat with a greeting, or who does not know what to ask,
I want a short introduction and a few example questions,
so that I can reach a real answer instead of a refusal.

## Business Context

Six customers in the Feature 155 baseline sent a greeting, asked the assistant to introduce itself, or asked what they could ask (figure from the program lead; not re-measured). Today such a message ends in the generic refusal or an unrelated reply, on web and Telegram. Telegram has a `/start` text, but only for the command.

## Scope

- A message that is *only* one of the phrases in "Phrase lists" gets the fixed reply in "Reply text", on web and Telegram.
- Three patterns: greeting; a request to introduce the assistant and say what it does; a follow-up asking what can be asked.
- The reply is free (OQ-017).

## Out Of Scope

- A greeting combined with a question (`سلام، کگل چطور است؟` keeps today's route).
- General small talk and LLM-written replies.
- Changes to `/start`, `/help`, the starter-prompt endpoint, or frontend code.
- `specs/155-.../queries.sql` and classifier versioning (OQ-030).
- Any flag or configuration change. The behaviour has no flag.
- The V1 path.

## Functional Requirements

### Matching

1. A rule runs in `ConversationDialogueGate.PrepareAsync`, after the Symbol Snapshot check and before the hybrid interpreter.
2. Matching works on a **compact key**: `QueryNormalization.Normalize(message)` (which already removes punctuation and unifies Arabic letters and ZWNJ), with spaces removed and `ؤ` replaced by `و`. So `چی می‌تونم بپرسم؟`, `چی میتونم بپرسم` and `چي ميتونم بپرسم` give one key.
3. A message longer than 80 characters never matches.
4. The whole message must equal a listed phrase. For the introduction and follow-up patterns only, these leading words are removed first, one or more times: `سلام`, `درود`, `خب`, `خوب`, `پس`, `باشه`, `ببخشید`, `لطفا`, `لطفاً`. For pattern 2 only, the standalone tokens `کامل`, `کاملا` and its spelling `کاملاً` are also removed wherever they occur, before compaction. Nothing else is removed, and the whole remaining message must still equal a listed phrase, so these words alone never match.
5. Matching is exact against the lists below. There is no fuzzy or partial match.

### Phrase lists

All phrases are invented, same-pattern text. Each row is one entry; spacing and ZWNJ variants collapse through the compact key. Checked in a scratch run: all 84 compact keys are unique, and the deterministic interpreter proposes no capability for any of them today.

**Pattern 1 — Greeting (22)**

`سلام` · `سلام علیکم` · `درود` · `درود بر شما` · `وقت بخیر` · `وقت به خیر` · `روز بخیر` · `صبح بخیر` · `ظهر بخیر` · `عصر بخیر` · `سلام وقت بخیر` · `سلام صبح بخیر` · `سلام روز بخیر` · `سلام خسته نباشید` · `خسته نباشید` · `سلام خوبی` · `سلام چطوری` · `سلام ساپیو` · `سلام دستیار` · `hi` · `hello` · `hey`

**Pattern 2 — Introduce the assistant and say what it does (33)**

`خودت را معرفی کن` · `خودت رو معرفی کن` · `خودتو معرفی کن` · `خودت را معرفی کن و بگو چه کارهایی بلدی` · `خودت رو معرفی کن و بگو چه کارهایی بلدی` · `خودتو معرفی کن و بگو چه کارهایی بلدی` · `خودت را معرفی کن و بگو چه کارهایی میتوانی انجام بدهی` · `خودت را معرفی کن و بگو چیکار میکنی` · `خودت رو معرفی کن و بگو چیکار میکنی` · `خودتو معرفی کن و بگو چیکار میکنی` · `خودت را معرفی کن و بگو چیکار میتونی بکنی` · `خودت رو معرفی کن و بگو چیکار میتونی بکنی` · `خودتو معرفی کن و بگو چیکار میتونی بکنی` · `کی هستی` · `تو کی هستی` · `تو چی هستی` · `شما کی هستید` · `چیکار میکنی` · `چکار میکنی` · `چه کار میکنی` · `چیکار میتونی بکنی` · `چکار میتونی بکنی` · `چه کاری میتونی بکنی` · `چه کارهایی میتونی بکنی` · `چه کارهایی میتوانی انجام بدهی` · `چه کارهایی بلدی` · `چه کارایی بلدی` · `چی بلدی` · `چیکار بلدی` · `who are you` · `introduce yourself` · `what can you do` · `what do you do`

**Pattern 3 — Follow-up asking what can be asked (29)**

`چی میتونم بپرسم` · `چی میتوانم بپرسم` · `چی میشه پرسید` · `چی میشه بپرسم` · `چی بپرسم` · `چی رو بپرسم` · `چی را بپرسم` · `چه چیزی میتونم بپرسم` · `چه چیزی میتوانم بپرسم` · `چه سوالی میتونم بپرسم` · `چه سوالی میتوانم بپرسم` · `چه سوالی بپرسم` · `چه سوالایی میتونم بپرسم` · `چه سوالهایی میتونم بپرسم` · `چه سوالاتی میتونم بپرسم` · `چه سوالاتی میشه پرسید` · `از تو چی میتونم بپرسم` · `ازت چی میتونم بپرسم` · `ازت چی بپرسم` · `از شما چی میتونم بپرسم` · `از شما چه سوالی میتونم بپرسم` · `درباره چی میتونم بپرسم` · `راجع به چی میتونم بپرسم` · `راهنما` · `راهنمایی` · `help` · `what can i ask` · `what can i ask you` · `what should i ask`

### Reply text

6. All three patterns get the same reply. Persian reply, exactly (OQ-018; each sample on its own line):

```text
سلام! من ساپیو، دستیار هوشمند بازار هستم و می‌توانم فروش ماهانه، گزارش فصلی، ترازنامه و ترکیب فروش شرکت‌های بورسی را نشان بدهم. برای نمونه بپرسید:
• روند فروش ماهانه کگل را نشان بده
• گزارش فصلی کگل را نشان بده
• ترازنامه کگل را نشان بده
• ترکیب فروش محصولات کگل
```

7. A message in English (`AiDialogueOutcomePolicy.DetectReplyLanguage` = `en`) gets this text. OQ-018 covers only the Persian copy, so the owner reviews this wording at Gate B:

```text
Hello! I am Sapio, your market assistant. I can show monthly sales, quarterly reports, balance sheets and product sales mix for listed companies. For example, ask:
• monthly sales trend for کگل
• show کگل quarterly income statement
• show کگل balance sheet
• product revenue mix for کگل
```

8. The four samples are also returned as suggested actions, in this order:

| # | Id | Kind | Persian label | English label | Capability | Message |
|---|---|---|---|---|---|---|
| 1 | `assistant_help:monthly_sales` | `ShowCapabilityHelp` | روند فروش ماهانه | Monthly sales trend | `monthly_activity_trend` | sample 1 |
| 2 | `assistant_help:quarterly_report` | `ShowCapabilityHelp` | گزارش فصلی | Quarterly report | `financial_statement_table` | sample 2 |
| 3 | `assistant_help:balance_sheet` | `ShowCapabilityHelp` | ترازنامه | Balance sheet | `financial_statement_table` | sample 3 |
| 4 | `assistant_help:product_mix` | `ShowCapabilityHelp` | ترکیب فروش محصولات | Product sales mix | `product_revenue_mix` | sample 4 |

`PresetSlots` is empty, `RelevanceReason` is `assistant_help`, and `RegistryVersion` is the current registry version.

### Example symbol

9. The samples use one fixed constant, `کگل`. It is a proposal for Gate B review. How it was chosen:
   - It is not `فولاد`, the existing example symbol: the Feature 155 baseline records `entity_ambiguous` for monthly sales on that symbol in production (`specs/155-.../design.md` §10).
   - It is not a bank: banks have no product sales mix.
   - It is not one of the common-word tickers in OQ-011.
   - It is a single, unique compact key in the eligible list (`specs/156-.../evidence/T1.1-ticker-check-output.txt`).
   - A fixed value keeps the reply deterministic (Feature 121 suggestion rules).

### Answerability under NoavaranAmin

| Sample | Route available (scratch run, `ChatCapabilityAvailability`) | Deterministic interpreter top candidate (scratch run) | Data for `کگل` |
|---|---|---|---|
| Monthly sales trend | Yes | `monthly_activity_trend`; one company mention | **Not verified.** The read-only Development role cannot read the monthly tables (`specs/156-.../tasks.md`). |
| Quarterly report | Yes | `financial_statement_table`; one company mention | Development only: quarterly items present (`specs/156-.../evidence/quarterly-rows-check-output.txt`). |
| Balance sheet | Yes | `financial_statement_table`; one company mention | **Not verified** for this symbol. Feature 154 live runs used other symbols. |
| Product mix | Yes | `product_revenue_mix`; one company mention | **Not verified.** |

Routing was confirmed; data was only partly confirmed. In production the model also takes part in routing the product-mix sample. `tasks.md` carries the verification step.

### Availability

10. A provider availability check drops a sample whose route is unavailable. In `CyclicalWaves` mode (not production, OQ-008) the monthly and product-mix samples are dropped from both the text and the actions. The introductory sentence does not change.

### Conversation behaviour

11. Behaviour of the rule by conversation state:

| Previous state | Behaviour |
|---|---|
| New conversation | Rule fires; reply below. |
| After a generic refusal (`Unsupported`) | No pending action exists (`RecordOutcomeAsync` records an answered state), so the rule fires. Follow-up pattern gets the reply. |
| After a normal answer | Rule fires. The active task, its slots, and the pending state are **left unchanged**: the reply does not write conversation task state, so the next question can still use the earlier company and metric. |
| During a pending clarification or disambiguation (`ConversationTaskState.PendingAction` set, within its 20-minute, 6-turn life) | **The rule is skipped, as approved at Gate A. A follow-up of this kind is not answered with the help reply.** What happens instead is today's flow: the message goes to the hybrid interpreter. If the model proposes no capability, the user gets the generic refusal, and the pending action is cleared when the turn is recorded. If the model proposes the pending capability, the user is asked the same clarification again, or told the name was not found. Which one occurs depends on the model and was not reproduced. See OQ-PROPOSED-A10. |

12. Symbol Snapshot runs first, so with `SymbolSnapshot:Enabled` on, a message that is exactly an eligible ticker opens the card (OQ-011 assumption). The flag is not read or changed by this rule.

### Outcome and billing

13. The turn is `Answered`, reason code `assistant_help`, `SemanticCapabilityCode = assistant_help`.
14. `DetectedIntent` stays `Unknown`. No new persisted enum member is added. The capability code identifies the turn.
15. The turn settles with the new completion status `InformationalReply`, listed in `ZeroChargeStatuses`. Credits charged: 0. No model call is made.

## Data / Schema

No schema change and no migration. `InformationalReply` (18 characters) fits the existing `CompletionStatus` column.

## API (if any)

No new or changed endpoint. `POST /api/ai/v1/query` returns the existing fields.

## AI Query Path (if any)

- V2 only (`specs/POLICY-V1-FREEZE.md`). Gate-routed capability `assistant_help`, route `assistant_help`, `GateRoutedOnly = true`, no slots.
- The rule is off unless `Feature154RoutingScope.IsActive` is true, and it stands down when the rollout decision for the capability does not execute the semantic route (`Rollback` or `Legacy` mode).
- No data is read. There is no missing-data or unsupported outcome.

## UI (if any)

No frontend or Telegram code change. Web shows the text through `MarkdownMessage` and the existing suggested-action buttons. Telegram shows the text and the existing `پیشنهادها:` list. The samples therefore appear twice, in the text and as actions, as approved in the design.

## Non-Functional Requirements

- No outbound call. The rule is a bounded lookup in fixed sets.
- Customer text is not logged by the new code.
- Authorization and credit reservation are unchanged. Reservation runs before the gate, so an account that is denied at reservation (for example, in cooldown) gets the existing denial, not this reply.

## Acceptance Criteria

- [ ] AC-01: Every phrase in the three lists routes to `assistant_help` and the model proposal provider is not called.
- [ ] AC-02: Punctuation, ZWNJ, Arabic-letter, and `ؤ` variants of a listed phrase match (`سلام!`, `چی می‌تونم بپرسم؟`, `چي ميتونم بپرسم`, `چه سؤال‌هایی می‌تونم بپرسم؟`).
- [ ] AC-03: A leading word from Requirement 4 before a pattern 2 or 3 phrase matches (`خب چی می‌تونم بپرسم؟`, `سلام، خودت را معرفی کن و بگو چه کارهایی بلدی؟`). The word alone (`خب`) does not. A pattern 2 phrase with `کامل`, `کاملا` or `کاملاً` matches (`خودت را کامل معرفی کن و بگو چه کارهایی بلدی؟`).
- [ ] AC-04: These keep today's route: `سلام، کگل چطور است؟`, `کگل`, `قیمت کگل`, `کامل`, `گزارش کامل کگل`, a message over 80 characters.
- [ ] AC-05: The Persian reply equals Requirement 6 character for character, and the English reply equals Requirement 7.
- [ ] AC-06: The response carries the four suggested actions of Requirement 8, in order.
- [ ] AC-07: Each sample's deterministic interpretation has the expected top capability, no missing slot, and one company mention equal to the example symbol.
- [ ] AC-08: In `NoavaranAmin` mode all four samples appear. In `CyclicalWaves` mode only the quarterly report and balance sheet appear, and the turn still succeeds.
- [ ] AC-09: Outcome is `Answered`, reason code `assistant_help`, `SemanticCapabilityCode` is `assistant_help`, and the persisted payload matches.
- [ ] AC-10: The turn finalizes with `InformationalReply`, credits charged 0, and the default pricing policy test pins `InformationalReply` as free.
- [ ] AC-11: After a generic refusal, a pattern 3 phrase gets the reply.
- [ ] AC-12: After a normal answer, a pattern 3 phrase gets the reply and the conversation task state (active capability, slots, version) is unchanged.
- [ ] AC-13: With a pending action, a listed phrase does not route to `assistant_help`.
- [ ] AC-14: With `Feature154RoutingScope.V1Frozen`, or inside `EnterFrozenV1`, a listed phrase takes today's route.
- [ ] AC-15: With the rollout mode `Rollback` or `Legacy`, a listed phrase takes today's route.
- [ ] AC-16: With `SymbolSnapshot:Enabled` on and an eligible ticker equal to a listed phrase in the test, the snapshot wins.
- [ ] AC-17: `assistant_help` never appears in the model prompt's capability list or in starter prompts.
- [ ] AC-18: The Telegram renderer shows the reply text and the suggestion list for this response.
- [ ] AC-19: `specs/155-.../queries.sql` is unchanged by this item.

No acceptance criterion concerns the T15 gap rate.

## Assumptions

- Production runs `NoavaranAmin` (OQ-008) and `MicrosoftAgentFrameworkV2` (`appsettings.json`; the deployed configuration was not read).
- OQ-011: the recorded assumption stands; a whole-message ticker match wins over this rule. Whether a listed phrase equals an eligible ticker has not been checked against the full list. Not a blocker.
- OQ-030 is open. **Under classifier v1 the reply still counts as `misrouted` until OQ-030 is answered**, for every phrase that `misroute_hint` in `queries.sql` matches: outcome 0 plus the hint gives `misrouted`. The hint covers messages starting with `سلام`, `hi` or `hello`, `معرفی کن`, and `چیکار می ?کنی`. Listed phrases outside the hint (for example `درود`, `چی می‌تونم بپرسم`) would land in `answered`. This is a description of classifier v1, not a success claim.
- The single fixed reply opens with `سلام!` for all three patterns, as decided in OQ-018.
