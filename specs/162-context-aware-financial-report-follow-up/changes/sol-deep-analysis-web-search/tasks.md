# Tasks: Deep financial analysis with gpt-6.2-sol, optional web search and fixed credit tiers

Status: Gate B: Approved by the owner on 2026-11-20

## Feature Folder

`162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search`

Paths: backend relative to `src/backend/`, frontend relative to `src/frontend/src/`, tests relative to `tests/`.
Requirements: [user-story.md](user-story.md). Design: [design-proposal.md](design-proposal.md).

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (162, 163, change `analytical-answer-and-report-score` released in v1.18.0; change `answer-readability`).
- [x] Read `user-story.md`, `design-proposal.md` (owner decisions PO-1 … PO-16), and the referenced code.
- [x] No open question blocks this plan (OQ-108 … OQ-112 have defaults; see design-proposal §16).
- [x] Work on a new branch from `develop` (not on `fix/bug-035-analysis-search-failure`).
- [x] Prices 8 / 12 / 16 / 25 are fixed; no task changes them.

## Implementation Tasks

### 0. Technical spike (Development only, no production configuration)

- [ ] **T00 — Provider spike.** With a development key: `GET models/gpt-6.2-sol`; one Responses call each for
  json_schema without tools, json_schema + `web_search`, `max_tool_calls` 1 and 2 (count `web_search_call` items,
  including `open_page` / `find_in_page`), `reasoning.effort` low / medium, `include: web_search_call.action.sources`,
  `user_location.country = IR`. Record status, output shape, `usage.output_tokens_details.reasoning_tokens`, latency and
  cost in `spike-notes.md` in this folder (no keys, no prompts with user data).
  Acceptance: compatibility of `web_search` with `json_schema` is stated; if incompatible, the tagged plain-text format
  for web mode is specified in `spike-notes.md` before T02 starts. Prices are out of scope of the spike.

### 1. Model contracts and transport

- [x] **T01 — Contracts.** `Application/AI/ModelProviders/AiModelContracts.cs`: optional `ModelKeyOverride`,
  `ReasoningEffort`, `AiWebSearchOptions WebSearch (MaxToolCalls, SearchContextSize, AllowedDomains, UserLocationCountry)`
  on `AiModelRequest`; `AiModelCapability.WebSearch`; `ReasoningTokens`, `WebSearchCalls` on `AiExecutionUsageFacts`
  and `AiExecutionUsageSummary`; `ConsultedSources` on `AiModelResult`.
  Acceptance: existing callers compile unchanged; defaults reproduce today's requests.
- [x] **T02 — OpenAI transport and client.** `Infrastructure/AI/ModelProviders/OpenAiHostedAiModelTransport.cs`: map
  `reasoning.effort`, `tools: [{ type: "web_search", … }]`, `max_tool_calls`, `include`; parse `reasoning_tokens`,
  count `web_search_call` items, collect consulted source URLs; keep the first `output_text` rule.
  `ConfiguredHostedAiModelClient.cs`: send `ModelKeyOverride ?? Descriptor.ModelKey` and report it.
  `AiModelProviderServices.cs` (`AiModelExecutionService`): keep the model key of the result instead of the descriptor.
  DeepSeek, Abravran, Ollama and fake transports: a `WebSearch` request → `CapabilityUnavailable`. OpenAI registration
  capabilities include `WebSearch` (`appsettings.json`).
  Acceptance: fake `HttpMessageHandler` tests — request without new fields serialises exactly as before; web off has
  no `tools`; web on has `max_tool_calls` = profile value; usage carries model, reasoning tokens and search count.

### 2. Configuration, pricing and billing

