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
3. Check that `GroundedAnalysisPricing:Credits` holds the approved amounts (8 / 12 / 16 / 25).
4. Check that the OpenAI registration lists the `WebSearch` capability and that the key has access to `gpt-6.2-sol`.

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

All metrics are tagged with `tier`, `status` (completion status), `result` and `model`. The log line «Grounded
analysis call measured» carries the same values plus input, cached-input, output and reasoning tokens, the number of
web searches and consulted sources, the estimated cost, the elapsed time and the correlation id. It never carries
prompts, keys or user text. Telemetry failures are logged and never affect the turn.
