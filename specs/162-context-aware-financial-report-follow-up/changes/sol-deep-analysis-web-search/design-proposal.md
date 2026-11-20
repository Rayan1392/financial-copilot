# Change proposal — Deep financial analysis with gpt-6.2-sol, optional web search and fixed credit tiers (Features 162 and 163)

Status: Gate A: Approved by the owner on 2026-11-20

## Feature Folder   `162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search`
## Layers / Area    backend · frontend · telegram-gateway · AI · Billing · Conversations · Telegram
## Priority         to be set by the owner
## Complexity       XL
## Depends On       162 (released), 163 (released), 162 change `analytical-answer-and-report-score` (released v1.18.0), 163 change `answer-readability`
## References       `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/`, `src/backend/FinancialCopilot.Application/AI/SymbolGroundedFallback/`, `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs`, `src/backend/FinancialCopilot.Infrastructure/AI/ModelProviders/OpenAiHostedAiModelTransport.cs`, `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`, `src/backend/FinancialCopilot.Infrastructure/Billing/AiFacadeBillingHook.cs`, `src/backend/FinancialCopilot.Infrastructure/Billing/DefaultPricingPolicy.cs`, `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAiAssistantAdapter.cs`, `src/frontend/src/components/app/message-list.tsx`, OpenAI docs (model `gpt-6.2-sol`, web search guide, pricing, Responses create reference)

This change touches both features. It is recorded under 162 because 162 owns the shared building blocks
(`GroundedAnswerLayout`, `NumericGroundingValidator`, `ReportDiscussionContextBuilder.RenderReport`, the discussion
reader); 163 reuses them. The design was reviewed in two rounds on 2026-11-19 and 2026-11-20; this file records the
approved revision.

## 1. Owner decisions (binding)

| # | Decision |
|---|---|
| PO-1 | Analysis model for 162 and 163: `gpt-6.2-sol`, through the existing OpenAI provider and Responses API. Other workloads keep their model. |
| PO-2 | All financial reasoning and insight discovery is done by the LLM. No financial rule engine, observation builder, insight detector, industry rules or backend-written `observations` field. The backend owns data, deterministic calculations, units, context, budgets, billing, validation, persistence and telemetry. |
| PO-3 | No OpenAI Agent Builder, OpenAI Agents SDK, new multi-agent design, research agent or GraphRAG. Microsoft Agent Framework and the existing V2 workflow stay. |
| PO-4 | Two analysis depths, `Simple` and `Deep`, both on `gpt-6.2-sol`. Default `Deep`. |
| PO-5 | Web search is a user switch. Default enabled. Enabled = the model may search (not must). Disabled = no search tool is sent. |
| PO-6 | **Fixed credits (approved product decision):** Simple without web 8, Simple with web 12, Deep without web 16, Deep with web 25. Determined only by depth and web switch. Not dependent on tokens, reasoning tokens, search calls, benchmark or actual API cost. Web enabled is charged at the web tier even when the model does not search. Changed only by an explicit owner decision. |
| PO-7 | No dynamic credit pricing and no usage-based settlement for these features. Actual OpenAI cost is recorded for telemetry and monitoring only. |
| PO-8 | Token budgets are technical settings, independent of credits: initial `MaxOutputTokens` 3,000 (Simple) and 8,000 (Deep), configurable. Reasoning effort and input budget are also technical settings. |
| PO-9 | The backend is the only source of prices. The frontend shows prices received from the backend and never sends an amount. When prices change while an old page is open, the new price is shown before execution, and in the confirmation path consent is asked again. |
| PO-10 | Feature 163 never runs without explicit user confirmation, on every client (web, mobile PWA, Telegram, API). A client that cannot give a valid confirmation never runs the analysis. Cancellation is applied by the backend; a cancelled offer cannot be confirmed later. |
| PO-11 | Before confirmation in 163: no analysis model call and no credit reservation. After confirmation: balance check, reservation, model call, one settlement at the fixed price. |
| PO-12 | Max (unlimited) keeps the current unlimited mechanism; actual API cost is monitored; no new commercial limit without the owner. |
| PO-13 | Numeric grounding stays. Sapio report figures are checked against Sapio data; external figures are kept apart and traceable. |
| PO-14 | Unnecessary web search for 162 turns that are handed back to the normal route is avoided, without a rule engine or a new agent. |
| PO-15 | Benchmarks measure quality, actual cost, latency, tokens, search calls and error rate. A benchmark never changes prices. |
| PO-16 | Economic analysis is reported as financial risk only; no automatic change to prices, plans or top-ups. |

