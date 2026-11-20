# User Story: Deep financial analysis with gpt-6.2-sol, optional web search and fixed credit tiers

## Feature Folder

`162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search`

## Layers / Area

Layers: backend | frontend | telegram-gateway
Area: AI · Billing · Conversations · Telegram

## Priority

To be set by the owner

## Complexity

XL

## Depends On

162-context-aware-financial-report-follow-up (released), 163-symbol-grounded-fallback-answer (released),
162 change `analytical-answer-and-report-score` (released v1.18.0), 163 change `answer-readability`

## References

- [design-proposal.md](design-proposal.md) (approved at Gate A on 2026-11-20; owner decisions PO-1 … PO-16 are binding)
- `specs/162-context-aware-financial-report-follow-up/user-story.md`, `specs/163-symbol-grounded-fallback-answer/user-story.md`
- `specs/POLICY-V1-FREEZE.md` (no V1 work)
- `docs/domain-glossary.md` terms used: Report Discussion, Active Report, credit, هِمَت
- Code: `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/`, `.../AI/SymbolGroundedFallback/`,
  `.../AI/ModelProviders/AiModelContracts.cs`, `src/backend/FinancialCopilot.Infrastructure/AI/ModelProviders/OpenAiHostedAiModelTransport.cs`,
  `.../AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`, `.../Billing/AiFacadeBillingHook.cs`,
  `.../Billing/DefaultPricingPolicy.cs`, `.../Authentication/TelegramAiAssistantAdapter.cs`,
  `.../Authentication/TelegramAssistantResponseRenderer.cs`, `src/frontend/src/components/app/message-list.tsx`,
  `src/frontend/src/components/app/prompt-input.tsx`, `src/frontend/src/components/mobile/mobile-composer.tsx`,
  `src/frontend/src/lib/chat.functions.ts`
- Data model: `AssistantMessagePayload` JSON in `MessageRow.AssistantPayloadJson` (optional members only); billing
  `UsageReservation` (unique `IdempotencyKey`)
- OpenAI docs: model `gpt-6.2-sol`, web search guide, pricing, Responses create reference (developers.openai.com)

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user (web, mobile PWA or Telegram),
I want to choose a simple or deep analysis, with or without web search, at a fixed credit price I can see before I send,
so that I get a professional, evidence-backed fundamental analysis of a report or a symbol, and I am never charged for
a 163 analysis I did not confirm.

## Business Context

162 and 163 answers summarise the statement instead of analysing it. The owner wants senior-analyst depth from
`gpt-6.2-sol`, optional web evidence, and a fixed, visible price per analysis. Because 163 is a fallback the user did
not ask for, it must ask before running.

## Scope

- `gpt-6.2-sol` for 162 and 163 only, with reasoning effort, output and input budgets per depth (configuration).
- Web search switch (default on) with a hard per-response search cap.
- Fixed credits per depth and web switch (8 / 12 / 16 / 25), backend-owned, shown before sending.
- 162 disposition step before the Sol call, so hand-backs never run Sol or web search.
- 163 offer → confirm / cancel on every client; the immediate-run path is removed.
- External evidence (`[Wn]` markers, consulted-source check) next to the unchanged internal numeric grounding.
- Web and mobile PWA controls; Telegram price line and 163 inline buttons.
- Actual API cost telemetry (monitoring only).

## Out Of Scope

- Any dynamic or usage-based credit pricing; any price change (owner decision only).
- Cash-flow family in 162 (OQ-110; waits for Feature 161).
- Telegram settings buttons for 162 (OQ-111 default).
- A bulkhead or any new commercial limit for Max (OQ-112 default).
- OpenAI Agent Builder / Agents SDK, new agents, GraphRAG, a financial rule engine or backend-written observations.
- V1, routing of other capabilities, session management, database migrations.

## Functional Requirements

1. **FR-1 Model.** 162 and 163 analysis calls use `GroundedAnalysis:Model` (`gpt-6.2-sol`) through the existing OpenAI
   client via `ModelKeyOverride`; every other workload keeps its model. Usage records the model actually called.
2. **FR-2 Profiles.** `GroundedAnalysis:Profiles:{Simple|Deep}` set reasoning effort, `MaxOutputTokens` (3,000 / 8,000),
   `InputTokenTarget`, timeout and `MaxWebSearchCalls` (1 / 2). Profiles never influence credits.