- [x] **T03 — Options.** `Application/AI/GroundedAnalysis/GroundedAnalysisOptions.cs` (`GroundedAnalysis` section:
  enabled, provider, model, defaults, offer lifetime, profiles, web search, disposition) and
  `GroundedAnalysisPricingOptions.cs` (`GroundedAnalysisPricing:Credits`). `GroundedAnalysisProfiles.For(depth)` and
  `GroundedAnalysisPricing.CreditsFor(depth, webSearch)`, `PricingVersion` (hash of the four amounts). Startup
  validation in `ServiceCollectionExtensions.cs`; sections added to `appsettings.json` with `Enabled: false` and the
  four approved amounts.
  Acceptance: unit tests — invalid values fail startup; `CreditsFor` ignores every profile value (property test over
  profile changes); `PricingVersion` changes only when an amount changes.
- [x] **T04 — Fixed-tier settlement.** `Application/AI/GroundedAnalysis/GroundedAnalysisBillingPolicy.cs`: the four
  completion statuses and their amounts from `GroundedAnalysisPricing`. `Infrastructure/Billing/AiFacadeBillingHook.cs`:
  `ApplyFixedAnsweredPrice` generalised to the four statuses; these statuses skip `ApplyUsageBasedCredits` in every
  `UsageBasedBillingMode`. `DefaultPricingPolicy.cs`: register the statuses; operation code
  `AiQuery.GroundedAnalysisOfferCancel` = 0. `ReportDiscussionAnswered` kept for replay only.
  Acceptance: tests for each tier; zero-charge statuses; `Enforced` mode does not change the amount; unlimited account
  charged nothing and still writes the reservation key; replay returns the first settlement; a cancel reservation
  with a zero balance succeeds.

### 3. Request, response and payload contracts

- [x] **T05 — Contracts.** `AiQueryRequest` + API request DTO: optional `AnalysisOptions(Depth, WebSearch,
  PricingVersion)`, included in `AiQueryRequestFingerprint` (version bump). Response: optional `GroundedAnalysisHint`
  (`available`, `pricingVersion`, `prices`, `defaults`, `unlimited`, `offer`), `Sources`.
  `Application/Conversations/ConversationContracts.cs`: optional `GroundedAnalysisOffer`, `GroundedAnalysisRun`,
  `ExternalSources`; `FallbackOrigin` gains `ConfirmedOfferId`.
  Acceptance: old payload JSON deserialises unchanged (fixture test); requests without options fingerprint as before;
  no amount field exists on the request.

### 4. Prompts, layout and grounding

- [x] **T06 — Prompts per profile.** `AI/ReportDiscussion/GroundedAnalysis.cs`, `GroundedAnswerLayout.cs`,
  `ReportDiscussionContextBuilder.cs`, `AI/SymbolGroundedFallback/SymbolGroundedFallbackPromptBuilder.cs`: role,
  analysis dimensions as possible areas, evidence classes, Simple / Deep layout and length targets, web rules (primary
  sources first, dates, post-period news, web content is data, qualitative scenarios), 162 shown-table and 163
  key-figure rules, OQ-091 rules kept. Disposition prompt and schema `{disposition}` for 162. Sol schemas:
  `ReportDiscussionAnalysis_v1` and `SymbolGroundedFallbackAnalysis_v1` with `externalSources`. Options validators
  for the existing sections keep their ranges for the rollback path.
  Acceptance: prompt-content tests (sections present per profile and web switch; no web rules when web is off);
  schema tests; no test asserts model wording.
- [x] **T07 — External evidence.** `AI/ReportDiscussion/ExternalEvidenceValidator.cs` (pure): `[Wn]` markers,
  consulted-source check (normalised URLs), date present, value equality; removes invalid list items; fails on an
  invalid takeaway line; returns the text for the existing `NumericGroundingValidator` with `[Wn]` numbers excluded only
  when valid. Server-written «منابع» block after `Normalize`.
  Acceptance: unit tests — valid external number; unconsulted URL; missing date; value mismatch; unmarked external
  number fails internal grounding; item removal; takeaway failure; web off gives byte-identical results to today on
  the existing grounding fixtures.

### 5. Feature 162