Plans and top-ups used for the risk note (§11): Plus 260 credits / 1,000,000 toman; Pro 2,600 / 5,000,000; Max
unlimited / 25,000,000; 90-day period. Top-ups 100 / 250,000, 500 / 1,000,000, 1,000 / 1,650,000. Rate 280,000 toman/USD.

## 2. Discovery summary

| # | Finding | Where |
|---|---|---|
| F1 | 162 enters in `PrepareStartMessageAsync` before the dialogue gate; one structured call returns answer and disposition; hand-backs run the normal route. | `FinancialCopilotWorkflowDefinition.cs` (`TryReportDiscussionAsync`), `ReportDiscussionHandler.cs` |
| F2 | 163 runs in the result step after the outcome is known and calls the model immediately (no confirmation). Its billing uses a derived reservation (`:fallback163`); with too little credit the turn is left unchanged. | `FinancialCopilotWorkflowDefinition.cs` (`TrySymbolGroundedFallbackAsync`), `SymbolGroundedFallbackHandler.cs` |
| F3 | The OpenAI transport already uses the Responses API (`POST responses`, `text.format=json_schema`, `max_output_tokens`) but sends no `reasoning`, no hosted tools and no `include`, and does not read `reasoning_tokens` or search sources. | `OpenAiHostedAiModelTransport.cs` |
| F4 | One OpenAI client is registered with one `ModelKey` (`gpt-6-luna`) for every workload; `AiModelExecutionService` stamps the descriptor model key on usage. | `ServiceCollectionExtensions.cs`, `AiModelProviderServices.cs` |
| F5 | Fixed price today: `ReportDiscussionBillingPolicy.ApplyFixedAnsweredPrice` returns 2 credits for status `ReportDiscussionAnswered`. Unlimited plans skip capacity checks. The reservation idempotency key has a unique index. `UsageBasedCredits:Mode` is `Legacy`, so actual cost is not computed today. | `AiFacadeBillingHook.cs`, `DefaultPricingPolicy.cs`, `BillingEntityConfigurations.cs` |
| F6 | `ReportFamily` has QuarterlyIncomeStatement, BalanceSheet and MonthlySales only; the cash-flow family waits for Feature 161 (OQ-110). | `ReportDiscussionContracts.cs` |
| F7 | Web chips send `suggestedActionId`; Telegram renders AI suggestions as text only (no buttons) but has inline-keyboard callbacks for other features (`tr.`, `rd.`, `nt.`, `ah.`). | `message-list.tsx`, `TelegramAssistantResponseRenderer.cs`, `TelegramAiAssistantAdapter.cs` |
| F8 | OpenAI docs: `gpt-6.2-sol` supports Responses, structured outputs, reasoning (effort `none` rejected) and the `web_search` tool; `max_output_tokens` includes reasoning tokens; `max_tool_calls` caps built-in tool calls per response ("further attempts … ignored"); `search_context_size` does not guarantee a token count; `include: web_search_call.action.sources` lists consulted URLs; `filters.allowed_domains` up to 100. Combining `web_search` with `json_schema` is **not documented** (spike T00). Prices: $2 / $0.10 cached / $10 per 1M tokens; web search $10 per 1k calls plus search-content tokens at the model's input rate. | developers.openai.com model, web-search, pricing and Responses pages |

## 3. Architecture

- Optional fields only on existing contracts: `AiModelRequest` gets `ModelKeyOverride`, `ReasoningEffort` and
  `WebSearch` (`MaxToolCalls`, `SearchContextSize`, `AllowedDomains`, `UserLocationCountry`); `AiModelCapability.WebSearch`;
  `AiExecutionUsageFacts` gets `ReasoningTokens` and `WebSearchCalls`; `AiModelResult` gets `ConsultedSources`.
