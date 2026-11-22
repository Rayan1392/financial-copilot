# Grounded analysis — operations runbook

Operations notes for Features 162 and 163 change `sol-deep-analysis-web-search`
(`specs/162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search/`). Provider details:
`docs/providers/openai-grounded-analysis.md`. Terms: `docs/domain-glossary.md` (Grounded Analysis, Analysis Depth,
Web Search, Fixed Analysis Tariff, Analysis Offer).

## Flags

| Setting | Effect |
|---|---|
| `GroundedAnalysis:Enabled` | On: Report Discussion answers with `gpt-6.2-sol` at the four tiers, and Feature 163 shows an Analysis Offer. Off: the earlier Report Discussion path runs (current model, 2 credits), and a 163 turn keeps its original non-answer with no offer. Default `false`. |
| `ReportDiscussion:Enabled` | Feature 162 as a whole. |
| `SymbolGroundedFallback:Enabled` | Feature 163 as a whole. Off leaves the turn unchanged. No path runs 163 without the user's confirmation. |

Rollback is a flag change and a restart. Stored conversations stay readable. An offer created while the flag was on
cannot run after the flag is turned off: pressing «تحلیل کن» returns the expired-offer reply at 0 credits, and the
user asks again.

## Company data pack

Change `local-company-data-context`
(`specs/162-context-aware-financial-report-follow-up/changes/local-company-data-context/`, BUG-042). Each analysis, in
both 162 and a confirmed 163 offer, sends the model Sapio's own data for the company instead of one report:

- The report the question is about is the primary section. In 162 it is the report the user saw; in 163 it is the
  quarterly income statement.
- The supporting sections are the quarterly income statement, the balance sheet, the cash-flow statement and the
  monthly production and sales trend. Each is the same data the chat answer for that report returns.
- A section that is disabled, has no data, fails, times out or belongs to another company is left out. Its name and
  reason go in `<not_available>`.
- The cash-flow section follows `CashFlowReport:Enabled`.
- The prompt tells the model not to search the web for figures that `<company_data>` holds or lists as not available.
  The other search targets stay. Product-level detail and input and product prices may still come from the web (OQ-121).

The pack is read after the routing step answers `Answered`. A hand-back reads no section and calls no analysis.

The sections are read one after another on the request scope. Each read has `SectionTimeoutSeconds` (3 s), and all
reads together have `TotalTimeoutSeconds` (8 s).

**Size.** "Too large" is no longer refused below the ceiling. The pack is fitted to the profile's `InputTokenTarget`
by shrinking it in this order:

1. Shorten the supporting sections to 2 periods (12 months for the monthly trend).
2. Shorten the conversation history.
3. Drop supporting sections: cash flow first, then balance sheet, monthly trend and income statement.
4. Drop the oldest periods of the primary section.

The analysis is refused, at 0 credits, only when even the smallest form is above `MaxInputTokens`. The diagnostics
reason is then `over_max_input_tokens`.

| Setting | Default | Effect |
|---|---|---|
| `GroundedAnalysis:DataPack:Enabled` | `true` | Off: only the primary report is sent, as before this change (still under the larger budgets below). |
| `GroundedAnalysis:DataPack:SectionTimeoutSeconds` | `3` | Limit for one section read (1–30). |
| `GroundedAnalysis:DataPack:TotalTimeoutSeconds` | `8` | Limit for all section reads together (between the section limit and 60). |
| `GroundedAnalysis:Profiles:Simple:InputTokenTarget` | `24000` | Budget the pack is fitted to (was 6,000). |
| `GroundedAnalysis:Profiles:Deep:InputTokenTarget` | `40000` | Budget the pack is fitted to (was 10,000). |
| `GroundedAnalysis:MaxInputTokens` | `120000` | Hard ceiling; must be at least each profile's target. |

The code defaults are the production values, so the server `.env` needs none of these (BUG-041). For rollback, set
`GroundedAnalysis__DataPack__Enabled=false` in the server `.env` and restart the api. The prices do not change: a
larger input raises the cost of an analysis, not its credits. Expect `grounded_analysis.cost_alerts` (0.20 USD) to fire
more often, and review the threshold with the owner after the first week.

## Production configuration

The production image has no `appsettings.json`: `docker/api.Dockerfile` removes it. Production reads only
`docker/appsettings.Production.json` and the server `.env`, which the api container receives through `env_file`. So the
flag and the prices must be in the server `.env` (BUG-041):

```
GroundedAnalysis__Enabled=true
GroundedAnalysisPricing__Credits__SimpleNoWeb=8
GroundedAnalysisPricing__Credits__SimpleWeb=12
GroundedAnalysisPricing__Credits__DeepNoWeb=16
GroundedAnalysisPricing__Credits__DeepWeb=25
AiModelProviders__Providers__0__Capabilities=ChatCompletion,StructuredOutput,ToolCalling,Streaming,UsageReporting,WebSearch
```