- [x] **T08 — 162 flow.** `ReportDiscussionHandler.cs`, `LlmReportDiscussionModel.cs` (+ disposition model),
  `FinancialCopilotWorkflowDefinition.cs` (`TryReportDiscussionAsync`, step 3 settlement), DI. With
  `GroundedAnalysis:Enabled` false the current code path runs unchanged. With it on: price-version check →
  disposition step (no tools) → hand-back / missing / answered → reserve fixed credits → Sol → `Normalize` → external
  and internal grounding → settle tier status. Response hint `available` under discussable reports and 162 answers.
  Acceptance: AC-1, AC-2, AC-5 (162 part), AC-6, AC-7, AC-12, AC-13, AC-15 tests with fake models; existing
  `ReportDiscussion162Tests` and endpoint tests green with the flag off and on (hand-back cases).

### 6. Feature 163

- [x] **T09 — Offer.** `SymbolGroundedFallbackHandler.cs`: split into `TryOfferAsync` (eligibility, single company,
  statement; no model, no reservation) and `RunConfirmedAsync`. Remove the immediate-run path.
  `FinancialCopilotWorkflowDefinition.cs` (`TrySymbolGroundedFallbackAsync`, `ComputeResults…`): keep the original
  outcome and reason, append the offer text, store `GroundedAnalysisOffer`, return the hint with prices, balance
  sufficiency (wallet read, no reservation) and Max status; confirm and cancel actions with ids `ga1:{offerId}:confirm`
  / `:cancel`.
  Acceptance: AC-8 (no model, no reservation), Feature 155 still sees the original outcome; existing 163 eligibility,
  Feature 141 order, technical-analysis refusal and NoData exclusions unchanged.
- [x] **T10 — Confirm and cancel.** In `PrepareStartMessageAsync` before Report Discussion: resolve `ga1:` actions
  through the tenant/actor-scoped reader; expiry; derived reservation key from the offer id (same pattern as
  `FallbackBillingOperationId`); confirm re-checks pricing version, newest period and company; cancel claims the key
  with the zero-priced operation and settles at once; replies composed through the existing reply channel; answered
  turns persist `GroundingReport`, `FallbackOf`, `ConfirmedOfferId`; failures return a fresh offer.
  Acceptance: AC-9, AC-10, AC-11, AC-15; concurrency test with two confirms (one model call, one charge); confirm
  after cancel; cancel after confirm; replay with the same idempotency key; expired; deleted conversation; price change.

### 7. Telemetry

- [x] **T11 — Cost telemetry.** `appsettings.json` `ModelPrices`: `gpt-6.2-sol` row ($2 / $0.10 / $10 per 1M) and a
  per-search price ($0.01); estimated USD per analysis from usage facts (pure function, monitoring only); structured
  log fields and metrics (tier, tokens incl. reasoning, searches, cost, latency, truncation, grounding result,
  disposition step result); alert threshold setting for cost per analysis.
  Acceptance: AC-17; a test proves no billing path reads the cost value.

### 8. Clients

- [x] **T12 — Web and PWA.** `lib/chat.functions.ts` and `lib/chat/use-chat-thread.ts` (`analysisOptions`,
  `pricingVersion`, hint mapping); new `components/app/analysis-settings.tsx` (segmented control, Globe toggle, cost
  sentence, `localStorage` with try/catch); `prompt-input.tsx`, `mobile/mobile-composer.tsx` (bar when the hint is
  available); `message-list.tsx` and the mobile answer card (offer card, «تحلیل کن» / «انصراف», lock after click,
  disabled with purchase link on insufficient balance, Max text, sources list). Check server-function and fetch
  timeouts for 90-second calls.
  Acceptance: Vitest for the controls, cost text from the hint, offer actions sending the right ids and options, no
  amount in the request; light and dark; 320 px. Run from the uppercase `C:` path; lint with `endOfLine` auto.
  Done 2026-11-20: `lib/chat/analysis-choice.ts` (prices from the hint, options, cost text, storage),
  `lib/chat/live-analysis-hint.ts` (the live hint kept per conversation for its answer; the new-chat page hands it to
  the opened conversation), `lib/chat/external-sources.ts`, `components/app/analysis-settings.tsx`,
  `components/app/answer-sources.tsx` (links replace the plain «منابع» text block on screen; http(s) only).
  Browser check `npm run test:analysis-settings-browser` (light/dark × 1280/360/320, no overflow, 44 px targets),
  evidence in `evidence/browser/`. Timeouts: no frontend or server-function limit below 90 s (the API client and
  worker fetch have none); the production reverse proxy (not in the repository) must allow at least 120 s — T16
  runbook. Gap found: the hint is not on reloaded conversations (OQ-113).