- `ConfiguredHostedAiModelClient` sends `ModelKeyOverride ?? Descriptor.ModelKey` and usage records the model actually
  called. Transports other than OpenAI reject a `WebSearch` request with `CapabilityUnavailable` (zero credits, no
  hidden fallback to another model).
- New Application folder `AI/GroundedAnalysis` with two separate owners:
  - `GroundedAnalysisPricing` — the four fixed credit amounts from configuration and a derived `PricingVersion`
    (hash of the four amounts). Inputs: depth and web switch only.
  - `GroundedAnalysisProfiles` — technical budgets per depth (model, reasoning effort, output and input budgets,
    timeout, maximum search calls, search options).
  Neither reads the other; a unit test and an architecture test keep them apart.
- Persistence: optional members in the existing `AssistantMessagePayload` JSON only. No migration.

Configuration (two sections, validated at startup):

```json
"GroundedAnalysis": {
  "Enabled": false,
  "ProviderKey": "OpenAI",
  "Model": "gpt-6.2-sol",
  "DefaultDepth": "Deep",
  "DefaultWebSearch": true,
  "OfferLifetimeMinutes": 30,
  "Profiles": {
    "Simple": { "ReasoningEffort": "low",    "MaxOutputTokens": 3000, "InputTokenTarget": 6000,  "TimeoutSeconds": 45, "MaxWebSearchCalls": 1 },
    "Deep":   { "ReasoningEffort": "medium", "MaxOutputTokens": 8000, "InputTokenTarget": 10000, "TimeoutSeconds": 90, "MaxWebSearchCalls": 2 }
  },
  "WebSearch": { "SearchContextSize": "low", "UserLocationCountry": "IR", "AllowedDomains": [] },
  "Disposition": { "Model": null, "MaxOutputTokens": 200, "TimeoutSeconds": 15 }
},
"GroundedAnalysisPricing": {
  "Credits": { "SimpleNoWeb": 8, "SimpleWeb": 12, "DeepNoWeb": 16, "DeepWeb": 25 }
}
```

`Disposition:Model = null` means the existing workload model. Changing `GroundedAnalysisPricing` is an owner decision
recorded in the operations runbook; no code changes it.

## 4. Fixed credit billing

- Credits = `GroundedAnalysisPricing.CreditsFor(depth, webSearch)`.
- Four completion statuses: `GroundedAnalysisAnswered.SimpleNoWeb`, `.SimpleWeb`, `.DeepNoWeb`, `.DeepWeb`.
  `ApplyFixedAnsweredPrice` takes the amount for these statuses from `GroundedAnalysisPricing`. They are excluded from
  the usage-based path even if `UsageBasedCredits:Mode` becomes `Enforced`. The ledger records the tier.
- Flow: credits → `EnsureReservedCapacityAsync(credits)` (existing) → model → answered and valid: settle the tier
  status (the fixed amount) / anything else (provider failure, timeout, truncation, invalid output, grounding failure,
  `MissingInReport`, narrow request, `NotAnswerable`): existing zero-charge status, reservation released.
- Idempotency: existing reservation and ledger keys; one settlement per reservation.
- Max: the unlimited path is unchanged; no credits are lost; the reservation row with its unique key is still written,
  so 163 confirmation idempotency works for Max too.
- Actual cost (input, cached, output and reasoning tokens, search calls, estimated USD from `ModelPrices` with a
  `gpt-6.2-sol` row and a per-search price) goes to logs and metrics only. No path from it to billing.
- `ReportDiscussionAnswered` (2 credits) stays only so old reservations replay correctly; new settlements never use it.

## 5. Feature 162 flow

Entry point unchanged (`PrepareStartMessageAsync` → `TryReportDiscussionAsync`).

1. Existing state checks (flag, existing conversation, no pending clarification, active report, interpretation chip).
2. Price check: a request whose `pricingVersion` differs from the current one gets a zero-credit reply «تعرفه به‌روز
   شده است» with the current prices; no model call.
3. **Disposition step**: one light structured call with the existing workload model, **no tools**, output
   `{disposition}` (the decision the single call makes today, moved first). `NeedsDifferentData` / `OutsideReport` →
   hand-back exactly as today, no Sol call and no web search (PO-14); its cost is absorbed and logged. `MissingInReport`
   → existing zero-credit reply. `Answered` → continue.
