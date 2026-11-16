# Report interpretation suggestion under shown reports

Status: Gate A: Approved by the project owner on 2026-11-16

**Layers / Area:** backend · AI, Conversations, Telegram

**References:** `specs/162-context-aware-financial-report-follow-up/user-story.md` (D1 families, D2 pricing, FR-1 routing), Feature 121 change «follow-up-suggestion-pipeline» (`FollowUpSuggestionPipeline`), `ReportDiscussionHandler`, `TelegramAssistantResponseRenderer`.

## Request

Under a shown quarterly income statement, balance sheet or monthly sales trend, add a follow-up suggestion that asks for its interpretation, for example «صورت سود و زیان فاذر را تفسیر کن» under the quarterly report of فاذر. The suggestion joins the existing ones.

## Problem found in discovery

A suggestion click sends its text with a `suggestedActionId`. Report Discussion deliberately skips every turn that carries one (`structured_action`), because existing suggestions are requests for specific data. A plain new suggestion would therefore go to the normal route and probably show the same report again.

## Design

1. **New kind `SuggestedActionKind.DiscussReport`**, capability code `report_discussion`, appended at the end of the enum. The web client renders suggestions without looking at the kind. Telegram lists them as text the user types, which reaches Report Discussion as an ordinary question.
2. **Report Discussion accepts this one kind.** When the turn's `suggestedActionId` identifies a `DiscussReport` suggestion stored on the active report's assistant message, the turn is handled like a typed question. The stored message must equal the sent text, and the suggestion must be at most 30 minutes old (the existing suggestion rules). Every other suggestion is still skipped as `structured_action`. The check reads only the messages the handler already loads.
3. **One shared step in the follow-up pipeline.** After the provider that owns the answer has built its suggestions, the pipeline puts the interpretation suggestion **first** (owner decision) when:
   - Report Discussion is enabled (`ReportDiscussion:Enabled`),
   - the answer is Answered or PartialAnswer, and
   - the answer holds a report Report Discussion can discuss, using the same conditions as `ActiveReportResolver` (one shared definition).

   Answers that no provider owns are left unchanged.
4. **Texts:**
   - quarterly income statement: «صورت سود و زیان {نماد} را تفسیر کن»
   - balance sheet: «ترازنامه {نماد} را تفسیر کن»
   - monthly sales trend: «روند فروش ماهانه {نماد} را تفسیر کن»
5. **Limit of 5 suggestions (owner decision).** The pipeline returns at most 5 suggestions: the interpretation suggestion plus up to 4 existing ones. Telegram shows up to 5 instead of 4. Providers keep their own limits.
6. **Pricing unchanged (D2):** an answered interpretation costs 1 credit; non-answers cost 0.

## Out of scope

- The cash-flow family (deferred with Feature 161).
- Changing the guidance-service suggestions for answers no provider owns.

## Risks

- Interpreting a whole statement mentions more numbers, so the grounding check can reject more answers. The BUG-031 changes reduce this; the live check confirms it.
- The model might classify «… را تفسیر کن» as a request for a new report and hand it back; the live check covers this.