### 8a. Amendment: writing, per-section sources and score reason (owner, 2026-11-20)

Approved by the owner on 2026-11-20 («طرح مورد تایید است»), together with the four recommendations: an amendment of
this change (not a new folder); a verbal comparison such as «تقریباً دو برابر» only when both figures are in the report
and the comparison is obvious; OQ-108 closed with (b); the period stays in the score line (OQ-105). Scope is only the
three items below; model, depths, web switch, defaults, prices, 163 confirmation, grounding, routing and the legacy
(flag-off) path are unchanged. T13 runs after this amendment.

- [x] **A1 — Persian writing rules (prompt only).** `GroundedAnalysis.PersianWritingRules` in both analysis prompts
  (`ReportDiscussionContextBuilder.AnalysisSystemPromptFor`, `SymbolGroundedFallbackPromptBuilder.AnalysisSystemPromptFor`):
  present perfect for changes whose result shows in the report where natural, simple past where it fits, no mechanical
  conversion, natural capital-market Persian, no translated structures or repetition, meaning and numbers unchanged; the
  owner's before/after pairs as style examples whose numbers are not data. No rule-based rewriting.
  Acceptance: prompt tests for both features, both depths, web on and off; legacy prompts unchanged.
- [x] **A2 — Score reason.** `scoreReason` (string or null) in both analysis schemas and replies; `ScoreRules` asks for
  one sentence of the main strengths and weaknesses, from this analysis, without numbers, weights or sub-scores;
  `ScoreBlock` writes it right after the score line and before the disclaimer; a reason with a digit or an empty one is
  left out and the score stays. Acceptance: schema and parse tests; handler tests for 162 and 163 (order, omitted
  reason, no score).
- [x] **A3 — Sources at the end of their own section (web).** `WebRules`: `title` is the publisher or a short title.
  Frontend `lib/chat/external-sources.ts`: per paragraph or list item, the `[Wn]` markers are removed and that item ends
  with «منابع: » and links to its own verified sources (no duplicates, http(s) only, long titles shortened); the
  aggregated list and the plain «منابع» block are not shown on screen. Telegram keeps the text block.
  Acceptance: Vitest for per-item links, duplicates, unknown marker, unsafe URL, long title, no sources = no links.
- Done 2026-11-20. A1/A2: `AnalysisWritingAndScoreReasonTests` (prompt sections for both features × four choices,
  legacy prompt unchanged, both schemas require `scoreReason`, parsers, `ScoreBlock` order and omission); integration
  `OverallAnalysis_ShowsTheScoreReason_RightAfterTheScore` (163) and `BalanceSheet_ShowsNoScoreReason` (162). Unit
  filter 147 passed; 162/163/159 integration 50 passed. A3: `withInlineSources` replaces `withoutSourcesBlock`; the
  `AnswerSources` list is deleted. One source reads «— منبع: …», several «— منابع: …» (separated by «،»); a table's
  sources go in a line under the table; markers with Persian digits are read; link text and URL are escaped. Applied
  after `replaceProviderDisplayNames` so URLs are never altered. `external-sources.test.ts` (11) and the updated
  MessageList test pass; the browser check (light/dark, 1280/360/320 px) asserts per-item links and no `[Wn]`.
  Frontend 575 passed, 3 failed (pre-existing: terminology, markdown 16-row, product-trend failed message) plus the 3
  `scripts/*.browser.test.mjs` environment suites; type-check at the 37-error baseline; lint clean; build green.