4. Credits from `analysisOptions` (missing options → defaults Deep + web); reservation; insufficient credit → the
   existing insufficient-credit response without the Sol call.
5. Sol call with the profile; the web tool only when `webSearch` is true. Output
   `{disposition: Answered|MissingInReport, answer, usedEvidenceIds, reportScore, externalSources}`.
6. `GroundedAnswerLayout.Normalize` → internal and external grounding (§9) → settlement.
7. The report score keeps the v1.18.0 rules (quarterly income statement, overall analysis only); OQ-108 may extend it.

## 6. Feature 163 confirmation flow

The immediate-run path is removed; 163 runs only through offer and confirmation.

**Offer** (same place as today, after the outcome is known): existing eligibility, exactly one company, and a usable
quarterly statement of that symbol and provider are checked, with **no model call and no reservation**. The original
outcome (`Unsupported`, or the no-recent-analysis `NoData`) is kept so Feature 155 still counts the question. The reply
adds the offer sentence, the four prices, the defaults, whether the balance covers the default, and «نامحدود» for Max.
Payload: `GroundedAnalysisOffer { OfferId, QuestionMessageId, CompanyCanonicalId, Symbol, NewestPeriod, PricingVersion,
ExpiresAt }` (the question is referenced by message id, not copied).

**Resolution** (in `PrepareStartMessageAsync`, before Report Discussion): `suggestedActionId` `ga1:{offerId}:confirm` or
`ga1:{offerId}:cancel`, with `analysisOptions` on confirm. Checks: the offer is on an assistant message of the same
conversation (tenant/actor-scoped reader), not expired. **One arbiter:** a reservation key derived from `OfferId`
(unique index) — exactly one of confirm or cancel succeeds, once.

| Case | Behavior |
|---|---|
| Confirm | Read the question by `QuestionMessageId`. If `PricingVersion`, `NewestPeriod` or the company changed → no run, a new offer (consent again), 0 credits. Otherwise reserve the fixed tier, run Sol, settle once. |
| Cancel | Claim the offer key with a zero-priced operation code `AiQuery.GroundedAnalysisOfferCancel` and settle it at zero at once. Reply «تحلیل انجام نشد؛ اعتباری کسر نشد.» Later confirms are rejected. |
| Repeated or concurrent confirm/cancel, replay | Key exists: committed → «این پیشنهاد قبلاً انجام یا لغو شده است»; reserved → «در حال انجام است». 0 credits, no model call. |
| Insufficient credit | Shown in the offer; on confirm the standard insufficient-credit response; the offer stays until it expires. The FR-12 deviation of 163 is withdrawn. |
| Expired offer / deleted conversation | «پیشنهاد منقضی شده است» / existing `ConversationNotFound`. No run. |
| Provider failure, timeout, grounding failure | 0 credits; a fresh offer is returned for a retry (each offer is single-use). |
| `NotAnswerable` | 0 credits; the existing income-statement offer (FR-9). |
| Answered | Stores `GroundingReport`, `FallbackOf` and `ConfirmedOfferId`; the next question continues in 162. |

**Telegram:** the offer is sent with inline buttons — four tiers, each labelled with its price, and «انصراف».
Callback data `ga1:{offerId N}:{tier|x}` (about 45 bytes, under the 64-byte limit) reaches the same backend path.
Without these buttons nothing runs. 163 eligibility, order after the Feature 141 fallback, single-company rule,
technical-analysis refusal and excluded routes are unchanged.

## 7. Frontend / backend contract

Request (`POST /api/ai/v1/query`, optional, additive):

```json
{ "analysisOptions": { "depth": "deep", "webSearch": true, "pricingVersion": "a1b2c3" } }
```

- No amount field. `pricingVersion` is the hash of prices the backend sent earlier; it cannot set a price. It is
  needed only to detect a page showing old prices (PO-9). 163 does not need it (the offer stores the version).
- `analysisOptions` is part of `AiQueryRequestFingerprint`.

Response / payload:

```json
"groundedAnalysis": {
  "available": true,
  "pricingVersion": "a1b2c3",
  "prices": [ { "depth": "simple", "webSearch": false, "credits": 8 } ],
  "defaults": { "depth": "deep", "webSearch": true },
  "unlimited": false,
  "offer": { "offerId": "…", "symbol": "قمرو", "expiresAt": "…", "sufficientBalance": true }
}
```

`available` comes with a discussable report or a 162 answer; `offer` with a 163 offer. Answers carry
`sources: [{ id, title, url, publishedAt }]`.

## 8. UI

- **162 (composer area):** when the last reply has `groundedAnalysis.available`, a compact bar: segmented «ساده |
  عمیق», a Globe icon toggle for web, and «اگر پاسخ از روی همین گزارش باشد، با تنظیمات انتخاب‌شده ۲۵ اعتبار مصرف
  می‌شود.» (amount from `prices`). The choice is remembered in `localStorage` (per-viewer convenience).
- **163 (offer card):** the same two controls, the live cost sentence «این تحلیل با تنظیمات انتخاب‌شده ۲۵ اعتبار مصرف
  می‌کند.», and «تحلیل کن» / «انصراف». The card locks after a click. Insufficient balance: «تحلیل کن» disabled with a
  purchase link. Max: «با اشتراک Max اعتباری کسر نمی‌شود.»
- Mobile PWA: the same pieces in `mobile-composer` and the mobile answer card; 44 px touch targets; no horizontal scroll
  at 320 px. Existing theme tokens, light and dark. No new panel.
- Telegram: one price line under each discussable report («پرسش تحلیلی درباره‌ی این گزارش: عمیق با وب، ۲۵ اعتبار»),
  so the price is known before sending; the 163 buttons of §6.

## 9. Model and web search

- `gpt-6.2-sol` via `ModelKeyOverride`, only for these features; `gpt-6-luna` stays for every other workload.
- Web disabled: no tool is sent, so no search is possible.
- Web enabled: `tools: [{ type: "web_search", search_context_size: "low", user_location: { country: "IR" } }]`,
  `tool_choice: auto`, `include: ["web_search_call.action.sources"]`, `max_tool_calls` 1 (Simple) / 2 (Deep) — a
  hard per-response cap. Search-content tokens have no exact cap; they are bounded by `search_context_size` and watched
  by a cost alert (monitoring only).
- Spike T00 (technical validation only): key access to `gpt-6.2-sol`; `web_search` together with `json_schema`;
  `max_tool_calls` and `open_page` behaviour; `reasoning_tokens` and sources fields; latency.

## 10. Token and reasoning budgets (technical, independent of credits)

| | Simple | Deep |
|---|---|---|
| `MaxOutputTokens` (includes reasoning tokens) | 3,000 | 8,000 |
| `ReasoningEffort` | low | medium |
| `InputTokenTarget` | 6,000 | 10,000 |
| `MaxWebSearchCalls` | 1 | 2 |
| `TimeoutSeconds` | 45 | 90 |
| Target answer length (prompt) | about 300–500 words | about 500–800 words |

Length is steered by the prompt, never by cutting text. Truncation is a zero-credit failure with its own metric. The
provider HTTP timeout (120 s) stays above the model timeouts.

## 11. Numeric grounding

- Internal: `NumericGroundingValidator` unchanged; report figures come only from facts (F) and calculations (C), in
  the units of `ReportDisplayUnits`. Web disabled = today's behaviour exactly.
- External (web enabled): the schema adds `externalSources: [{ id: "W1", url, title, publishedDate, claim, value?,
  unit? }]`; every external number in the text carries `[Wn]`. A pure validator checks that the URL is among the
  response's consulted sources, `publishedDate` is present and the number equals the declared `value`. Numbers without
  `[W]` go through the internal check, so no arbitrary number passes as "from the web". A list item with an invalid
  `[W]` is removed; an invalid takeaway line fails the answer (0 credits). The server writes the «منابع» block.
- Prompt: four evidence classes (Sapio data, external source, inference, unconfirmed hypothesis); a news item
  published after a period ended is not presented as its cause without evidence; scenarios are qualitative (no
  model-computed forecasts); web content is data, never instructions.