3. **FR-3 Prices.** `GroundedAnalysisPricing:Credits` holds SimpleNoWeb 8, SimpleWeb 12, DeepNoWeb 16, DeepWeb 25.
   Credits are a function of depth and web switch only. `PricingVersion` is a hash of the four amounts.
4. **FR-4 Options.** `analysisOptions { depth, webSearch, pricingVersion }` is optional on `POST /api/ai/v1/query`.
   Missing options use the defaults Deep and web on. No amount is accepted from a client.
5. **FR-5 Web search.** Web off: no search tool in the request. Web on: `web_search` tool with `tool_choice: auto`,
   `max_tool_calls` from the profile, `search_context_size` and user location from configuration, consulted sources
   included. Web on is charged at the web tier whether or not a search ran. Transports other than OpenAI reject web search.
6. **FR-6 162 flow.** Existing activation checks; price-version check (stale → zero-credit reply with current prices);
   disposition step (existing workload model, no tools); `NeedsDifferentData` / `OutsideReport` hand back as today
   without a Sol call; `MissingInReport` zero-credit reply; `Answered` → reserve the fixed credits, Sol call, layout,
   grounding, settlement.
7. **FR-7 163 offer.** After existing eligibility, single company and statement checks (no model call, no reservation),
   the reply keeps the original outcome and adds the offer, the four prices, defaults, balance sufficiency and Max
   status. The payload stores `GroundedAnalysisOffer` (offer id, question message id, company, symbol, newest period,
   pricing version, expiry).
8. **FR-8 163 resolution.** `ga1:{offerId}:confirm` / `ga1:{offerId}:cancel` is handled before Report Discussion. One
   reservation key derived from the offer id decides the single outcome. Confirm re-checks pricing version, newest
   period and company (a change → new offer, 0 credits), then reserves, runs Sol and settles once. Cancel claims the key
   at zero cost; later confirms are rejected. Repeated, replayed or concurrent requests never run the model twice or
   charge twice. Expired offers and deleted conversations never run.
9. **FR-9 Failures.** Provider failure, timeout, truncation, invalid output, grounding failure, `MissingInReport`,
   narrow request and `NotAnswerable` settle at zero. A failed 163 run returns a fresh offer.
10. **FR-10 Grounding.** Internal numbers: `NumericGroundingValidator` unchanged. External numbers: `[Wn]` marker,
    URL among consulted sources, `publishedDate` present, value equal to the declared value. An invalid list item is
    removed; an invalid takeaway line fails the answer. The server appends the sources block.
11. **FR-11 Billing.** Four completion statuses `GroundedAnalysisAnswered.{SimpleNoWeb|SimpleWeb|DeepNoWeb|DeepWeb}`
    settle the configured amount and are excluded from usage-based settlement in every billing mode. Max stays unlimited.
12. **FR-12 Telemetry.** Per analysis: depth, web switch, tier, disposition, input, cached, output and reasoning tokens,
    search calls, estimated USD cost, latency, grounding result, truncation. Never report values, question or answer.
13. **FR-13 Clients.** Web and PWA show the 162 composer bar and the 163 offer card with prices from the backend.
    Telegram shows a price line under discussable reports and sends the 163 offer with inline buttons (four tiers and
    cancel, callback prefix `ga1:`).
14. **FR-14 Continuity.** A confirmed 163 answer stores `GroundingReport`, `FallbackOf` and `ConfirmedOfferId`; the
    next question continues in 162.

## Data / Schema

No migration. `AssistantMessagePayload` gains optional members `GroundedAnalysisOffer`, `GroundedAnalysisRun` (depth,
web switch, credits tier, model) and `ExternalSources`; `SymbolGroundedFallback` origin gains `ConfirmedOfferId`. Old
JSON deserialises unchanged. Offer arbitration uses the existing unique `UsageReservation.IdempotencyKey`. New
operation code `AiQuery.GroundedAnalysisOfferCancel` priced at 0.

## API (if any)

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| POST | `/api/ai/v1/query` | + optional `analysisOptions { depth, webSearch, pricingVersion }` | + optional `groundedAnalysis { available, pricingVersion, prices[], defaults, unlimited, offer? }`, `sources[]` | Additive; no amount from clients |