- [x] **T13 — Telegram.** `TelegramAssistantResponseRenderer.cs`: price line under discussable reports; 163 offer with
  an inline keyboard (four tiers labelled with credits, «انصراف»). `TelegramAiAssistantAdapter.cs`: `ga1:` callback →
  `AiQueryRequest` with the action id and `analysisOptions`; `answerCallbackQuery`; callback data ≤ 64 bytes. Check the
  gateway timeout for 90-second calls.
  Acceptance: AC-8 and AC-19 for Telegram; a typed «بله» never confirms; renderer and adapter tests.
- Done 2026-11-20. `Infrastructure/Authentication/TelegramGroundedAnalysis.cs`: price line «پرسش تحلیلی درباره‌ی این
  گزارش: عمیق با وب، ۲۵ اعتبار» at the defaults (OQ-111 a; Max: «… با اشتراک Max اعتباری کسر نمی‌شود.»); offer buttons
  «تحلیل کن — {tier} ({credits})» × 4 and «انصراف», so the offer text («تا «تحلیل کن» را نزنید …») stays the same on
  every client; on a short balance only tiers cheaper than the default stay, with «موجودی برای تحلیل … کافی نیست. برای
  خرید اعتبار /plans را بفرستید.» (the backend still refuses what the balance cannot cover). Callback data
  `ga1:{offerId:N}:{s0|s1|d0|d1|x}` = 39 bytes. The renderer adds the line, note and buttons to the last part of every
  rendering (a photo caption without room gets one more part). The adapter maps a button to `SuggestedActionId`
  `ga1:{id}:confirm|cancel` plus `AnalysisOptions` (depth, web; no pricingVersion — the offer stores it), message
  «تحلیل کن» / «انصراف», the same billing operation id scheme and conversation binding as text; billing runs in the same
  workflow as the web. The gateway answers an offer button before the primary call (other callbacks unchanged).
  Tests: `TelegramGroundedAnalysisTests` (callback round trip and ≤ 64 bytes, malformed data, price line, Max, short
  balance, renderer), `TelegramAiAssistant089Tests` (four tiers, cancel, malformed refused without AI call, replay runs
  once, typed «بله» / «تحلیل کن» never confirm), `TelegramGateway130Tests` (answer order). Telegram unit tests 194
  passed, 2 failed (both in `specs/test-baseline.md`). Timeout check: the gateway call to the API is limited by
  `TelegramGateway:RequestTimeoutSeconds` (compose default 40 s) and updates are handled one at a time — OQ-114, not
  changed here.

### 8b. Amendment A4: less conservative analysis prompts (owner, 2026-11-20)

Approved by the owner on 2026-11-20 («طرح مورد تایید است؛ D1 a، D2 b، D3 b، D4 a»), Gate A and Gate B together for this
amendment. Scope: only the analysis prompt sections of 162 and 163 (they are shared), the Deep layout length and one
logging override. Model, reasoning effort, `MaxOutputTokens`, `max_tool_calls`, JSON schemas, numeric and external
grounding, context, billing, prices, 163 confirmation, routing and the legacy (flag-off) prompt are unchanged.

- [x] **A4.1 — Research mindset.** `GroundedAnalysis.ResearchMindset` (explain the economics, find the main changes,
  industry knowledge, hypotheses tested against evidence, sustainability, hidden risks) in both analysis prompts;
  `AnalysisDimensions` gains the owner's guiding questions for an overall analysis (not a checklist).
- [x] **A4.2 — Number rule.** `NumberRules`: write only numbers from a fact, a calculation or a cited source; never
  invent, estimate, forecast or round one, never replace a report number; "this rule limits the numbers you write, not
  your reasoning" — relationships are explained in words when no calculation exists.
- [x] **A4.3 — Evidence classes.** Financial fact / (external information) / analytical inference / economic
  hypothesis; inferences and hypotheses are expected; a hypothesis may say what data would confirm it (the owner's
  gross-margin example, no figures); scenarios are conditional on named economic factors, never a forecast number.