- The technical options (model, profiles, web search, disposition, telemetry) default in code to the values in
  `appsettings.json`. Set them in `.env` only to change them, for example `GroundedAnalysis__Profiles__Deep__TimeoutSeconds`.
- The prices have no code default. With the flag on and a price missing or 0, the api refuses to start and restarts
  in a loop (BUG-041, 2026-11-22). Set the prices before the flag.
- Check what the container received: `docker compose exec api printenv | grep -E 'GroundedAnalysis|Providers__0__Capabilities'`.
- `GroundedAnalysisProductionConfigurationTests` checks that `.env.example` and the image defaults stay valid and keep
  the prices of `appsettings.json`. It cannot see the server `.env`.

## Before turning `GroundedAnalysis:Enabled` on in production

1. Run the provider spike (T00) and the live evaluation (T15). Their results are in `spike-notes.md` and
   `evaluation.md` in the change folder.
2. **Timeouts.** A Deep + web analysis takes 30–90 s.
   - Telegram: set `TELEGRAM_GATEWAY_REQUEST_TIMEOUT_SECONDS=120` on the server. The compose default is 40 s (OQ-114,
     answered (a) on 2026-11-20). With a lower value the gateway drops the call and retries the update, and the user
     may never get the answer.
   - Web: the production reverse proxy in front of the frontend and the API (not in this repository) must allow at
     least 120 s for a request. The frontend and its server functions set no lower limit.
   - The OpenAI provider timeout (120 s) stays above the Deep profile timeout (90 s).
3. Check that `GroundedAnalysisPricing:Credits` holds the approved amounts (8 / 12 / 16 / 25) in the server `.env`.
4. Check that the OpenAI registration lists the `WebSearch` capability (`AiModelProviders__Providers__0__Capabilities`)
   and that the key has access to `gpt-6.2-sol`.

The Telegram gateway still handles updates one at a time. While an analysis runs, other Telegram users wait. Per-chat
concurrent processing is in `specs/backlog.md`.

## Prices are an owner decision

- `GroundedAnalysisPricing:Credits` (`SimpleNoWeb`, `SimpleWeb`, `DeepNoWeb`, `DeepWeb`) is the only source of the
  analysis price. Clients show what the backend returns; a request never carries an amount.
- **Change these values only on the owner's written decision**, and record the decision and its date in the change
  folder or a follow-up spec. Profiles, token budgets, model prices and the cost alert never change a price.
- `PricingVersion` is derived from the four amounts. After a change, a client still holding the old version gets a
  fresh price instead of an analysis at the old price. An open offer re-checks the version when it is confirmed.
- Max users are not charged. Non-answers (hand-back, missing data, grounding failure, truncation, provider failure,
  cancel) cost 0.

## Monitoring

| Signal | Meaning |
|---|---|
| `grounded_analysis.runs` | Analysis calls per tier, status and result. |
| `grounded_analysis.cost_usd`, `grounded_analysis.cost_alerts` | Estimated cost per analysis, and runs above `GroundedAnalysis:Telemetry:CostAlertUsd` (0.20 USD). Monitoring only. |
| `grounded_analysis.latency_ms` | Watch the tail against the 120 s gateway and proxy limits. |
| `grounded_analysis.output_tokens`, `grounded_analysis.reasoning_tokens` | Pressure on `MaxOutputTokens`; truncation is a 0-credit failure. |
| `grounded_analysis.web_search_calls` | Searches per analysis (at most 1 for Simple, 2 for Deep). |
| `grounded_analysis.data_pack_sections` | Pack sections per `feature` (162/163), `section` and `outcome` (`sent`, or the reason the section is missing: `disabled`, `no_data`, `timeout`, `error`, `other_company`, `time_budget`, `trimmed`). A rise in `timeout` or `error` points to the section's query, not to the model. |
| `grounded_analysis.codal_citations` | Verified web sources on codal.ir per analysis, tagged by `feature`. A high count means the model still searches for data Sapio holds (OQ-122: counted, never removed). |

All metrics are tagged with `tier`, `status` (completion status), `result` and `model`. The log line «Grounded
analysis call measured» carries the same values plus input, cached-input, output and reasoning tokens, the number of
web searches and consulted sources, the estimated cost, the elapsed time and the correlation id. It never carries
prompts, keys or user text. Telemetry failures are logged and never affect the turn.

The log line «Grounded analysis data pack» is written for each analysis. It carries:

- `Feature`, `SectionsSent` and `NotAvailable` (kind:reason);
- `DegradationStep`: 0 when the whole pack fitted;
- `ShortHistory`;
- `SectionReads`: kind:outcome:milliseconds for each read;
- `CodalCitations` and the correlation id.

It carries section names, reason codes, counts and times only, never a value.