## AI Query Path (if any)

V2 only. 162 stays in `PrepareStartMessageAsync`; the 163 offer stays at the result step; 163 confirmation and
cancellation are handled in `PrepareStartMessageAsync` before Report Discussion. Workload `ExplanationGeneration`.
Execution routes `report_discussion`, `symbol_grounded_fallback`.

## UI (if any)

- Web: compact bar in the composer area (`prompt-input.tsx`): segmented «ساده | عمیق», Globe toggle, cost sentence.
  163 offer card in `message-list.tsx` with the same controls and «تحلیل کن» / «انصراف».
- Mobile PWA: the same in `mobile-composer.tsx` and the mobile answer card; 44 px targets; 320 px without horizontal scroll.
- Light and dark theme tokens; RTL.
- Telegram: price line; 163 inline keyboard.

## Non-Functional Requirements

- Timeouts: model per profile (45 / 90 s), disposition 15 s, provider HTTP 120 s; no retry beyond the transport's
  bounded rate-limit retry.
- Authorization: offers and messages read only in the request's tenant/actor scope.
- Idempotency: request fingerprint includes `analysisOptions`; one settlement per reservation; one outcome per offer.
- Logs: identifiers and counts only.
- Prompt injection: report labels, history and web content are data.
- Startup validation of both configuration sections.

## Acceptance Criteria

- [ ] AC-1 A successful analysis is charged exactly 8, 12, 16 or 25 credits by depth and web switch only; 0, 1 or 2
      searches and any token count give the same amount.
- [ ] AC-2 Web on with no search performed is charged at the web tier.
- [ ] AC-3 Changing `MaxOutputTokens`, `ReasoningEffort`, `InputTokenTarget` or `MaxWebSearchCalls` does not change any price.
- [ ] AC-4 The request has no amount field; an extra amount field is ignored; UI prices come from the response.
- [ ] AC-5 A stale `pricingVersion` in 162 returns a zero-credit reply with current prices and no model call; a stale
      163 offer returns a new offer; usage-based settlement never applies to the four statuses, also in `Enforced` mode.
- [ ] AC-6 Provider failure, timeout, truncation, invalid output, grounding failure, `MissingInReport`, narrow request
      and `NotAnswerable` are charged 0 and the reservation is released.
- [ ] AC-7 A 162 hand-back runs neither the Sol call nor web search; only the normal route charges.
- [ ] AC-8 Before confirmation no 163 analysis model call and no reservation happen, on web, PWA, Telegram and API.
- [ ] AC-9 Cancel charges 0 and makes later confirms fail; repeated, replayed or concurrent confirms run the model
      once and charge once; an expired offer or a deleted conversation never runs; a changed company, period or price
      gives a new offer at 0 credits.
- [ ] AC-10 Insufficient credit is shown in the offer; confirm returns the standard insufficient-credit response with
      no model call.
- [ ] AC-11 Max users lose no credits; actual cost is recorded; confirmation idempotency holds.
- [ ] AC-12 Web off: the model request has no search tool.
- [ ] AC-13 Web on: `max_tool_calls` equals the profile value.
- [ ] AC-14 Web off: grounding results equal today's; an external number without `[W]` or with an unconsulted URL is
      not accepted.
- [ ] AC-15 After a confirmed 163 answer, the next question is answered by 162.
- [ ] AC-16 No regression in routing, hand-back, Feature 141 order, 163 eligibility, technical-analysis refusal and V1;
      other workloads still use their configured model.
- [ ] AC-17 Logs contain no report value, question or answer text.
- [ ] AC-18 Architecture: no rule engine or `observations`; Application has no Infrastructure reference; pricing and
      profiles have separate owners.
- [ ] AC-19 Web and PWA controls work in light and dark themes and at 320 px; Telegram shows the price line and the
      163 buttons.

## Assumptions

- Spike T00 confirms access to `gpt-6.2-sol` and the combination of `web_search` with `json_schema`; otherwise the
  documented fallback (tagged plain text in web mode) is used and recorded in the implementation report.
- `user_location.country = IR` is accepted by the API (checked in T00).
- Open questions OQ-108 … OQ-112 use the defaults in the design proposal until answered.