- [x] **A4.4 — Web use by depth.** `GroundedAnalysis.WebSearchUseRule(depth)` before the shared `WebRules` (source
  rules unchanged, plus "if nothing reliable is found, continue with the report data and invent nothing"). Deep: web
  research is part of the analysis; a report with enough numbers is no reason to skip it; targeted, non-repeated
  queries; narrow questions search only when it clearly helps. Simple (D3 b): optional, without the old sentence "do
  not search when the report alone answers the question".
- [x] **A4.5 — No web (D1 a).** `NoWebRules`: well-established industry knowledge may frame inferences and hypotheses;
  no outside news, events, dates or figures; never claim to have checked online or current information.
- [x] **A4.6 — Deep layout (D2 b).** 5 to 8 items, at most five sentences each, an overall analysis within about 600 to
  1,000 words; narrow questions unchanged (2 to 4 items).
- [x] **A4.7 — Logging (D4 a).** `appsettings.json` Serilog override
  `FinancialCopilot.Infrastructure.AI.GroundedAnalysis: Information`, so the «Grounded analysis call measured» line is
  written (the compose `api` service already sets the default level to Information).
- [x] **A4.8 — Tests.** `AnalysisResearchPromptTests` (both features × four choices: mindset, guiding questions, number
  ban kept, inference rule, no old skip sentence; Deep+web research rule; Simple+web optional; no-web industry rule and
  no web claim; example without digits; legacy prompt without the new rules; Deep layout);
  `GroundedAnalysisPromptTests` and `SymbolGroundedFallback163Tests` updated. 171 prompt, grounding and report
  discussion unit tests passed; 162/163/159 integration 58 passed.