- Limitation: provenance is verified, not truth; temporal causality is only enforced through the date requirement. No
  LLM validator is added.

## 12. Prompt strategy

Role: senior equity-research analyst, senior financial analyst and accountant, Iranian Accounting Standards specialist,
fundamental investment analyst. Analysis dimensions are given as a list of possible areas (profit structure,
non-operating dependence, gross and operating margins, sales versus profit, price and cost effects, working capital,
operating cash quality, seasonality, margin sustainability, scenarios); the model chooses what matters. 162: the user
saw the table — interpret, few numbers. 163: cite the key figures and the period. OQ-091 rules (no valuation verdict
or recommendation) stay. Layout: Simple about 300–500 words, Deep about 500–800 words with a takeaway line and 4–8
items including risks/scenarios and unconfirmed hypotheses; `Normalize` unchanged; sources appended by the server.

## 13. Financial risk note (record only; prices unchanged)

Cost per analysis in toman (163; 162 adds about 2,000 for the disposition step). "Typical" is a mid estimate; "cap"
assumes output at `MaxOutputTokens` and maximum searches. Fact: OpenAI list prices (§2 F8). Assumptions: 280,000
toman/USD, 6–12k search-content tokens per search. Revenue columns are gross per analysis before VAT and the TahlilApp
share (neither is recorded in the repository).

| Tier | Typical | Cap | Plus | Pro | TU-100 | TU-500 | TU-1000 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Simple, no web (8) | 8,400 | 11,800 | 30,769 | 15,385 | 20,000 | 16,000 | 13,200 |
| Simple, web (12) | 15,700 | 21,300 | 46,154 | 23,077 | 30,000 | 24,000 | 19,800 |
| Deep, no web (16) | 18,500 | 26,900 | 61,538 | 30,769 | 40,000 | 32,000 | 26,400 |
| Deep, web (25) | 33,000 | 45,900 | 96,154 | 48,077 | 62,500 | 50,000 | 41,250 |

- **RF-1:** On Pro and the 500/1,000 top-ups the web tiers have thin typical margins (about 20–35 % gross); at cap
  cost, Simple+web on TU-1000 and Deep+web on Pro, TU-500 and TU-1000 exceed gross revenue.
- **RF-2 (Max):** at a typical Deep+web cost and an assumed 10 % VAT, about 690 analyses per 90 days (about 7.6 a
  day) consume the plan's revenue. Monitoring and alerting only.

## 14. Technical risks

| Risk | Mitigation |
|---|---|
| R1 `web_search` with `json_schema` not documented | Spike T00 first; fallback: plain-text output with fixed tags in web mode only. |
| R2 Search-content tokens have no exact cap | `search_context_size: low`, per-analysis cost metric and alert. |
| R3 Deep + web latency 30–90 s | Check frontend server-function, gateway and Telegram timeouts (T12, T13); existing progress UI. |
| R4 External validation proves provenance, not truth | Stated in the answer's evidence classes; content matching is a later step if the live check needs it. |
| R5 Disposition step adds a light call (about 1–2 s, about 2,000 toman) | Measured in T15. |
| R6 Cancel uses a zero-priced reservation | Tested with a zero balance in T04 (`availableBalance < minimum` with minimum 0 passes). |
| R7 162 follow-ups move from 2 credits to 8–25 | Price shown before sending (PO-9). |

## 15. Rollback

- 162: `GroundedAnalysis:Enabled=false` restores today's Report Discussion (current model, 2 credits).
- 163: `SymbolGroundedFallback:Enabled=false` leaves the turn unchanged. No rollback path runs 163 without confirmation.

## 16. Open questions (`specs/open-questions.md`)

None blocks Gate B; each has a default that the plan uses until answered.

- **OQ-108** Deep score with four sub-scores and a reason each, summed by the server? Default: v1.18.0 behaviour.
- **OQ-109** `allowed_domains` restricted to primary sources, or open web with prompt priority? Default: open web.
- **OQ-110** Cash-flow family in 162 waits for Feature 161? Default: out of this change.
- **OQ-111** Telegram 162 settings buttons, or defaults with the price line only? Default: defaults with the price line.
- **OQ-112** Technical bulkhead of one in-flight analysis per user? Default: not applied.