- [x] **A4.9 — Live check** (owner's steps 4–6): real `gpt-6.2-sol`, فولاد quarterly income statement from the
  Development database, old and new prompt; results in `evaluation-a4.md`.
  Done 2026-11-20: 15 calls. Deep+web new: searched 6/6, valid cited source 5/6, 638–708 words, 41–54 s, about
  $0.09–0.10; old: searched 2/5 (162: 0/2), cited 0/5, 406–528 words. All 15 grounded (0 unverified numbers). Found
  BUG-036 (inline citation remnant «(domain)»), not fixed; sources are secondary sites, not codal.ir. The temporary
  harness was deleted.

### 9. Verification and documentation

- [x] **T14 — Regression.** Full backend and frontend suites (see Tests).
- Done 2026-11-20. Backend Release build: 0 warnings. Unit: 3,920 passed, 32 failed, all 32 in
  `specs/test-baseline.md` (run after A4). Architecture: 23/23. Integration (no baseline exists): the full suite was
  run on this branch and on the pre-change commit `e98356f8` in a temporary worktree, both with the same flags
  (`ReportDiscussion`, `SymbolGroundedFallback`, `GroundedAnalysis` off as committed; `Billing:TahlilAppTopUps` off as
  on this machine) — 198 vs 197 failures; every failure on this branch also fails at `e98356f8` except
  `TahlilAppTopUpPostgreSqlTests.Concurrent_invoice_identity_conflict…`, which passes on re-run (flaky, as
  `OwnedIdentityEndpointTests.Refresh_RotatesCookie…`, which failed only at `e98356f8`). The common failures are
  environment-bound (invalid local SMTP options in fixtures, PostgreSQL-backed suites, Feature 148/143 E2E). The 57 new
  integration tests of this change pass; with the developer's local flags on, `ReportDiscussion162ApiFactory` now pins
  `GroundedAnalysis:Enabled=false` so its 8 tests pass. Frontend: 575 passed, 3 pre-existing failures plus the 3
  `scripts/*.browser.test.mjs` environment suites; type-check at the 37-error baseline; lint and build green.
- [ ] **T15 — Live evaluation (measurement only).** Development, real `gpt-6.2-sol`, both flags on locally. Scenarios:
  قمرو quarterly report (simple / deep × web off / on), monthly sales, balance sheet, a 163 symbol question with
  confirm and with cancel, insufficient credit, repeated confirm, provider failure (invalid key), search failure,
  external number with and without support, settings change before confirm, continuation after 163, Max account,
  Telegram. Each analysis three times. Record the nine quality criteria of the owner's rubric, actual cost, latency,
  tokens including reasoning, searches, truncation and grounding results in `evaluation.md` in this folder. Prices are
  not an output of this task.
- [x] **T16 — Docs.** `docs/domain-glossary.md` (عمق تحلیل، جستجوی وب، تعرفهٔ ثابت تحلیل، پیشنهاد تحلیل);
  `docs/providers/` OpenAI note (model, web search, budgets); operations runbook entry "changing analysis prices is an
  owner decision" and "the reverse proxy and `TELEGRAM_GATEWAY_REQUEST_TIMEOUT_SECONDS` must allow at least 120 s
  (OQ-114)"; 162 and 163 `user-story.md` pointers to this change; `specs/README.md`; `CHANGELOG.md`
  `[Unreleased]` Persian line through the release manager.
- Done 2026-11-20. `docs/domain-glossary.md` (Grounded Analysis, Analysis Depth, Web Search, Fixed Analysis Tariff,
  Analysis Offer, Score Reason); `docs/providers/openai-grounded-analysis.md` (model, web search payload, budgets, cost
  monitoring, partial spike); `docs/grounded-analysis-operations.md` (flags and rollback, launch steps including
  `TELEGRAM_GATEWAY_REQUEST_TIMEOUT_SECONDS=120` and the ≥ 120 s reverse proxy per OQ-114 (a), prices as an owner
  decision, monitoring); link in `docs/ai-provider-switching.md`; pointers in the 162 and 163 `user-story.md` and
  `specs/README.md`; `CHANGELOG.md` `[Unreleased]` (Added + Changed) by the release manager, not committed.

- [x] **BUG-036 — Citation remnant.** Owner approved the fix 2026-11-20 (no design change). `GroundedAnswerLayout.Normalize` removes OpenAI's inline citation `([domain](url))` and an http(s) link right before `[Wn]`; other links keep their text as before. Verified with `WebCitationRemnantTests` and the 162/163 unit suites (221 passed). Not verified live: the owner asked that no OpenAI call be made by the agent.

## Tests

- [ ] Unit: transport payload and parsing (T02), options and pricing independence (T03), settlement and modes (T04),
  fingerprint and payload compatibility (T05), prompt content and schemas (T06), external evidence (T07), 162 flow
  (T08), 163 offer, confirm, cancel, replay and concurrency (T09, T10), cost estimate isolation (T11), Telegram renderer
  and callback (T13).
- [ ] Integration: `ReportDiscussion162EndpointTests`, `SymbolGroundedFallback163EndpointTests` extended for options,
  hints, offers and billing; billing replay and unlimited.
- [ ] Architecture: `AI/GroundedAnalysis`, `AI/ReportDiscussion`, `AI/SymbolGroundedFallback` reference no
  Infrastructure, EF or HTTP type; V1 does not reference them; pricing and profiles do not reference each other.
- [ ] Unchanged and green: `ReportDiscussion162Tests`, `SymbolGroundedFallback163Tests`, `GroundedAnswerLayoutTests`,
  numeric grounding and display-unit tests, Feature 141, 144, 150, 154 suites, billing suites.
- [ ] Frontend (Vitest): analysis settings, offer card, request contract, mobile answers.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md`.
- [ ] No database migration; `dotnet ef migrations has-pending-model-changes` reports none for the conversation and billing contexts.
- [ ] In `src/frontend`: `npm run lint`, `npm run test`, `npm run build` green; `npm run typecheck` adds no error beyond `specs/test-baseline.md`.
- [ ] Acceptance criteria AC-1 … AC-19 in `user-story.md` checked.
- [ ] `spike-notes.md` (T00) and `evaluation.md` (T15) written.
- [ ] Reference docs updated (T16).
- [ ] Persian user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] Prices in configuration are exactly 8 / 12 / 16 / 25.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects → `specs/bugs.md`.
- [ ] Gate B does not authorize a release, a deploy or a production configuration change.
